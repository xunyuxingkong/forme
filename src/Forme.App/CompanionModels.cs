using Forme.Core;
using System.Windows.Media.Media3D;
using static Forme.App.MeshArt;

namespace Forme.App;

internal static class PetModels
{
    public static readonly (string Id,string Name)[] Catalog=[("sprout","团团 · 芽芽伙伴"),("cat","奶糖 · 小猫"),("fox","栗子 · 小狐狸"),("penguin","雪球 · 小企鹅")];
    public static IPetModelFactory For(string id)=>id=="sprout"?new SproutPetFactory():new CreatureFactory(id);
    private sealed class CreatureFactory(string id):IPetModelFactory {public IPetModel Create()=>new CreaturePetModel(id);}
}

internal sealed class CreaturePetModel : IPetModel
{
    private static readonly Dictionary<string,Model3DGroup> Bodies=new();
    private static readonly Dictionary<string,Model3DGroup> Feet=new();
    private static readonly Dictionary<string,Model3DGroup> Expressions=new();
    public Model3DGroup Root {get;}=new();
    private readonly string _id;
    private readonly Model3DGroup _body,_foot;
    private readonly ScaleTransform3D _scale=new();
    private readonly TranslateTransform3D _lift=new();
    private readonly AxisAngleRotation3D _yaw=new(new(0,1,0),0),_lean=new(new(0,0,1),0);
    private readonly TranslateTransform3D _left=new(-.29,.14,.19),_right=new(.29,.14,.19);
    private readonly TranslateTransform3D _leftPaw=new(-.52,.61,.06),_rightPaw=new(.52,.61,.06);
    private Model3DGroup? _expression;
    private static readonly Model3DGroup CatEar=CatDetails.Ear(),CatTail=CatDetails.Tail();
    private readonly AxisAngleRotation3D _earLeft=new(new(0,0,1),-12),_earRight=new(new(0,0,1),12),_tailSwing=new(new(0,1,0),0);
    public CreaturePetModel(string id)
    {
        if(id is not ("cat" or "fox" or "penguin"))throw new ArgumentException("Unknown companion model",nameof(id));_id=id;
        if(!Bodies.TryGetValue(id,out var body)){body=Body(id);body.Freeze();Bodies[id]=body;}_body=body;
        if(!Feet.TryGetValue(id,out var foot)){foot=new();Ellipse(foot,id=="penguin"?"#E5B060":id=="fox"?"#9B6654":"#D2B699",0,0,0,.20,.12,.24);foot.Freeze();Feet[id]=foot;}_foot=foot;
        var transform=new Transform3DGroup();transform.Children.Add(_scale);transform.Children.Add(new RotateTransform3D(_yaw,new Point3D(0,.7,0)));transform.Children.Add(new RotateTransform3D(_lean,new Point3D(0,.7,0)));transform.Children.Add(_lift);Root.Transform=transform;
        Root.Children.Add(body);foreach(var leg in new[]{_left,_right}){var group=new Model3DGroup{Transform=leg};group.Children.Add(foot);Root.Children.Add(group);}Apply(PetPose.Neutral());
        if(id=="cat"){foreach(var (angle,x) in new[]{(_earLeft,-.33),(_earRight,.33)}){var ear=new Model3DGroup();ear.Children.Add(CatEar);var pose=new Transform3DGroup();pose.Children.Add(new RotateTransform3D(angle));pose.Children.Add(new TranslateTransform3D(x,1.28,0));ear.Transform=pose;Root.Children.Add(ear);}var tail=new Model3DGroup();tail.Children.Add(CatTail);var tailPose=new Transform3DGroup();tailPose.Children.Add(new RotateTransform3D(_tailSwing));tailPose.Children.Add(new TranslateTransform3D(.45,.32,-.40));tail.Transform=tailPose;Root.Children.Add(tail);}
        if(id=="cat")foreach(var paw in new[]{_leftPaw,_rightPaw}){var group=new Model3DGroup{Transform=paw};group.Children.Add(foot);Root.Children.Add(group);}
    }
    private static Model3DGroup Body(string id)
    {
        var g=new Model3DGroup();
        if(id=="penguin")
        {
            Ellipse(g,"#657F93",0,.76,0,.54,.70,.44);Ellipse(g,"#F4EBDD",0,.63,.27,.40,.47,.19);
            Ellipse(g,"#F4EBDD",-.22,1.08,.33,.19,.20,.10);Ellipse(g,"#F4EBDD",.22,1.08,.33,.19,.20,.10);
            Ellipse(g,"#657F93",-.55,.70,0,.12,.36,.16,-28);Ellipse(g,"#657F93",.55,.70,0,.12,.36,.16,28);
            Ellipse(g,"#E5B060",0,.96,.49,.15,.075,.13);Box(g,"#BDAFCB",0,.44,.38,.62,.08,.08);
        }
        else
        {
            string fur=id=="fox"?"#D9A071":"#E0C5A3",light=id=="fox"?"#F7E7D0":"#F6EDDB";
            Ellipse(g,fur,0,.70,0,.56,.57,.46);Ellipse(g,light,0,.54,.27,.36,.31,.18);Ellipse(g,fur,0,1.01,.04,.53,.39,.44);
            if(id!="cat")foreach(double x in new[]{-.34,.34})
            {Box(g,fur,x,1.38,0,.26,.39,.21,x<0?-22:22);Box(g,"#D6A5A0",x,1.41,.117,.12,.22,.018,x<0?-22:22);if(id!="cat")Ellipse(g,fur,x<0?-.54:.54,.59,.06,.12,.23,.13,x<0?-20:20);}
            if(id=="fox")
            {Ellipse(g,fur,.44,.55,-.43,.22,.39,.35,-35);Ellipse(g,light,.58,.80,-.60,.18,.18,.20);Ellipse(g,light,0,.94,.38,.31,.17,.16);Ellipse(g,"#644E43",0,1.02,.55,.047,.037,.034);}
            else
            {Ellipse(g,"#BD987B",0,1.32,.34,.08,.055,.018);Ellipse(g,"#DDAEA0",0,1.0,.484,.037,.029,.020);}
            Ellipse(g,"#E9B0A1",-.34,.91,.37,.075,.038,.026);Ellipse(g,"#E9B0A1",.34,.91,.37,.075,.038,.026);
        }
        return g;
    }
    public void Apply(PetPose pose)
    {
        _scale.ScaleX=pose.ScaleX;_scale.ScaleY=pose.ScaleY;_lift.OffsetY=pose.Lift*1.6;_yaw.Angle=pose.Yaw;_lean.Angle=pose.Lean;
        _left.OffsetZ=.19+pose.Stride*.13;_right.OffsetZ=.19-pose.Stride*.13;_left.OffsetY=.14+Math.Max(0,pose.Stride)*.06;_right.OffsetY=.14+Math.Max(0,-pose.Stride)*.06;
        if(_id=="cat"){double curious=pose.Expression=="thinking"?15:pose.Expression is "rest" or "quiet"?-10:0;_earLeft.Angle=-12-curious+pose.Lean;_earRight.Angle=12+curious+pose.Lean;_tailSwing.Angle=pose.Stride*18+pose.Lean*2;}
        double reach=Math.Clamp(pose.Reach,0,1);_leftPaw.OffsetY=_rightPaw.OffsetY=.61+reach*.14;_leftPaw.OffsetZ=_rightPaw.OffsetZ=.06+reach*.32;
        bool closed=pose.Blink||pose.Expression is "sleep" or "rest" or "quiet" or "focus";string expression=pose.Expression is "happy" or "thinking" or "focus"?pose.Expression:"idle";
        string key=$"{_id}/{closed}/{expression}";
        if(!Expressions.TryGetValue(key,out var face))
        {
            face=new();double y=_id=="penguin"?1.12:1.10,z=_id=="penguin"?.433:.434;
            foreach(double x in new[]{-.20,.20})
            {if(closed)Box(face,"#3D4546",x,y,z,.105,.020,.02,x<0?-7:7);else{Ellipse(face,"#3D4546",x,y,z,.034,.048,.023);Ellipse(face,"#FFFFFF",x+.009,y+.015,z+.020,.009,.011,.006);}}
            if(_id!="penguin"){Box(face,"#66534A",-.025,.90,.466,.055,.016,.018,-20);Box(face,"#66534A",.025,.90,.466,.055,.016,.018,20);}
            if(expression=="happy"){Ellipse(face,"#E7C676",.68,1.25,0,.06,.13,.045);Ellipse(face,"#E7C676",.68,1.25,0,.13,.045,.045);}
            if(expression=="thinking")Ellipse(face,"#BAAACB",0,1.55,0,.05,.05,.04);
            if(expression=="focus"){Box(face,"#E5D4AE",0,.35,.51,.53,.045,.23);Box(face,"#FFF0D0",0,.38,.51,.018,.012,.21);}
            face.Freeze();Expressions[key]=face;
        }
        if(ReferenceEquals(face,_expression))return;if(_expression is not null)Root.Children.Remove(_expression);Root.Children.Add(face);_expression=face;
    }
    public bool Contains(Model3D model)=>CatEar.Children.Contains(model)||CatTail.Children.Contains(model)||_body.Children.Contains(model)||_foot.Children.Contains(model)||_expression?.Children.Contains(model)==true;
    public void Dispose(){Root.Children.Clear();_expression=null;}
}
