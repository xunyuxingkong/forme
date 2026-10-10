using Forme.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;

namespace Forme.App;

// Runs only under --smoke, with an unrelated, earless test model to exercise the adapter.
internal static class PetAnimationChecks
{
    private sealed class CubeFactory : IPetModelFactory
    {
        public CubeModel? Last;
        public IPetModel Create()=>Last=new CubeModel();
    }
    private static bool HasChatHit(Room3DView room)
    {
        for(int x=5;x<room.ActualWidth;x+=8)for(int y=5;y<room.ActualHeight;y+=8)
            if(room.ObjectAt(new Point(x,y))=="chat")return true;
        return false;
    }
    public static void CheckRoomReplacement(Room3DView room)
    {
        var factory=new CubeFactory();room.ReplaceModel(factory);room.UpdateLayout();
        if(!HasChatHit(room))throw new Exception("Replacement model lost room chat picking");
        var cube=factory.Last!;room.ReplaceModel(new SproutPetFactory());room.UpdateLayout();
        if(cube.Disposals!=1||!HasChatHit(room))throw new Exception("Restored room model lost picking or retained old rig");
    }
    private sealed class CubeModel : IPetModel
    {
        public Model3DGroup Root { get; }=new();
        private readonly ScaleTransform3D _scale=new();
        private readonly TranslateTransform3D _lift=new();
        private readonly GeometryModel3D _shape;
        public int Samples,Disposals;
        public PetPose Pose;
        public CubeModel()
        {
            _shape=MeshArt.Box(Root,"#8BBCC4",0,.6,0,.7,1.2,.5);
            var transform=new Transform3DGroup();transform.Children.Add(_scale);transform.Children.Add(_lift);Root.Transform=transform;
        }
        public void Apply(PetPose pose){Samples++;Pose=pose;_scale.ScaleX=pose.ScaleX;_scale.ScaleY=pose.ScaleY;_lift.OffsetY=pose.Lift*1.2;}
        public bool Contains(Model3D model)=>ReferenceEquals(model,_shape);
        public void Dispose(){Disposals++;Root.Children.Clear();}
    }
    public static async Task Run()
    {
        var factory=new CubeFactory();var pet=new Pet3DView(factory);var cube=factory.Last!;
        var window=new Window{Width=230,Height=240,ShowActivated=false,ShowInTaskbar=false,Content=pet};
        try
        {
            window.Show();await Task.Delay(180);
            if(!pet.AnimationRunning||cube.Samples<2||cube.Pose.ScaleY<=1)throw new Exception("Adapter idle animation did not advance");
            pet.Pat();await Task.Delay(200);
            if(cube.Pose.Expression!="happy"||cube.Pose.Lift<=0)throw new Exception("Adapter pat missing");
            pet.Drag(true);await Task.Delay(100);
            if(cube.Pose.Expression!="thinking")throw new Exception("Adapter drag missing");
            pet.Drag(false);await Task.Delay(120);
            if(cube.Pose.ScaleY>=1)throw new Exception("Adapter landing missing");
            pet.Play(PetAction.DanceSpin);await Task.Delay(300);
            if(cube.Pose.Expression!="happy"||cube.Pose.Yaw<=0)throw new Exception("Adapter dance missing");
            pet.StopAction();
            if(cube.Pose.Expression!="idle")throw new Exception("Stopped dance did not restore idle");
            pet.MotionSettings(false,true,false);await Task.Delay(500);
            if(pet.AnimationRunning)throw new Exception("Quiet idle kept ticking");
            pet.Pat();await Task.Delay(150);if(!pet.AnimationRunning||cube.Pose.Expression!="happy")throw new Exception("Quiet explicit interaction missing");
            await Task.Delay(1200);if(pet.AnimationRunning)throw new Exception("Quiet action failed to stop");
            pet.MotionSettings(true,false,false);pet.Pat();int count=cube.Samples;await Task.Delay(150);
            if(pet.AnimationRunning||cube.Samples!=count||cube.Pose!=PetPose.Neutral("happy"))throw new Exception("Reduced motion did not stay static");
            pet.MotionSettings(false,false,true);count=cube.Samples;await Task.Delay(150);
            if(pet.AnimationRunning||cube.Samples!=count)throw new Exception("Suspended animator kept ticking");
            pet.MotionSettings(false,false,false);window.Hide();count=cube.Samples;await Task.Delay(150);
            if(pet.AnimationRunning||cube.Samples!=count)throw new Exception("Hidden animator kept ticking");
            window.Show();await Task.Delay(100);
            var replacement=new CubeFactory();pet.ReplaceModel(replacement);
            if(cube.Disposals!=1||cube.Root.Children.Count!=0)throw new Exception("Replaced model was retained");
            cube=replacement.Last!;await Task.Delay(100);pet.Rub(false);await Task.Delay(120);
            if(!pet.AnimationRunning||cube.Pose.ScaleY>=1||!cube.Pose.Blink)throw new Exception("Replacement did not reuse rub action");
            pet.Release();count=cube.Samples;await Task.Delay(150);
            if(pet.AnimationRunning||cube.Samples!=count||cube.Disposals!=1)throw new Exception("Released animator kept ticking");
            using var sprout=new SproutPetModel();sprout.Apply(PetPose.Neutral());var geometry=sprout.Root.Children[0];
            sprout.Apply(new PetMotion().Sample(1));
            if(!ReferenceEquals(geometry,sprout.Root.Children[0])||!geometry.IsFrozen)throw new Exception("Motion rebuilt model geometry or left static geometry mutable");
            foreach(var state in new[]{"idle","focus","thinking","happy"})foreach(bool blink in new[]{false,true})
            {
                sprout.Apply(PetPose.Neutral(state) with{Blink=blink});
                foreach(var mesh in sprout.Root.Children.OfType<Model3DGroup>())
                    if(!Shapes(mesh).All(sprout.Contains))throw new Exception("Expression geometry lost picking ownership");
            }
            using var other=new SproutPetModel();
            if(ReferenceEquals(sprout.Root,other.Root)||!ReferenceEquals(sprout.Root.Children[0],other.Root.Children[0]))throw new Exception("Mutable roots must be independent and frozen bodies shared");
            int rootParts=sprout.Root.Children.Count;var states=new[]{"idle","focus","thinking","happy","rest","quiet"};
            for(int i=0;i<10000;i++)sprout.Apply(PetPose.Neutral(states[i%6]) with{Blink=(i/6)%2==0});
            if(sprout.ExpressionCount>7||sprout.Root.Children.Count!=rootParts||!ReferenceEquals(geometry,sprout.Root.Children[0]))throw new Exception("10000 expression swaps grew geometry cache or duplicated body");
        }
        finally{pet.Release();window.Close();}
    }
    private static IEnumerable<Model3D> Shapes(Model3DGroup group)=>group.Children.SelectMany(child=>child is Model3DGroup nested?Shapes(nested):[child]);
}
