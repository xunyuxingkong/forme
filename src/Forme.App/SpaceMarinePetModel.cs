using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Forme.Core;

namespace Forme.App;

internal sealed class SpaceMarinePetFactory : IPetModelFactory
{
    public IPetModel Create()=>new SpaceMarinePetModel();
}

internal sealed class SpaceMarinePetModel : IPetModel
{
    // Geometry and materials are shared; each displayed companion owns eight joint transforms.
    private static readonly Lazy<MarineAsset> Asset=new(Load,LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly Dictionary<int,MatrixTransform3D> _joints=[];
    private readonly ScaleTransform3D _scale=new();
    private readonly TranslateTransform3D _lift=new();
    private readonly AxisAngleRotation3D _yaw=new(new(0,1,0),0),_lean=new(new(0,0,1),0);
    public Model3DGroup Root {get;}=new();
    internal string ActiveClip {get;private set;}="";
    internal Matrix3D[] JointMatrices=>_joints.Values.Select(transform=>transform.Matrix).ToArray();
    internal IReadOnlyDictionary<string,GlbPetClip> Clips=>Asset.Value.Clips;

    public SpaceMarinePetModel()
    {
        var asset=Asset.Value;var body=new Model3DGroup{Transform=asset.Normalization};var groups=new Dictionary<int,Model3DGroup>();
        foreach(var joint in asset.Joints)
        {
            var transform=new MatrixTransform3D(ToWpf(joint.Pose(null,0)));_joints.Add(joint.Id,transform);groups.Add(joint.Id,new(){Transform=transform});
        }
        foreach(var joint in asset.Joints)(joint.Parent<0?body:groups[joint.Parent]).Children.Add(groups[joint.Id]);
        foreach(var (joint,geometry) in asset.Geometry)(joint<0?body:groups[joint]).Children.Add(geometry);
        var pose=new Transform3DGroup();pose.Children.Add(_scale);pose.Children.Add(new RotateTransform3D(_yaw,new Point3D(0,1.55,0)));pose.Children.Add(new RotateTransform3D(_lean,new Point3D(0,1.55,0)));pose.Children.Add(_lift);Root.Transform=pose;
        Root.Children.Add(body);Root.Children.Add(asset.Lights);Apply(PetPose.Neutral());
    }
    public void Apply(PetPose pose)
    {
        var asset=Asset.Value;GlbPetClip? clip=null;ActiveClip="";
        if(pose.Locomotion is "walk" or "run"&&asset.Clips.TryGetValue(pose.Locomotion,out clip))ActiveClip=pose.Locomotion;
        foreach(var joint in asset.Joints)_joints[joint.Id].Matrix=ToWpf(joint.Pose(clip,pose.LocomotionSeconds));
        bool moving=clip is not null;
        _scale.ScaleX=_scale.ScaleZ=moving?1:pose.ScaleX;_scale.ScaleY=moving?1:pose.ScaleY;
        _lift.OffsetY=moving?0:pose.Lift*3.2;_yaw.Angle=!moving&&pose.Expression is "idle" or "quiet"?0:pose.Yaw;_lean.Angle=moving?0:pose.Lean;
    }
    public bool Contains(Model3D model)=>Asset.Value.Shapes.Contains(model);
    public void Dispose(){Root.Children.Clear();_joints.Clear();}
    private static Matrix3D ToWpf(Matrix4x4 m)=>new(m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44);

    private static MarineAsset Load()
    {
        // The complete source contains the rifle and original rigid-joint Walk/Run clips.
        // Keep the display pedestal out of the companion and bake only fixed node transforms.
        var source=GlbPetAsset.Read("Forme.SpaceMarine.glb","Display_Base");
        if(!source.Clips.ContainsKey("Walk")||!source.Clips.ContainsKey("Run"))throw new InvalidDataException("Space Marine requires the original Walk and Run clips.");
        Vector3 min=new(float.MaxValue),max=new(float.MinValue);
        foreach(var ((joint,_),batch) in source.Batches)
        {
            Matrix4x4 world=joint<0?Matrix4x4.Identity:source.Joints[joint].RestWorld;
            foreach(var position in batch.Positions){var p=Vector3.Transform(position,world);min=Vector3.Min(min,p);max=Vector3.Max(max,p);}
        }
        if(!float.IsFinite(min.Y)||max.Y-min.Y<.001f)throw new InvalidDataException("Space Marine mesh bounds are invalid.");
        const float height=3.1f;float fit=height/(max.Y-min.Y);
        var normalization=new MatrixTransform3D(ToWpf(Matrix4x4.CreateTranslation(-(min.X+max.X)*.5f,-min.Y,-(min.Z+max.Z)*.5f)*Matrix4x4.CreateScale(fit)));normalization.Freeze();
        var geometry=new Dictionary<int,Model3DGroup>();var shapes=new HashSet<Model3D>();
        foreach(var ((joint,materialIndex),batch) in source.Batches)
        {
            PetMaterial? materialInfo=materialIndex>=0&&materialIndex<source.Materials.Length?source.Materials[materialIndex]:null;Material material=materialInfo?.Surface??GlbPetAsset.Fallback;
            IReadOnlyList<Vector3> normals=materialInfo?.Normal is {} normal?GlbPetAsset.ApplyNormalMap(batch,normal,materialInfo.NormalScale):batch.Normals;
            var mesh=new MeshGeometry3D();foreach(var p in batch.Positions)mesh.Positions.Add(new Point3D(p.X,p.Y,p.Z));
            foreach(var n in normals)mesh.Normals.Add(new Vector3D(n.X,n.Y,n.Z));foreach(var uv in batch.Uvs)mesh.TextureCoordinates.Add(new System.Windows.Point(uv.X,1-uv.Y));foreach(int index in batch.Indices)mesh.TriangleIndices.Add(index);mesh.Freeze();
            var shape=new GeometryModel3D(mesh,material){BackMaterial=material};shape.Freeze();shapes.Add(shape);
            if(!geometry.TryGetValue(joint,out var group))geometry[joint]=group=new();group.Children.Add(shape);
        }
        foreach(var group in geometry.Values)group.Freeze();
        var lights=new Model3DGroup();lights.Children.Add(new AmbientLight(Color.FromRgb(176,176,176)));lights.Children.Add(new DirectionalLight(Colors.White,new Vector3D(-.45,-.8,-1)));lights.Children.Add(new DirectionalLight(Color.FromRgb(185,195,210),new Vector3D(.6,-.2,.7)));lights.Freeze();foreach(var light in lights.Children)shapes.Add(light);
        return new(source.Joints.Values.ToArray(),source.Clips,geometry,shapes,normalization,lights);
    }
    private sealed record MarineAsset(GlbPetJoint[] Joints,Dictionary<string,GlbPetClip> Clips,Dictionary<int,Model3DGroup> Geometry,HashSet<Model3D> Shapes,MatrixTransform3D Normalization,Model3DGroup Lights);
}
