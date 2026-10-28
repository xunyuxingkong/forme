using System.Buffers.Binary;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace Forme.App;

internal sealed class GlbPetAsset
{
    public Dictionary<(int Joint,int Material),PetMeshBatch> Batches {get;}=[];
    public List<PetSkinnedBatch> SkinnedBatches {get;}=[];
    public Dictionary<int,GlbPetJoint> Joints {get;}=[];
    public Dictionary<int,PetSkin> Skins {get;}=[];
    public Dictionary<int,string> HumanoidBones {get;}=[];
    public Dictionary<string,GlbPetClip> Clips {get;}=new(StringComparer.OrdinalIgnoreCase);
    public PetMaterial[] Materials {get;private set;}=[];
    public static Material Fallback {get;}=new DiffuseMaterial(Brushes.White);

    public static GlbPetAsset Read(string resourceName,string? excludedNodeName=null)
    {
        using Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)??throw new InvalidDataException($"Embedded model resource is missing: {resourceName}");using var memory=new MemoryStream();stream.CopyTo(memory);byte[] data=memory.ToArray();
        return ReadData(data,resourceName,excludedNodeName);
    }
    public static GlbPetAsset ReadFile(string path)=>ReadData(File.ReadAllBytes(path),path,null);
    private static GlbPetAsset ReadData(byte[] data,string resourceName,string? excludedNodeName)
    {
        if(data.Length<20||BinaryPrimitives.ReadUInt32LittleEndian(data)!=0x46546C67||BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4))!=2||BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8))!=data.Length)throw new InvalidDataException($"Invalid GLB header: {resourceName}");
        JsonDocument? document=null;byte[]? binary=null;int offset=12;
        while(offset+8<=data.Length){int length=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)));uint type=BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset+4));offset+=8;if(length<0||offset+length>data.Length)throw new InvalidDataException("Invalid GLB chunk.");if(type==0x4E4F534A)document=JsonDocument.Parse(data.AsMemory(offset,length));else if(type==0x004E4942)binary=data.AsSpan(offset,length).ToArray();offset+=length;}
        using(document)
        {
            if(document is null||binary is null)throw new InvalidDataException("GLB requires JSON and binary chunks.");JsonElement root=document.RootElement;var result=new GlbPetAsset();result.Materials=LoadMaterials(root,binary);JsonElement nodes=root.GetProperty("nodes"),meshes=root.GetProperty("meshes"),accessors=root.GetProperty("accessors"),views=root.GetProperty("bufferViews");int scene=root.TryGetProperty("scene",out var sceneIndex)?sceneIndex.GetInt32():0;
            if(root.TryGetProperty("extensions",out var extensions)&&extensions.TryGetProperty("VRM",out var vrm)&&vrm.TryGetProperty("humanoid",out var humanoid)&&humanoid.TryGetProperty("humanBones",out var humanBones))
                foreach(var bone in humanBones.EnumerateArray())result.HumanoidBones[bone.GetProperty("node").GetInt32()]=bone.GetProperty("bone").GetString()!;
            if(root.TryGetProperty("skins",out var skinDefs))
            foreach(var skinDef in skinDefs.EnumerateArray())
            {
                int id=result.Skins.Count;int[] joints=skinDef.GetProperty("joints").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
                Matrix4x4[] inverseBind=skinDef.TryGetProperty("inverseBindMatrices",out var inverseBindAccessor)?ReadMatrices(inverseBindAccessor.GetInt32()):Enumerable.Repeat(Matrix4x4.Identity,joints.Length).ToArray();
                if(inverseBind.Length!=joints.Length)throw new InvalidDataException("Skin joint and inverse-bind counts do not match.");
                result.Skins.Add(id,new(id,joints,inverseBind));
            }
            var animatedNodes=new HashSet<int>();var skinJointNodes=new HashSet<int>();
            if(root.TryGetProperty("skins",out var rigSkins))foreach(var rigSkin in rigSkins.EnumerateArray())foreach(var joint in rigSkin.GetProperty("joints").EnumerateArray())skinJointNodes.Add(joint.GetInt32());
            if(root.TryGetProperty("animations",out var animations))
            foreach(var animation in animations.EnumerateArray())
            {
                var tracks=new List<GlbPetTrack>();
                foreach(var channel in animation.GetProperty("channels").EnumerateArray())
                {
                    var target=channel.GetProperty("target");int nodeId=target.GetProperty("node").GetInt32();string path=target.GetProperty("path").GetString()!;
                    if(path is not ("rotation" or "translation" or "scale"))throw new InvalidDataException("Unsupported pet animation channel.");
                    var sampler=animation.GetProperty("samplers")[channel.GetProperty("sampler").GetInt32()];string interpolation=sampler.TryGetProperty("interpolation",out var method)?method.GetString()!:"LINEAR";
                    if(interpolation is not ("LINEAR" or "STEP"))throw new InvalidDataException("Unsupported pet animation interpolation.");
                    float[] times=ReadFloats(sampler.GetProperty("input").GetInt32(),1),values=ReadFloats(sampler.GetProperty("output").GetInt32(),path=="rotation"?4:3);
                    int components=path=="rotation"?4:3;
                    if(times.Length<2||values.Length!=times.Length*components||times[^1]<=0||times.Any(t=>!float.IsFinite(t))||values.Any(v=>!float.IsFinite(v)))throw new InvalidDataException("Invalid pet animation keys.");
                    for(int key=1;key<times.Length;key++)if(times[key]<=times[key-1])throw new InvalidDataException("Pet animation key times must increase.");
                    tracks.Add(new(nodeId,path,times,values,interpolation=="STEP"));animatedNodes.Add(nodeId);
                }
                result.Clips.Add(animation.GetProperty("name").GetString()!,new(tracks.ToArray()));
            }
            foreach(var item in root.GetProperty("scenes")[scene].GetProperty("nodes").EnumerateArray())Visit(item.GetInt32(),Matrix4x4.Identity,-1);
            if(result.Batches.Count==0&&result.SkinnedBatches.Count==0)throw new InvalidDataException("GLB has no triangle meshes.");return result;

            void Visit(int index,Matrix4x4 parent,int joint)
            {
                JsonElement node=nodes[index];if(excludedNodeName is not null&&node.TryGetProperty("name",out var name)&&name.GetString()==excludedNodeName)return;
                Matrix4x4 local=Local(node),world=local*parent;
                if(animatedNodes.Contains(index)||skinJointNodes.Contains(index))
                {
                    if(!Matrix4x4.Decompose(local,out var scale,out var rotation,out var translation))throw new InvalidDataException("Invalid animated node transform.");
                    var restWorld=world*(joint<0?Matrix4x4.Identity:result.Joints[joint].RestWorld);string jointName=node.TryGetProperty("name",out var jointNameElement)?jointNameElement.GetString()??index.ToString():index.ToString();
                    result.Joints.Add(index,new(index,joint,jointName,scale,rotation,translation,parent,restWorld));joint=index;world=Matrix4x4.Identity;
                }
                if(node.TryGetProperty("mesh",out var meshIndex))foreach(var primitive in meshes[meshIndex.GetInt32()].GetProperty("primitives").EnumerateArray())
                {
                    if(primitive.TryGetProperty("mode",out var mode)&&mode.GetInt32()!=4)continue;int mat=primitive.TryGetProperty("material",out var m)?m.GetInt32():-1;
                    JsonElement attrs=primitive.GetProperty("attributes");var positions=ReadVectors(attrs.GetProperty("POSITION").GetInt32(),3);int normalId=attrs.TryGetProperty("NORMAL",out var normalElement)?normalElement.GetInt32():-1;var normals=normalId>=0?ReadVectors(normalId,3):Array.Empty<Vector3>();int uvId=attrs.TryGetProperty("TEXCOORD_0",out var uvElement)?uvElement.GetInt32():-1;var uvs=uvId>=0?ReadVectors(uvId,2):Array.Empty<Vector3>();int[] indices=primitive.TryGetProperty("indices",out var indexElement)?ReadIndices(indexElement.GetInt32()):Enumerable.Range(0,positions.Length).ToArray();
                    if(node.TryGetProperty("skin",out var skinElement)&&attrs.TryGetProperty("JOINTS_0",out var jointElement)&&attrs.TryGetProperty("WEIGHTS_0",out var weightElement))
                    {
                        int[] jointIndices=ReadJoints(jointElement.GetInt32());Vector4[] weights=ReadVector4(weightElement.GetInt32());
                        if(jointIndices.Length!=positions.Length*4||weights.Length!=positions.Length)throw new InvalidDataException("Invalid skinned pet vertex attributes.");
                        // VRM primitives often share a full POSITION accessor. Skin only
                        // vertices referenced by this primitive, rather than duplicating
                        // the entire character for every material (Hu Tao: 209016 -> 18176).
                        var used=indices.Distinct().ToArray();if(used.Any(i=>(uint)i>=(uint)positions.Length))throw new InvalidDataException("Invalid skinned mesh indices.");
                        var remap=used.Select((old,index)=>(old,index)).ToDictionary(x=>x.old,x=>x.index);
                        result.SkinnedBatches.Add(new(skinElement.GetInt32(),mat,world,used.Select(i=>positions[i]).ToArray(),used.Select(i=>i<normals.Length?normals[i]:Vector3.UnitY).ToArray(),used.Select(i=>i<uvs.Length?new Vector2(uvs[i].X,uvs[i].Y):Vector2.Zero).ToArray(),indices.Select(i=>remap[i]).ToArray(),used.SelectMany(i=>jointIndices.Skip(i*4).Take(4)).ToArray(),used.Select(i=>weights[i]).ToArray()));continue;
                    }
                    var batchKey=(joint,mat);if(!result.Batches.TryGetValue(batchKey,out var batch))result.Batches[batchKey]=batch=new();int start=batch.Positions.Count;
                    if(!Matrix4x4.Invert(world,out var inverse))throw new InvalidDataException("Invalid pet mesh transform.");var normalMatrix=Matrix4x4.Transpose(inverse);
                    foreach(var p in positions)batch.Positions.Add(Vector3.Transform(p,world));if(normals.Length==positions.Length)foreach(var normal in normals){var n=Vector3.TransformNormal(normal,normalMatrix);batch.Normals.Add(n.LengthSquared()>0?Vector3.Normalize(n):Vector3.UnitY);}else batch.Normals.AddRange(Enumerable.Repeat(Vector3.UnitY,positions.Length));if(uvs.Length==positions.Length)foreach(var uv in uvs)batch.Uvs.Add(new(uv.X,uv.Y));else batch.Uvs.AddRange(Enumerable.Repeat(Vector2.Zero,positions.Length));
                    foreach(int triangleIndex in indices)batch.Indices.Add(start+triangleIndex);
                }
                if(node.TryGetProperty("children",out var children))foreach(var child in children.EnumerateArray())Visit(child.GetInt32(),world,joint);
            }
            float[] ReadFloats(int accessorIndex,int components)
            {
                JsonElement accessor=accessors[accessorIndex],view=views[accessor.GetProperty("bufferView").GetInt32()];
                if(accessor.GetProperty("componentType").GetInt32()!=5126)throw new InvalidDataException("Animation keys must be float values.");
                int count=accessor.GetProperty("count").GetInt32(),stride=view.TryGetProperty("byteStride",out var s)?s.GetInt32():4*components,begin=(view.TryGetProperty("byteOffset",out var v)?v.GetInt32():0)+(accessor.TryGetProperty("byteOffset",out var a)?a.GetInt32():0);var values=new float[count*components];
                for(int i=0;i<count;i++)for(int c=0;c<components;c++)values[i*components+c]=BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(binary.AsSpan(begin+i*stride+c*4)));return values;
            }
            Matrix4x4[] ReadMatrices(int accessorIndex)
            {
                float[] values=ReadFloats(accessorIndex,16);var matrices=new Matrix4x4[values.Length/16];
                for(int i=0;i<matrices.Length;i++){int at=i*16;matrices[i]=new(values[at],values[at+1],values[at+2],values[at+3],values[at+4],values[at+5],values[at+6],values[at+7],values[at+8],values[at+9],values[at+10],values[at+11],values[at+12],values[at+13],values[at+14],values[at+15]);}
                return matrices;
            }
            int[] ReadJoints(int accessorIndex)
            {
                JsonElement accessor=accessors[accessorIndex],view=views[accessor.GetProperty("bufferView").GetInt32()];int count=accessor.GetProperty("count").GetInt32(),component=accessor.GetProperty("componentType").GetInt32(),size=component switch{5121=>1,5123=>2,_=>throw new InvalidDataException("Unsupported skin joint component.")},stride=view.TryGetProperty("byteStride",out var s)?s.GetInt32():size*4,begin=(view.TryGetProperty("byteOffset",out var v)?v.GetInt32():0)+(accessor.TryGetProperty("byteOffset",out var a)?a.GetInt32():0);var output=new int[count*4];
                for(int i=0;i<count;i++)for(int c=0;c<4;c++){int at=begin+i*stride+c*size;output[i*4+c]=component==5121?binary[at]:BinaryPrimitives.ReadUInt16LittleEndian(binary.AsSpan(at));}return output;
            }
            Vector4[] ReadVector4(int accessorIndex)
            {
                JsonElement accessor=accessors[accessorIndex],view=views[accessor.GetProperty("bufferView").GetInt32()];if(accessor.GetProperty("componentType").GetInt32()!=5126)throw new InvalidDataException("Skin weights must be float values.");int count=accessor.GetProperty("count").GetInt32(),stride=view.TryGetProperty("byteStride",out var s)?s.GetInt32():16,begin=(view.TryGetProperty("byteOffset",out var v)?v.GetInt32():0)+(accessor.TryGetProperty("byteOffset",out var a)?a.GetInt32():0);var output=new Vector4[count];
                for(int i=0;i<count;i++){int at=begin+i*stride;output[i]=new(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(binary.AsSpan(at))),BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(binary.AsSpan(at+4))),BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(binary.AsSpan(at+8))),BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(binary.AsSpan(at+12))));}return output;
            }
            Vector3[] ReadVectors(int accessorIndex,int components)
            {
                JsonElement accessor=accessors[accessorIndex];JsonElement view=views[accessor.GetProperty("bufferView").GetInt32()];int count=accessor.GetProperty("count").GetInt32(),component=accessor.GetProperty("componentType").GetInt32(),size=component switch{5126=>4,5123=>2,5121=>1,_=>throw new InvalidDataException("Unsupported GLB vertex component.")},stride=view.TryGetProperty("byteStride",out var s)?s.GetInt32():size*components,begin=view.GetProperty("byteOffset").GetInt32()+(accessor.TryGetProperty("byteOffset",out var a)?a.GetInt32():0);bool normalized=accessor.TryGetProperty("normalized",out var norm)&&norm.GetBoolean();var values=new Vector3[count];
                for(int i=0;i<count;i++){float Read(int c){int at=begin+i*stride+c*size;return component switch{5126=>BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(binary.AsSpan(at))),5123=>normalized?BinaryPrimitives.ReadUInt16LittleEndian(binary.AsSpan(at))/65535f:BinaryPrimitives.ReadUInt16LittleEndian(binary.AsSpan(at)),5121=>normalized?binary[at]/255f:binary[at],_=>0};}values[i]=new(Read(0),components>1?Read(1):0,components>2?Read(2):0);}return values;
            }
            int[] ReadIndices(int accessorIndex)
            {
                JsonElement accessor=accessors[accessorIndex];JsonElement view=views[accessor.GetProperty("bufferView").GetInt32()];int count=accessor.GetProperty("count").GetInt32(),component=accessor.GetProperty("componentType").GetInt32(),size=component switch{5121=>1,5123=>2,5125=>4,_=>throw new InvalidDataException("Unsupported GLB index component.")},begin=view.GetProperty("byteOffset").GetInt32()+(accessor.TryGetProperty("byteOffset",out var a)?a.GetInt32():0),stride=view.TryGetProperty("byteStride",out var s)?s.GetInt32():size;var values=new int[count];for(int i=0;i<count;i++){int at=begin+i*stride;values[i]=component switch{5121=>binary[at],5123=>BinaryPrimitives.ReadUInt16LittleEndian(binary.AsSpan(at)),_=>checked((int)BinaryPrimitives.ReadUInt32LittleEndian(binary.AsSpan(at)))};}return values;
            }
        }
    }

    public static Vector3[] ApplyNormalMap(PetMeshBatch batch,ImageTexture normal,double strength)
    {
        var tan=new Vector3[batch.Positions.Count];var bitan=new Vector3[batch.Positions.Count];
        for(int i=0;i+2<batch.Indices.Count;i+=3)
        {
            int a=batch.Indices[i],b=batch.Indices[i+1],c=batch.Indices[i+2];Vector3 e1=batch.Positions[b]-batch.Positions[a],e2=batch.Positions[c]-batch.Positions[a];Vector2 d1=batch.Uvs[b]-batch.Uvs[a],d2=batch.Uvs[c]-batch.Uvs[a];float divisor=d1.X*d2.Y-d1.Y*d2.X;if(Math.Abs(divisor)<1e-8)continue;float inv=1/divisor;var t=(e1*d2.Y-e2*d1.Y)*inv;var bt=(e2*d1.X-e1*d2.X)*inv;tan[a]+=t;tan[b]+=t;tan[c]+=t;bitan[a]+=bt;bitan[b]+=bt;bitan[c]+=bt;
        }
        var output=new Vector3[batch.Normals.Count];for(int i=0;i<output.Length;i++)
        {
            Vector3 n=Vector3.Normalize(batch.Normals[i]);Vector3 t=tan[i]-n*Vector3.Dot(n,tan[i]);if(t.LengthSquared()<1e-10){output[i]=n;continue;}t=Vector3.Normalize(t);Vector3 b=Vector3.Normalize(Vector3.Cross(n,t));if(Vector3.Dot(b,bitan[i])<0)b=-b;Vector3 map=normal.Sample(batch.Uvs[i]);map.X*= (float)strength;map.Y*=(float)strength;map=Vector3.Normalize(map);Vector3 mapped=t*map.X+b*map.Y+n*map.Z;output[i]=mapped.LengthSquared()>0?Vector3.Normalize(mapped):n;
        }
        return output;
    }

    private static Matrix4x4 Local(JsonElement node)
    {
        if(node.TryGetProperty("matrix",out var matrix)){float[] m=matrix.EnumerateArray().Select(x=>x.GetSingle()).ToArray();return new(m[0],m[1],m[2],m[3],m[4],m[5],m[6],m[7],m[8],m[9],m[10],m[11],m[12],m[13],m[14],m[15]);}
        Vector3 scale=node.TryGetProperty("scale",out var sc)?new(sc[0].GetSingle(),sc[1].GetSingle(),sc[2].GetSingle()):Vector3.One;Vector3 translation=node.TryGetProperty("translation",out var tr)?new(tr[0].GetSingle(),tr[1].GetSingle(),tr[2].GetSingle()):Vector3.Zero;System.Numerics.Quaternion rotation=System.Numerics.Quaternion.Identity;if(node.TryGetProperty("rotation",out var rot))rotation=System.Numerics.Quaternion.Normalize(new(rot[0].GetSingle(),rot[1].GetSingle(),rot[2].GetSingle(),rot[3].GetSingle()));return Matrix4x4.CreateScale(scale)*Matrix4x4.CreateFromQuaternion(rotation)*Matrix4x4.CreateTranslation(translation);
    }
    private static PetMaterial[] LoadMaterials(JsonElement root,byte[] binary)
    {
        JsonElement defs=root.TryGetProperty("materials",out var materialDefs)?materialDefs:default,textures=root.TryGetProperty("textures",out var textureDefs)?textureDefs:default,images=root.TryGetProperty("images",out var imageDefs)?imageDefs:default,views=root.GetProperty("bufferViews");var cache=new ImageTexture?[images.ValueKind==JsonValueKind.Array?images.GetArrayLength():0];var output=new PetMaterial[defs.ValueKind==JsonValueKind.Array?defs.GetArrayLength():0];int i=0;
        foreach(var definition in defs.EnumerateArray())
        {
            JsonElement pbr=definition.GetProperty("pbrMetallicRoughness");float metallic=pbr.TryGetProperty("metallicFactor",out var mf)?mf.GetSingle():1,roughness=pbr.TryGetProperty("roughnessFactor",out var rf)?rf.GetSingle():1,alpha=1;float red=1,green=1,blue=1;if(pbr.TryGetProperty("baseColorFactor",out var color)){red=color[0].GetSingle();green=color[1].GetSingle();blue=color[2].GetSingle();alpha=color[3].GetSingle();}
            ImageTexture? baseColor=pbr.TryGetProperty("baseColorTexture",out var baseIndex)?Texture(baseIndex.GetProperty("index").GetInt32()):null;ImageTexture? mr=pbr.TryGetProperty("metallicRoughnessTexture",out var mrRef)?Texture(mrRef.GetProperty("index").GetInt32()):null;ImageTexture? normal=null;float normalScale=1;if(definition.TryGetProperty("normalTexture",out var normalRef)){normal=Texture(normalRef.GetProperty("index").GetInt32());if(normalRef.TryGetProperty("scale",out var scale))normalScale=scale.GetSingle();}
            Brush diffuse=baseColor is null?new SolidColorBrush(Color.FromArgb(Byte(alpha),Byte(red),Byte(green),Byte(blue))):new ImageBrush(baseColor.Bitmap){Stretch=Stretch.Fill,Opacity=alpha,Viewport=new System.Windows.Rect(0,0,1,1),Viewbox=new System.Windows.Rect(0,0,1,1)};diffuse.Freeze();var surface=new MaterialGroup();surface.Children.Add(new DiffuseMaterial(diffuse));float meanRough=roughness;if(mr is not null)meanRough*=mr.MeanGreen;double specularPower=2+Math.Pow(1-Math.Clamp(meanRough,0,1),2)*126;Brush specBrush=mr is null?new SolidColorBrush(Color.FromRgb(Byte(metallic*.8f+.04f),Byte(metallic*.8f+.04f),Byte(metallic*.8f+.04f))):new ImageBrush(SpecularMap(mr,metallic,roughness)){Stretch=Stretch.Fill,Viewport=new System.Windows.Rect(0,0,1,1),Viewbox=new System.Windows.Rect(0,0,1,1)};specBrush.Freeze();surface.Children.Add(new SpecularMaterial(specBrush,specularPower));surface.Freeze();output[i++]=new(surface,normal,normalScale);
        }
        return output;
        ImageTexture Texture(int textureIndex){if(textures.ValueKind!=JsonValueKind.Array||images.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("GLB material references missing texture data.");int imageIndex=textures[textureIndex].GetProperty("source").GetInt32();if(cache[imageIndex] is {} found)return found;JsonElement view=views[images[imageIndex].GetProperty("bufferView").GetInt32()];int start=view.GetProperty("byteOffset").GetInt32(),length=view.GetProperty("byteLength").GetInt32();using var input=new MemoryStream(binary,start,length,false);var decoder=new BitmapImage();decoder.BeginInit();decoder.CacheOption=BitmapCacheOption.OnLoad;decoder.StreamSource=input;decoder.EndInit();decoder.Freeze();var converted=new FormatConvertedBitmap(decoder,PixelFormats.Bgra32,null,0);converted.Freeze();var pixels=new byte[converted.PixelWidth*converted.PixelHeight*4];converted.CopyPixels(pixels,converted.PixelWidth*4,0);return cache[imageIndex]=new(converted,pixels);}
    }
    private static BitmapSource SpecularMap(ImageTexture mr,float metallic,float roughness)
    {
        byte[] result=new byte[mr.Pixels.Length];for(int i=0;i<result.Length;i+=4){double metal=mr.Pixels[i]*metallic/255d,rough=mr.Pixels[i+1]*roughness/255d;byte value=Byte((float)((.04*(1-metal)+metal)*(1-.75*rough)));result[i]=result[i+1]=result[i+2]=value;result[i+3]=255;}var bitmap=BitmapSource.Create(mr.Bitmap.PixelWidth,mr.Bitmap.PixelHeight,96,96,PixelFormats.Bgra32,null,result,mr.Bitmap.PixelWidth*4);bitmap.Freeze();return bitmap;
    }
    private static byte Byte(float value)=>(byte)Math.Clamp((int)Math.Round(value*255),0,255);
}

