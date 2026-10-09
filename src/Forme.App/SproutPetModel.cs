using Forme.Core;
using System.Windows.Media.Media3D;

namespace Forme.App;

internal sealed class SproutPetFactory : IPetModelFactory
{
    public IPetModel Create()=>new SproutPetModel();
}

internal sealed class SproutPetModel : IPetModel
{
    public Model3DGroup Root { get; }=new();
    private readonly ScaleTransform3D _scale=new();
    private readonly TranslateTransform3D _lift=new();
    private readonly AxisAngleRotation3D _yaw=new(new(0,1,0),0),_lean=new(new(0,0,1),0);
    private readonly Dictionary<string,Model3DGroup> _expressions=new();
    private Model3DGroup? _current;
    public SproutPetModel()
    {
        var transform=new Transform3DGroup();
        transform.Children.Add(_scale);
        transform.Children.Add(new RotateTransform3D(_yaw,new Point3D(0,.75,0)));
        transform.Children.Add(new RotateTransform3D(_lean,new Point3D(0,.75,0)));
        transform.Children.Add(_lift);Root.Transform=transform;
        Apply(PetPose.Neutral());
    }
    public void Apply(PetPose pose)
    {
        _scale.ScaleX=pose.ScaleX;_scale.ScaleY=pose.ScaleY;
        _lift.OffsetY=pose.Lift*1.7;_yaw.Angle=pose.Yaw;_lean.Angle=pose.Lean;
        // Bounded semantic vocabulary. Meshes are reused; no geometry creation per frame.
        string state=pose.Expression is "happy" or "focus" or "rest" or "quiet" or "thinking"?pose.Expression:"idle";
        string key=state+"/"+pose.Blink;
        if(!_expressions.TryGetValue(key,out var mesh))
        {
            mesh=SproutMesh.Create(state,pose.Blink);mesh.Freeze();_expressions[key]=mesh;
        }
        if(ReferenceEquals(_current,mesh))return;
        Root.Children.Clear();Root.Children.Add(mesh);_current=mesh;
    }
    public bool Contains(Model3D model)=>_current?.Children.Contains(model)==true;
    public void Dispose(){Root.Children.Clear();_expressions.Clear();_current=null;}
}
