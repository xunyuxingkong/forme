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
    private static readonly Model3DGroup Body=SproutMesh.CreateBody();
    private static readonly Model3DGroup Foot=CreateFoot();
    private static Model3DGroup CreateFoot(){var shape=new Model3DGroup();MeshArt.Ellipse(shape,"#BDD2AB",0,0,0,.23,.13,.25);shape.Freeze();return shape;}
    private readonly Model3DGroup _feet=new();
    private readonly TranslateTransform3D _leftFoot=new(-.35,.19,.23),_rightFoot=new(.35,.19,.23);
    internal int ExpressionCount=>_expressions.Count;
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
        foreach(var transformFoot in new[]{_leftFoot,_rightFoot}){var foot=new Model3DGroup{Transform=transformFoot};foot.Children.Add(Foot);_feet.Children.Add(foot);}
        Root.Children.Add(Body);Root.Children.Add(_feet);Apply(PetPose.Neutral());
    }
    public void Apply(PetPose pose)
    {
        _scale.ScaleX=pose.ScaleX;_scale.ScaleY=pose.ScaleY;
        _lift.OffsetY=pose.Lift*1.7;_yaw.Angle=pose.Yaw;_lean.Angle=pose.Lean;
        double stride=Math.Clamp(pose.Stride,-1,1);
        _leftFoot.OffsetZ=.23+stride*.15;_rightFoot.OffsetZ=.23-stride*.15;
        _leftFoot.OffsetY=.19+Math.Max(0,stride)*.07;_rightFoot.OffsetY=.19+Math.Max(0,-stride)*.07;
        // Bounded semantic vocabulary. Meshes are reused; no geometry creation per frame.
        string state=pose.Expression is "happy" or "focus" or "rest" or "quiet" or "thinking"?pose.Expression:"idle";
        // Idle, rest and quiet share the same expression geometry.
        string shape=state is "rest" or "quiet"?"idle":state;
        bool closed=pose.Blink||state is "focus" or "rest" or "quiet";
        string key=shape+"/"+closed;
        if(!_expressions.TryGetValue(key,out var mesh))
        {
            mesh=SproutMesh.CreateExpression(shape,closed);mesh.Freeze();_expressions[key]=mesh;
        }
        if(ReferenceEquals(_current,mesh))return;
        if(_current is not null)Root.Children.Remove(_current);Root.Children.Add(mesh);_current=mesh;
    }
    public bool Contains(Model3D model)=>Body.Children.Contains(model)||Foot.Children.Contains(model)||_current?.Children.Contains(model)==true;
    public void Dispose(){Root.Children.Clear();_expressions.Clear();_current=null;}
}