internal sealed class PetMeshBatch
{
    public List<Vector3> Positions {get;}=[];public List<Vector3> Normals {get;}=[];public List<Vector2> Uvs {get;}=[];public List<int> Indices {get;}=[];
}
internal sealed record PetSkin(int Id,int[] Joints,Matrix4x4[] InverseBindMatrices);
internal sealed record PetSkinnedBatch(int Skin,int Material,Matrix4x4 MeshTransform,Vector3[] Positions,Vector3[] Normals,Vector2[] Uvs,int[] Indices,int[] Joints,Vector4[] Weights);
internal sealed record GlbPetJoint(int Id,int Parent,string Name,Vector3 Scale,System.Numerics.Quaternion Rotation,Vector3 Translation,Matrix4x4 FixedParent,Matrix4x4 RestWorld)
{
    public Matrix4x4 Pose(GlbPetClip? clip,double seconds,double rotationOffset=0,Vector3 eulerOffsetDegrees=default)
    {
        var scale=Scale;var rotation=Rotation;var translation=Translation;
        if(clip is not null&&clip.Tracks.TryGetValue(Id,out var tracks))
        foreach(var track in tracks)
        {
            Vector4 key=track.Sample(seconds%clip.Duration);
            switch(track.Path)
            {
                case "rotation":rotation=new(key.X,key.Y,key.Z,key.W);break;
                case "translation":translation=new(key.X,key.Y,key.Z);break;
                case "scale":scale=new(key.X,key.Y,key.Z);break;
            }
        }
        if(rotationOffset!=0)rotation=System.Numerics.Quaternion.Normalize(rotation*System.Numerics.Quaternion.CreateFromAxisAngle(Vector3.UnitZ,(float)(rotationOffset*Math.PI/180)));
        if(eulerOffsetDegrees!=Vector3.Zero)rotation=System.Numerics.Quaternion.Normalize(rotation*System.Numerics.Quaternion.CreateFromYawPitchRoll((float)(eulerOffsetDegrees.Y*Math.PI/180),(float)(eulerOffsetDegrees.X*Math.PI/180),(float)(eulerOffsetDegrees.Z*Math.PI/180)));
        return Matrix4x4.CreateScale(scale)*Matrix4x4.CreateFromQuaternion(rotation)*Matrix4x4.CreateTranslation(translation)*FixedParent;
    }
}
internal sealed class GlbPetClip(GlbPetTrack[] tracks)
{
    public double Duration {get;}=tracks.Max(track=>track.Times[^1]);
    public Dictionary<int,GlbPetTrack[]> Tracks {get;}=tracks.GroupBy(track=>track.Node).ToDictionary(group=>group.Key,group=>group.ToArray());
}
internal sealed record GlbPetTrack(int Node,string Path,float[] Times,float[] Values,bool Step)
{
    public Vector4 Sample(double seconds)
    {
        int end=Array.BinarySearch(Times,(float)seconds);if(end>=0)return Key(end);end=~end;
        if(end<=0)return Key(0);if(end>=Times.Length)return Key(Times.Length-1);
        int start=end-1;float blend=Step?0:(float)Math.Clamp((seconds-Times[start])/(Times[end]-Times[start]),0,1);var a=Key(start);var b=Key(end);
        if(Path!="rotation")return Vector4.Lerp(a,b,blend);
        var rotation=System.Numerics.Quaternion.Slerp(new(a.X,a.Y,a.Z,a.W),new(b.X,b.Y,b.Z,b.W),blend);rotation=System.Numerics.Quaternion.Normalize(rotation);return new(rotation.X,rotation.Y,rotation.Z,rotation.W);
    }
    private Vector4 Key(int index){int size=Path=="rotation"?4:3,at=index*size;return new(Values[at],Values[at+1],Values[at+2],size==4?Values[at+3]:0);}
}
internal sealed record PetMaterial(Material Surface,ImageTexture? Normal,double NormalScale);
internal sealed class ImageTexture(BitmapSource bitmap,byte[] pixels)
{
    public BitmapSource Bitmap {get;}=bitmap;public byte[] Pixels {get;}=pixels;
    public float MeanGreen=>Pixels.Length==0?1:Enumerable.Range(0,Pixels.Length/4).Average(i=>Pixels[i*4+1]/255f);
    public Vector3 Sample(Vector2 uv)
    {
        int x=(int)MathF.Round(uv.X*(Bitmap.PixelWidth-1)),y=(int)MathF.Round((1-uv.Y)*(Bitmap.PixelHeight-1));x=(x%Bitmap.PixelWidth+Bitmap.PixelWidth)%Bitmap.PixelWidth;y=(y%Bitmap.PixelHeight+Bitmap.PixelHeight)%Bitmap.PixelHeight;int at=(y*Bitmap.PixelWidth+x)*4;return new(Pixels[at+2]/127.5f-1,Pixels[at+1]/127.5f-1,Pixels[at]/127.5f-1);
    }
}
