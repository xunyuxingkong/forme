using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Forme.Core;

namespace Forme.App;

internal sealed class ImportedPetModelFactory(string id):IPetModelFactory
{
    public IPetModel Create()=>new ImportedPetModel(id);
}

internal sealed class ImportedPetModel : IPetModel
{
    private static readonly IReadOnlyDictionary<string,string> Resources=new Dictionary<string,string>(StringComparer.Ordinal)
    {
        ["vesna"]="Forme.Pet.Ranger.glb",
        ["vodyanitsa"]="Forme.Pet.Cleric.glb",
        ["odette"]="Forme.Pet.Warrior.glb",
        ["umw-adventurer"]="Forme.Pet.Women.Adventurer.glb",
        ["umw-casual"]="Forme.Pet.Women.Casual.glb",
        ["umw-formal"]="Forme.Pet.Women.Formal.glb",
        ["anime-rose"]="Forme.Pet.Anime.Rose.vrm",
        ["anime-olivia"]="Forme.Pet.Anime.Olivia.vrm",
        ["anime-kate"]="Forme.Pet.Anime.Kate.vrm",
        [LocalPetModels.HuTaoId]="local:genshin-hu-tao"
    };
    private static readonly Dictionary<string,ImportedAsset> Assets=new(StringComparer.Ordinal);
    private readonly ImportedAsset _asset;
    private readonly Dictionary<int,MatrixTransform3D> _jointTransforms=[];
    private readonly Dictionary<int,Model3DGroup> _jointGroups=[];
    private readonly List<SkinnedMesh> _skinnedMeshes=[];
    private readonly Stopwatch _clock=Stopwatch.StartNew();
    private readonly ScaleTransform3D _scale=new();
    private readonly TranslateTransform3D _lift=new();
    private readonly AxisAngleRotation3D _yaw=new(new(0,1,0),0),_lean=new(new(0,0,1),0);
    private readonly HashSet<Model3D> _shapes=[];
    public Model3DGroup Root {get;}=new();
    internal int MeshCount=>_asset.JointGeometry.Values.Sum(items=>items.Length)+_asset.RootGeometry.Length+_asset.Skinned.Length;
    internal string ActiveClip {get;private set;}="";
    internal Matrix3D FacingTransform=>Root.Transform.Value;
    internal Matrix3D[] JointMatrices=>_jointTransforms.OrderBy(pair=>pair.Key).Select(pair=>pair.Value.Matrix).ToArray();

    public ImportedPetModel(string id)
    {
        if(!Resources.TryGetValue(id,out var resource))throw new ArgumentException("Unknown imported companion model",nameof(id));
        lock(Assets){if(!Assets.TryGetValue(id,out var asset))Assets.Add(id,asset=Load(resource));_asset=asset;}
        var transform=new Transform3DGroup();transform.Children.Add(_scale);transform.Children.Add(new RotateTransform3D(_yaw,new Point3D(0,.9,0)));transform.Children.Add(new RotateTransform3D(_lean,new Point3D(0,.9,0)));transform.Children.Add(_lift);Root.Transform=transform;
        var body=new Model3DGroup{Transform=new MatrixTransform3D(ToWpf(_asset.Normalization))};Root.Children.Add(body);Root.Children.Add(_asset.Lights);
        foreach(var joint in _asset.Joints.Values)
        {
            var matrix=new MatrixTransform3D(ToWpf(joint.Pose(null,0)));_jointTransforms.Add(joint.Id,matrix);_jointGroups.Add(joint.Id,new(){Transform=matrix});
        }
        foreach(var joint in _asset.Joints.Values)(joint.Parent<0?body:_jointGroups[joint.Parent]).Children.Add(_jointGroups[joint.Id]);
        foreach(var (joint,geometries) in _asset.JointGeometry)
        foreach(var geometry in geometries)(joint<0?body:_jointGroups[joint]).Children.Add(geometry);
        foreach(var geometry in _asset.RootGeometry)body.Children.Add(geometry);
        foreach(var skinned in _asset.Skinned)
        {
            // This VRM follows glTF's top-left texture origin, as does WPF.
            // Retain existing adapters' UV convention while importing this file.
            var mesh=new MeshGeometry3D{TextureCoordinates=new PointCollection(skinned.Uvs.Select(uv=>new System.Windows.Point(uv.X,id==LocalPetModels.HuTaoId?uv.Y:1-uv.Y))),TriangleIndices=new Int32Collection(skinned.Indices)};
            var material=MaterialFor(_asset,skinned.Material);var shape=new GeometryModel3D(mesh,material){BackMaterial=material};
            var node=new Model3DGroup{Transform=new MatrixTransform3D(ToWpf(skinned.MeshTransform))};node.Children.Add(shape);body.Children.Add(node);
            _skinnedMeshes.Add(new(skinned,mesh));_shapes.Add(shape);
        }
        foreach(var geometry in _asset.Shapes)_shapes.Add(geometry);
        Apply(PetPose.Neutral());
    }

    public void Apply(PetPose pose)
    {
        string requested=pose.Locomotion is "walk" or "run"?pose.Locomotion:
            pose.Reach>.2?(_asset.Clips.ContainsKey("PickUp")?"PickUp":_asset.Clips.ContainsKey("Interact")?"Interact":"Wave"):
            Math.Max(Math.Abs(pose.LeftArm),Math.Abs(pose.RightArm))>34&&_asset.Clips.ContainsKey("Wave")?"Wave":"Idle";
        GlbPetClip? clip=null;
        if(!_asset.Clips.TryGetValue(requested,out clip)&&!_asset.Clips.TryGetValue("Idle",out clip)&&!_asset.ProceduralRig)throw new InvalidDataException("Imported companion is missing its idle animation.");
        ActiveClip=clip is null?"Procedural":requested;
        double seconds=pose.Locomotion is "walk" or "run"?pose.LocomotionSeconds:_clock.Elapsed.TotalSeconds;
        var worlds=new Dictionary<int,Matrix4x4>(_asset.Joints.Count);
        Matrix4x4 World(int id)
        {
            if(worlds.TryGetValue(id,out var cached))return cached;
            GlbPetJoint joint=_asset.Joints[id];Matrix4x4 local=joint.Pose(clip,seconds,JointOffset(joint.Name,pose,_asset.ProceduralRig),ProceduralOffset(joint.Name,pose,seconds));
            Matrix4x4 world=joint.Parent<0?local:local*World(joint.Parent);worlds[id]=world;return world;
        }
        foreach(var joint in _asset.Joints.Values)_jointTransforms[joint.Id].Matrix=ToWpf(joint.Pose(clip,seconds,JointOffset(joint.Name,pose,_asset.ProceduralRig),ProceduralOffset(joint.Name,pose,seconds)));
        foreach(var dynamicMesh in _skinnedMeshes)UpdateSkin(dynamicMesh,_asset.Skins[dynamicMesh.Source.Skin],worlds,World);

        bool moving=pose.Locomotion is "walk" or "run";
        _scale.ScaleX=_scale.ScaleZ=moving?1:pose.ScaleX;_scale.ScaleY=moving?1:pose.ScaleY;
        _lift.OffsetY=moving?0:pose.Lift*1.6+Math.Abs(pose.Stride)*.018;
        // These VRMs are authored facing -Z while Forme's room camera and travel
        // convention treat +Z as the character's forward direction.
        _yaw.Angle=pose.Yaw+(_asset.ProceduralRig?180:0);_lean.Angle=pose.Lean;
    }

    public bool Contains(Model3D model)=>_shapes.Contains(model);
    public void Dispose(){Root.Children.Clear();_jointTransforms.Clear();_jointGroups.Clear();_skinnedMeshes.Clear();_shapes.Clear();}

    private void UpdateSkin(SkinnedMesh target,PetSkin skin,Dictionary<int,Matrix4x4> worlds,Func<int,Matrix4x4> world)
    {
        var positions=new Point3DCollection(target.Source.Positions.Length);var normals=new Vector3DCollection(target.Source.Positions.Length);
        Matrix4x4 meshInverse=Matrix4x4.Invert(target.Source.MeshTransform,out var invMesh)?invMesh:throw new InvalidDataException("Invalid skinned mesh transform.");
        var transforms=new Matrix4x4[skin.Joints.Length];var normalTransforms=new Matrix4x4[skin.Joints.Length];
        for(int i=0;i<transforms.Length;i++)
        {
            transforms[i]=skin.InverseBindMatrices[i]*world(skin.Joints[i])*meshInverse;
            normalTransforms[i]=Matrix4x4.Transpose(Matrix4x4.Invert(transforms[i],out var inverse)?inverse:Matrix4x4.Identity);
        }
        for(int i=0;i<target.Source.Positions.Length;i++)
        {
            Vector3 source=target.Source.Positions[i],normal=i<target.Source.Normals.Length?target.Source.Normals[i]:Vector3.UnitY;Vector3 positionSum=Vector3.Zero,normalSum=Vector3.Zero;Vector4 weights=target.Source.Weights[i];float total=0;
            for(int influence=0;influence<4;influence++)
            {
                float weight=influence switch{0=>weights.X,1=>weights.Y,2=>weights.Z,_=>weights.W};if(weight<=0)continue;
                int jointIndex=target.Source.Joints[i*4+influence];if((uint)jointIndex>=(uint)transforms.Length)continue;
                positionSum+=Vector3.Transform(source,transforms[jointIndex])*weight;normalSum+=Vector3.TransformNormal(normal,normalTransforms[jointIndex])*weight;total+=weight;
            }
            if(total<=1e-6f){positionSum=source;normalSum=normal;}
            else if(Math.Abs(total-1)>1e-4f){positionSum/=total;normalSum/=total;}
            if(normalSum.LengthSquared()>1e-12f)normalSum=Vector3.Normalize(normalSum);
            positions.Add(new(positionSum.X,positionSum.Y,positionSum.Z));normals.Add(new(normalSum.X,normalSum.Y,normalSum.Z));
        }
        target.Mesh.Positions=positions;target.Mesh.Normals=normals;
    }

    private static double JointOffset(string name,PetPose pose,bool proceduralRig)
    {
        int separator=name.LastIndexOf(':');if(separator>=0)name=name[(separator+1)..];
        return name switch
        {
            "UpperArm.L"=>pose.LeftArm,"UpperArm.R"=>pose.RightArm,
            "LeftArm"=>proceduralRig?65-pose.LeftArm*2.4:pose.LeftArm,
            "RightArm"=>proceduralRig?-65-pose.RightArm*1.8:pose.RightArm,
            "LowerArm.L" or "LeftForeArm"=>pose.LeftElbow,"LowerArm.R" or "RightForeArm"=>pose.RightElbow,
            "Head"=>pose.HeadTilt,_=>0
        };
    }

    private Vector3 ProceduralOffset(string name,PetPose pose,double seconds)
    {
        if(!_asset.ProceduralRig)return Vector3.Zero;
        int separator=name.LastIndexOf(':');if(separator>=0)name=name[(separator+1)..];
        double phase=seconds*(pose.Locomotion=="run"?Math.PI*5:Math.PI*3);
        double gait=pose.Locomotion is "walk" or "run"?Math.Sin(phase):pose.Stride;
        double leg=pose.Locomotion=="run"?44:pose.Locomotion=="walk"?28:pose.Stride!=0?20:0;
        double idle=Math.Sin(seconds*1.1)*2.8;
        double armSwing=pose.Locomotion is "walk" or "run"?gait*(pose.Locomotion=="run"?24:15):0;
        return name switch
        {
            "LeftUpLeg"=>new((float)(gait*leg),0,0),
            "RightUpLeg"=>new((float)(-gait*leg),0,0),
            "LeftLeg"=>new((float)(Math.Max(0,-gait)*leg*.8),0,0),
            "RightLeg"=>new((float)(Math.Max(0,gait)*leg*.8),0,0),
            "LeftFoot"=>new((float)(-Math.Max(0,-gait)*leg*.35),0,0),
            "RightFoot"=>new((float)(-Math.Max(0,gait)*leg*.35),0,0),
            // The source rigs use Mixamo's T-pose: rotate each shoulder down
            // around Z at rest, then animate that same joint for arm gestures.
            "LeftArm"=>new(0,0,(float)(-armSwing+idle)),
            "RightArm"=>new(0,0,(float)(armSwing-idle)),
            "LeftForeArm"=>new(0,0,(float)(pose.Reach>0?-20*pose.Reach:0)),
            "RightForeArm"=>new(0,0,(float)(pose.Reach>0?-20*pose.Reach:0)),
            "Head"=>new((float)(pose.Locomotion is "walk" or "run"?-gait*2:0),0,(float)idle),
            "Spine" or "Spine1" or "Spine2"=>new((float)(pose.Locomotion is "walk" or "run"?gait*3:0),(float)(pose.Lean*.25),0),
            _=>Vector3.Zero
        };
    }

    private static ImportedAsset Load(string resource)
    {
        bool local=resource=="local:genshin-hu-tao";
        if(local)LocalPetModels.RequireAvailable(LocalPetModels.HuTaoId);
        var source=local?GlbPetAsset.ReadFile(LocalPetModels.HuTaoPath):GlbPetAsset.Read(resource);
        if(local)
        {
            // VRM supplies semantic bone IDs; author-specific node names are not
            // a reliable rig API (the source uses numbered MMD joint names).
            var names=new Dictionary<string,string>{["leftUpperArm"]="LeftArm",["rightUpperArm"]="RightArm",["leftLowerArm"]="LeftForeArm",["rightLowerArm"]="RightForeArm",["head"]="Head",["neck"]="Neck",["spine"]="Spine",["chest"]="Spine1",["upperChest"]="Spine2",["leftUpperLeg"]="LeftUpLeg",["rightUpperLeg"]="RightUpLeg",["leftLowerLeg"]="LeftLeg",["rightLowerLeg"]="RightLeg",["leftFoot"]="LeftFoot",["rightFoot"]="RightFoot"};
            if(!source.HumanoidBones.Values.Contains("hips")||!source.HumanoidBones.Values.Contains("head"))throw new InvalidDataException("VRM humanoid rig is incomplete.");
            foreach(var (node,semantic) in source.HumanoidBones)
                if(names.TryGetValue(semantic,out string? name)&&source.Joints.TryGetValue(node,out var joint))source.Joints[node]=joint with{Name=name};
        }
        bool proceduralRig=local||resource.StartsWith("Forme.Pet.Anime.",StringComparison.Ordinal);if(!proceduralRig&&(!source.Clips.ContainsKey("Idle")||!source.Clips.ContainsKey("Walk")||!source.Clips.ContainsKey("Run")))throw new InvalidDataException("Quaternius companion requires Idle, Walk and Run animation clips.");
        var jointWorlds=source.Joints.ToDictionary(pair=>pair.Key,pair=>pair.Value.RestWorld);
        Vector3 min=new(float.MaxValue),max=new(float.MinValue);
        foreach(var ((joint,_),batch) in source.Batches)
        {
            Matrix4x4 rest=joint<0?Matrix4x4.Identity:source.Joints[joint].RestWorld;
            foreach(var position in batch.Positions){var point=Vector3.Transform(position,rest);min=Vector3.Min(min,point);max=Vector3.Max(max,point);}
        }
        foreach(var batch in source.SkinnedBatches)
        {
            var skin=source.Skins[batch.Skin];Matrix4x4 meshInverse=Matrix4x4.Invert(batch.MeshTransform,out var inverse)?inverse:throw new InvalidDataException("Invalid skinned mesh transform.");
            var transforms=new Matrix4x4[skin.Joints.Length];for(int i=0;i<transforms.Length;i++)transforms[i]=skin.InverseBindMatrices[i]*jointWorlds[skin.Joints[i]]*meshInverse;
            for(int i=0;i<batch.Positions.Length;i++)
            {
                Vector3 point=Vector3.Zero;Vector4 weights=batch.Weights[i];float total=0;
                for(int influence=0;influence<4;influence++){float weight=influence switch{0=>weights.X,1=>weights.Y,2=>weights.Z,_=>weights.W};int index=batch.Joints[i*4+influence];if(weight<=0||(uint)index>=(uint)transforms.Length)continue;point+=Vector3.Transform(batch.Positions[i],transforms[index])*weight;total+=weight;}
                if(total>1e-6f)point/=total;else point=batch.Positions[i];min=Vector3.Min(min,point);max=Vector3.Max(max,point);
            }
        }
        float height=max.Y-min.Y;if(!float.IsFinite(height)||height<.001f)throw new InvalidDataException("Imported companion mesh bounds are invalid.");
        float fit=1.8f/height;var normalization=Matrix4x4.CreateTranslation(-(min.X+max.X)*.5f,-min.Y,-(min.Z+max.Z)*.5f)*Matrix4x4.CreateScale(fit);
        var jointGeometry=new Dictionary<int,List<Model3D>>();var rootGeometry=new List<Model3D>();var shapes=new HashSet<Model3D>();
        foreach(var ((joint,materialIndex),batch) in source.Batches)
        {
            var mesh=new MeshGeometry3D();for(int i=0;i<batch.Positions.Count;i++){Vector3 p=batch.Positions[i],n=i<batch.Normals.Count?batch.Normals[i]:Vector3.UnitY;mesh.Positions.Add(new(p.X,p.Y,p.Z));mesh.Normals.Add(new(n.X,n.Y,n.Z));mesh.TextureCoordinates.Add(i<batch.Uvs.Count?new System.Windows.Point(batch.Uvs[i].X,1-batch.Uvs[i].Y):new System.Windows.Point());}foreach(int index in batch.Indices)mesh.TriangleIndices.Add(index);mesh.Freeze();
            Material material=MaterialFor(source,materialIndex);var shape=new GeometryModel3D(mesh,material){BackMaterial=material};shape.Freeze();shapes.Add(shape);
            if(joint<0)rootGeometry.Add(shape);else{if(!jointGeometry.TryGetValue(joint,out var list))jointGeometry[joint]=list=[];list.Add(shape);}
        }
        var lights=new Model3DGroup();lights.Children.Add(new AmbientLight(Color.FromRgb(188,188,188)));lights.Children.Add(new DirectionalLight(Colors.White,new Vector3D(-.45,-.8,-1)));lights.Children.Add(new DirectionalLight(Color.FromRgb(200,205,215),new Vector3D(.6,-.2,.7)));lights.Freeze();foreach(var light in lights.Children)shapes.Add(light);
        return new(source.Joints,source.Skins,source.Clips,source.Materials,jointGeometry.ToDictionary(pair=>pair.Key,pair=>pair.Value.ToArray()),rootGeometry.ToArray(),source.SkinnedBatches.ToArray(),shapes,lights,normalization,proceduralRig);
    }

    private static Material MaterialFor(ImportedAsset asset,int materialIndex)=>materialIndex>=0&&materialIndex<asset.Materials.Length?asset.Materials[materialIndex].Surface:GlbPetAsset.Fallback;
    private static Material MaterialFor(GlbPetAsset asset,int materialIndex)=>materialIndex>=0&&materialIndex<asset.Materials.Length?asset.Materials[materialIndex].Surface:GlbPetAsset.Fallback;
    private static Matrix3D ToWpf(Matrix4x4 m)=>new(m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44);

    private sealed record ImportedAsset(Dictionary<int,GlbPetJoint> Joints,Dictionary<int,PetSkin> Skins,Dictionary<string,GlbPetClip> Clips,PetMaterial[] Materials,Dictionary<int,Model3D[]> JointGeometry,Model3D[] RootGeometry,PetSkinnedBatch[] Skinned,HashSet<Model3D> Shapes,Model3DGroup Lights,Matrix4x4 Normalization,bool ProceduralRig);
    private sealed record SkinnedMesh(PetSkinnedBatch Source,MeshGeometry3D Mesh);
}
