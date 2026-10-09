using Forme.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

namespace Forme.App;

// Actual local meshes and lights; no game engine, network assets, or render loop.
internal static class MeshArt
{
    private static readonly MeshGeometry3D SphereMesh=Sphere();
    private static readonly MeshGeometry3D CubeMesh=Cube();
    private static readonly MeshGeometry3D RoundedMesh=RoundedCube();
    private static readonly Dictionary<string,Material> Materials=new();
    private static Material Material(string color)
    {
        if(!Materials.TryGetValue(color,out var m))
        {
            var group=new MaterialGroup();group.Children.Add(new DiffuseMaterial(Ui.Brush(color)));group.Children.Add(new SpecularMaterial(Ui.Brush("#FFFFFF"),16));group.Children[1]=new SpecularMaterial(new SolidColorBrush(Color.FromArgb(18,255,255,255)),16);group.Freeze();Materials[color]=group;m=group;
        }
        return m;
    }
    private static MeshGeometry3D Sphere()
    {
        const int rings=16,segments=24;var mesh=new MeshGeometry3D();
        for(int r=0;r<=rings;r++)for(int s=0;s<=segments;s++)
        {
            double theta=Math.PI*r/rings,phi=2*Math.PI*s/segments;var v=new Vector3D(Math.Sin(theta)*Math.Cos(phi),Math.Cos(theta),Math.Sin(theta)*Math.Sin(phi));mesh.Positions.Add(new(v.X,v.Y,v.Z));mesh.Normals.Add(v);mesh.TextureCoordinates.Add(new((double)s/segments,(double)r/rings));
        }
        for(int r=0;r<rings;r++)for(int s=0;s<segments;s++){int a=r*(segments+1)+s,b=a+segments+1;mesh.TriangleIndices.Add(a);mesh.TriangleIndices.Add(a+1);mesh.TriangleIndices.Add(b);mesh.TriangleIndices.Add(a+1);mesh.TriangleIndices.Add(b+1);mesh.TriangleIndices.Add(b);}
        mesh.Freeze();return mesh;
    }
    private static MeshGeometry3D RoundedCube()
    {
        const int steps=6;const double radius=.12;var mesh=new MeshGeometry3D();
        for(int face=0;face<6;face++)
        {
            int start=mesh.Positions.Count;var a=CubeMesh.Positions[face*4];var u=CubeMesh.Positions[face*4+1]-a;var v=CubeMesh.Positions[face*4+3]-a;
            for(int y=0;y<=steps;y++)for(int x=0;x<=steps;x++)
            {
                var point=a+u*(x/(double)steps)+v*(y/(double)steps);
                var inner=new Point3D(Math.Clamp(point.X,-.5+radius,.5-radius),Math.Clamp(point.Y,-.5+radius,.5-radius),Math.Clamp(point.Z,-.5+radius,.5-radius));
                var normal=point-inner;normal.Normalize();mesh.Positions.Add(inner+normal*radius);mesh.Normals.Add(normal);
            }
            for(int y=0;y<steps;y++)for(int x=0;x<steps;x++)
            {int i=start+y*(steps+1)+x;foreach(int n in new[]{i,i+1,i+steps+2,i,i+steps+2,i+steps+1})mesh.TriangleIndices.Add(n);}
        }
        mesh.Freeze();return mesh;
    }
    private static MeshGeometry3D Cube()
    {
        var mesh=new MeshGeometry3D();
        var faces=new[]{
            new[]{new Point3D(-.5,-.5,.5),new Point3D(.5,-.5,.5),new Point3D(.5,.5,.5),new Point3D(-.5,.5,.5)},
            new[]{new Point3D(.5,-.5,-.5),new Point3D(-.5,-.5,-.5),new Point3D(-.5,.5,-.5),new Point3D(.5,.5,-.5)},
            new[]{new Point3D(-.5,-.5,-.5),new Point3D(-.5,-.5,.5),new Point3D(-.5,.5,.5),new Point3D(-.5,.5,-.5)},
            new[]{new Point3D(.5,-.5,.5),new Point3D(.5,-.5,-.5),new Point3D(.5,.5,-.5),new Point3D(.5,.5,.5)},
            new[]{new Point3D(-.5,.5,.5),new Point3D(.5,.5,.5),new Point3D(.5,.5,-.5),new Point3D(-.5,.5,-.5)},
            new[]{new Point3D(-.5,-.5,-.5),new Point3D(.5,-.5,-.5),new Point3D(.5,-.5,.5),new Point3D(-.5,-.5,.5)}};
        foreach(var face in faces){int start=mesh.Positions.Count;var normal=Vector3D.CrossProduct(face[1]-face[0],face[2]-face[0]);normal.Normalize();foreach(var v in face){mesh.Positions.Add(v);mesh.Normals.Add(normal);}foreach(int n in new[]{0,1,2,0,2,3})mesh.TriangleIndices.Add(start+n);}
        mesh.Freeze();return mesh;
    }
    private static GeometryModel3D Shape(Model3DGroup group,MeshGeometry3D mesh,string color,double x,double y,double z,double sx,double sy,double sz,double angle=0,Vector3D? axis=null)
    {
        var transform=new Transform3DGroup();transform.Children.Add(new ScaleTransform3D(sx,sy,sz));if(angle!=0)transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(axis??new Vector3D(0,0,1),angle)));transform.Children.Add(new TranslateTransform3D(x,y,z));
        var material=Material(color);var model=new GeometryModel3D(mesh,material){BackMaterial=material,Transform=transform};group.Children.Add(model);return model;
    }
    public static GeometryModel3D Ellipse(Model3DGroup group,string c,double x,double y,double z,double rx,double ry,double rz,double angle=0,Vector3D? axis=null)=>Shape(group,SphereMesh,c,x,y,z,rx,ry,rz,angle,axis);
    public static GeometryModel3D Box(Model3DGroup group,string c,double x,double y,double z,double w,double h,double depth,double angle=0)=>Shape(group,CubeMesh,c,x,y,z,w,h,depth,angle);
    private static GeometryModel3D SoftBox(Model3DGroup group,string c,double x,double y,double z,double w,double h,double depth,double angle=0)=>Shape(group,RoundedMesh,c,x,y,z,w,h,depth,angle);
    public static Model3DGroup Pet(string state)
    {
        var g=new Model3DGroup();bool closed=state is "focus" or "rest" or "quiet" or "blink";
        // Face points towards +Z. Different states remain the same sculpted character.
        Ellipse(g,"#C9DCBB",0,.74,0,.65,.62,.53);
        Ellipse(g,"#A7C49C",-.42,1.32,-.06,.16,.37,.13,-26);Ellipse(g,"#A7C49C",.42,1.32,-.06,.16,.37,.13,26);
        Ellipse(g,"#BDD2AB",-.35,.19,.23,.23,.13,.25);Ellipse(g,"#BDD2AB",.35,.19,.23,.23,.13,.25);
        Ellipse(g,"#B9CFAB",-.58,.56,.06,.14,.24,.15,-20);Ellipse(g,"#B9CFAB",.58,.56,.06,.14,.24,.15,20);
        Ellipse(g,"#E5B9A1",-.34,.66,.451,.11,.048,.028);Ellipse(g,"#E5B9A1",.34,.66,.451,.11,.048,.028);
        if(closed){Box(g,"#38503C",-.22,.86,.499,.12,.024,.024,-6);Box(g,"#38503C",.22,.86,.499,.12,.024,.024,6);}
        else{Ellipse(g,"#38503C",-.22,.86,.501,.038,.06,.024);Ellipse(g,"#38503C",.22,.86,.501,.038,.06,.024);Ellipse(g,"#FFFFFF",-.212,.881,.522,.011,.013,.006);Ellipse(g,"#FFFFFF",.228,.881,.522,.011,.013,.006);}
        if(state=="thinking")Ellipse(g,"#38503C",0,.65,.517,.032,.043,.02);
        else{Box(g,"#38503C",-.033,.665,.517,.08,.018,.02,-22);Box(g,"#38503C",.033,.665,.517,.08,.018,.02,22);}
        Box(g,"#6D935F",0,1.47,-.015,.034,.3,.034,-8);
        Ellipse(g,"#83A96E",-.12,1.57,-.014,.17,.065,.085,-30);Ellipse(g,"#A0BE7F",.12,1.63,-.014,.17,.065,.085,27);
        if(state=="focus"){Box(g,"#F3E5B8",0,.34,.54,.57,.055,.25);Box(g,"#BBAA81",0,.373,.54,.009,.008,.24);}
        if(state=="happy"){Ellipse(g,"#E9C271",.69,1.3,.1,.055,.14,.055,25);Ellipse(g,"#E9C271",.69,1.3,.1,.14,.045,.045,25);}
        return g;
    }
    public static void Lights(Model3DGroup group,bool night=false)
    {
        group.Children.Add(new AmbientLight(Color.FromRgb(night?(byte)140:(byte)185,night?(byte)151:(byte)185,night?(byte)171:(byte)178)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(255,243,214),new Vector3D(-3,-5,-4)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(105,125,123),new Vector3D(2,-1,3)));
    }
    public static Model3DGroup Room(Preferences p,string state,int points,Dictionary<Model3D,string> hits)
    {
        var g=new Model3DGroup();bool night=p.Theme=="night"||p.Theme=="auto"&&(DateTime.Now.Hour>=19||DateTime.Now.Hour<7);
        // Room lighting keeps the geometry readable without washing out its colors.
        g.Children.Add(new AmbientLight(night?Color.FromRgb(65,78,103):Color.FromRgb(127,132,125)));
        g.Children.Add(new DirectionalLight(night?Color.FromRgb(144,158,191):Color.FromRgb(158,151,133),new Vector3D(-3,-5,-4)));
        g.Children.Add(new DirectionalLight(Color.FromRgb(49,63,70),new Vector3D(3,-2,2)));
        if(night)g.Children.Add(new PointLight(Color.FromRgb(244,185,111),new Point3D(-1.04,1.38,-1.6)){Range=3.5,ConstantAttenuation=1,LinearAttenuation=.65});
        void Mark(Model3D model,string page)=>hits[model]=page;
        void MarkRange(int start,string page){foreach(var item in g.Children.Skip(start))Mark(item,page);}
        SoftBox(g,"#AC947B",0,-.15,0,6.1,.30,5.1);
        Box(g,"#E2CEAB",0,.006,0,5.98,.026,4.98);
        Box(g,night?"#87989E":"#CBD8CC",0,1.5,-2.5,6,3,.12);Box(g,night?"#A0A6A8":"#EEE4D4",-3,1.5,0,.12,3,5);
        Box(g,"#B7A58E",0,.06,-2.4,6,.12,.08);Box(g,"#B7A58E",-2.91,.06,0,.08,.12,5);
        for(int i=0;i<9;i++)Box(g,"#BDA888",0,.023,-2.2+i*.53,5.82,.003,.008);
        Ellipse(g,"#BBA88B",-1.55,.027,-1.5,1.02,.004,.51);Ellipse(g,"#BBA88B",1.63,.027,-1.45,1.08,.004,.57);
        // Window is a sculpted frame and inset landscape panel.
        int windowStart=g.Children.Count;
        var pane=Box(g,night?"#304A6B":"#A4CED6",-1.58,1.95,-2.409,1.5,1.28,.032);Mark(pane,"room");
        Ellipse(g,night?"#F4ECD2":"#F4CA76",-1.12,2.2,-2.377,.15,.15,.016);
        Ellipse(g,night?"#5B727B":"#9BB890",-1.85,1.55,-2.373,.55,.23,.018);Ellipse(g,night?"#435C66":"#6F967C",-1.25,1.53,-2.351,.46,.24,.016);
        Box(g,"#BDBE9D",-2.38,1.95,-2.34,.08,1.43,.16);Box(g,"#BDBE9D",-.78,1.95,-2.34,.08,1.43,.16);Box(g,"#BDBE9D",-1.58,2.64,-2.34,1.68,.08,.16);Box(g,"#BDBE9D",-1.58,1.26,-2.34,1.68,.08,.16);Box(g,"#BDBE9D",-1.58,1.95,-2.34,.045,1.3,.12);Box(g,"#BDBE9D",-1.58,1.95,-2.34,1.6,.045,.12);Box(g,"#C3C5AD",-1.58,1.22,-2.23,1.86,.08,.38);
        MarkRange(windowStart,"room");
        // A shelf, books, vase, and decoration slot.
        int shelfStart=g.Children.Count;
        Mark(Box(g,"#B89B7E",1.25,2.25,-2.25,1.8,.10,.43),"room");
        foreach(var (x,c,h) in new[]{(.66,"#C6AD8D",.35),(.84,"#96AF9B",.45),(1.02,"#B6ACC0",.30)})Box(g,c,x,2.30+h/2,-2.24,.14,h,.18);
        Ellipse(g,"#E2CCAA",1.67,2.44,-2.23,.16,.15,.14);Box(g,"#E2CCAA",1.67,2.61,-2.23,.10,.15,.09);Box(g,"#86A675",1.67,2.77,-2.23,.023,.26,.025);Ellipse(g,"#91AD7D",1.56,2.81,-2.23,.14,.05,.055,-20);Ellipse(g,"#A4BB8C",1.79,2.88,-2.23,.14,.05,.055,20);
        if(p.Ornament!="none"){Box(g,"#E2C99E",.36,1.67,-2.37,.48,.48,.12);Ellipse(g,p.Ornament=="star"?"#D9A55D":p.Ornament=="cloud"?"#D1DFDC":"#C4B1C5",.36,1.67,-2.285,.13,.13,.025);}
        MarkRange(shelfStart,"room");
        // Desk.
        int deskStart=g.Children.Count;
        Mark(SoftBox(g,"#B98F69",-1.55,.93,-1.58,1.95,.13,.88),"focus");
        foreach(double x in new[]{-2.32,-.78})foreach(double z in new[]{-1.88,-1.27})Mark(Box(g,"#BAA086",x,.43,z,.10,.86,.10),"focus");
        SoftBox(g,"#C8A47D",-1.55,.77,-1.7,1.15,.23,.52);Ellipse(g,"#8D785F",-1.55,.77,-1.425,.035,.035,.016);
        var book=Box(g,"#E8BA85",-1.83,1.025,-1.37,.63,.075,.42);var bookSpine=Box(g,"#FFF2D4",-1.83,1.065,-1.37,.025,.009,.39);
        Ellipse(g,"#9AAF93",-1.04,1.03,-1.74,.15,.026,.15);Box(g,"#7D9676",-1.04,1.23,-1.74,.035,.42,.035);Mark(Ellipse(g,"#B1C9A1",-1.04,1.48,-1.74,.23,.13,.23),"focus");
        SoftBox(g,"#CDB99D",-1.6,.54,-.82,.55,.10,.49);foreach(double x in new[]{-1.8,-1.4})Box(g,"#BEA98B",x,.26,-.82,.07,.50,.07);
        MarkRange(deskStart,"focus");Mark(book,"mood");Mark(bookSpine,"mood");
        // Sofa and its cushions.
        int sofaStart=g.Children.Count;
        Mark(SoftBox(g,"#73967F",1.63,.69,-1.66,1.78,.83,.62),"relax");Mark(SoftBox(g,"#A6BDA0",1.63,.45,-1.34,1.82,.25,.92),"relax");
        SoftBox(g,"#86A68A",.67,.54,-1.45,.20,.6,.86);SoftBox(g,"#86A68A",2.58,.54,-1.45,.20,.6,.86);
        SoftBox(g,"#F1DEB6",1.14,.84,-1.27,.53,.42,.23,-8);SoftBox(g,"#CCA68C",2.04,.84,-1.27,.53,.42,.23,9);
        foreach(double x in new[]{.87,2.39})Box(g,"#829577",x,.12,-1.34,.1,.23,.12);
        MarkRange(sofaStart,"relax");
        string rug=p.Rug=="sage"?"#ADC3A6":p.Rug=="rose"?"#CB9D92":"#D4BA8D";
        string rugCenter=p.Rug=="sage"?"#D2DFC6":p.Rug=="rose"?"#ECD0C3":"#F1E1BC";
        Ellipse(g,rug,.15,.045,.7,2.13,.019,1.25);Ellipse(g,rugCenter,.15,.066,.7,1.98,.009,1.12);
        // Plant in the foreground.
        int plantStart=g.Children.Count;
        Mark(Ellipse(g,"#CCAA83",2.12,.26,1.08,.25,.27,.25),"plant");Ellipse(g,"#D9BC96",2.12,.51,1.08,.29,.05,.29);Ellipse(g,"#8E795D",2.12,.545,1.08,.22,.009,.22);
        double stem=points>=5?.85:points>=2?.6:.37;Mark(Box(g,"#779867",2.12,.55+stem/2,1.08,.025,stem,.025),"plant");
        Ellipse(g,"#9CBA84",1.96,.65+stem*.4,1.08,.22,.067,.11,-22);Ellipse(g,"#89AC78",2.29,.70+stem*.4,1.08,.22,.067,.11,25);
        if(points>=5){for(int i=0;i<5;i++){double a=Math.PI*2*i/5;Ellipse(g,"#ECC887",2.12+Math.Cos(a)*.13,1.45+Math.Sin(a)*.13,1.08,.115,.115,.04);}Ellipse(g,"#F4E6AE",2.12,1.45,1.13,.07,.07,.03);}
        MarkRange(plantStart,"plant");
        var gardenPlant=new Model3DGroup{Transform=new TranslateTransform3D(0,0,2.65)};
        foreach(var item in g.Children.Skip(plantStart).ToArray()){g.Children.Remove(item);gardenPlant.Children.Add(item);}
        Ellipse(g,"#C0B695",.12,.079,.86,.66,.008,.42);
        var pet=Pet(state);pet.Transform=new TranslateTransform3D(.1,.078,.85);g.Children.Add(pet);foreach(var child in pet.Children)Mark(child,"chat");
        // The larger indoor space opens directly onto a local outdoor garden.
        var world=new Model3DGroup();foreach(var light in g.Children.OfType<Light>().ToArray()){g.Children.Remove(light);world.Children.Add(light);}
        g.Transform=new ScaleTransform3D(1.1,1,1.1);world.Children.Add(g);
        SoftBox(world,"#98AA7D",0,-.15,4.22,6.65,.28,3.0);
        Box(world,night?"#7F9A88":"#A8C28F",0,.003,4.22,6.58,.024,2.96);
        // Low borders and a stone path keep the view into the house open.
        foreach(double x in new[]{-3.25,3.25})
        {
            SoftBox(world,"#CDBB99",x,.19,4.22,.12,.35,2.92);
            foreach(double z in new[]{3.1,3.8,4.5,5.2})SoftBox(world,"#D6C6AB",x,.38,z,.16,.58,.16);
        }
        for(int i=0;i<4;i++)SoftBox(world,"#DED6BC",-.28+i*.14,.035,2.98+i*.67,.90,.045,.47,i%2==0?5:-6);
        var flowerBed=new Model3DGroup();
        SoftBox(flowerBed,"#B3936E",-1.96,.13,4.32,1.45,.23,1.88);SoftBox(flowerBed,"#806C50",-1.96,.26,4.32,1.26,.05,1.65);
        foreach(var (x,z,c) in new[]{(-2.25,3.85,"#E8BF86"),(-1.75,4.08,"#DDA6A0"),(-2.16,4.65,"#D6B0CA"),(-1.67,4.85,"#EDD49D")})
        {
            Box(flowerBed,"#628958",x,.48,z,.035,.42,.035);
            Ellipse(flowerBed,"#8DAD72",x-.10,.42,z,.17,.055,.08,-25);Ellipse(flowerBed,"#779C64",x+.10,.48,z,.16,.05,.08,25);
            for(int i=0;i<5;i++){double angle=2*Math.PI*i/5;Ellipse(flowerBed,c,x+Math.Cos(angle)*.095,.72+Math.Sin(angle)*.095,z,.085,.085,.045);}
            Ellipse(flowerBed,"#F6E6AD",x,.72,z+.045,.055,.055,.027);
        }
        foreach(var model in flowerBed.Children)Mark(model,"garden");world.Children.Add(flowerBed);
        SoftBox(world,"#CEBA98",2.11,.032,3.73,1.05,.035,1.01);world.Children.Add(gardenPlant);
        foreach(var (x,z) in new[]{(1.45,5.04),(2.25,5.05)})
        {
            var shrub=new Model3DGroup();Ellipse(shrub,"#729263",x,.19,z,.44,.19,.33);Ellipse(shrub,"#8FA976",x-.11,.30,z,.27,.19,.24);
            foreach(var model in shrub.Children)Mark(model,"garden");world.Children.Add(shrub);
        }
        return world;
    }
}

internal sealed class Pet3DView : Grid
{
    private readonly Viewport3D _viewport=new();
    private readonly ModelVisual3D _model=new();
    private readonly ScaleTransform3D _squash=new(1,1,1);
    private readonly AxisAngleRotation3D _rotation=new(new(0,1,0),-12);
    private string _state="idle";
    private bool _blink;
    public string State {get=>_state;set{if(_state==value)return;_state=value;Refresh();}}
    public bool Blink {get=>_blink;set{if(_blink==value)return;_blink=value;Refresh();}}
    public Pet3DView()
    {
        Width=164;Height=168;Background=null;
        _viewport.Camera=new PerspectiveCamera(new Point3D(0,1.05,4.5),new Vector3D(0,-.05,-4.5),new Vector3D(0,1,0),31);
        _viewport.Children.Add(_model);Children.Add(_viewport);Refresh();
    }
    private void Refresh()
    {
        var group=new Model3DGroup();MeshArt.Lights(group);var pet=MeshArt.Pet(_blink?"blink":_state);
        var transform=new Transform3DGroup();transform.Children.Add(_squash);transform.Children.Add(new RotateTransform3D(_rotation,new Point3D(0,.75,0)));pet.Transform=transform;group.Children.Add(pet);_model.Content=group;
    }
    public bool HasMeshAt(Point local)
    {
        bool hit=false;VisualTreeHelper.HitTest(_viewport,null,result=>{if(result is RayMeshGeometry3DHitTestResult){hit=true;return HitTestResultBehavior.Stop;}return HitTestResultBehavior.Continue;},new PointHitTestParameters(local));return hit;
    }
    public void Rub(bool reduced)
    {
        State="happy";if(reduced)return;
        var animation=new DoubleAnimation(1,.9,TimeSpan.FromMilliseconds(180)){AutoReverse=true};Timeline.SetDesiredFrameRate(animation,30);_squash.BeginAnimation(ScaleTransform3D.ScaleYProperty,animation);
        var wide=new DoubleAnimation(1,1.07,TimeSpan.FromMilliseconds(180)){AutoReverse=true};Timeline.SetDesiredFrameRate(wide,30);_squash.BeginAnimation(ScaleTransform3D.ScaleXProperty,wide);
    }
    public void Turn(double delta)=>_rotation.Angle=Math.Clamp(_rotation.Angle+delta,-65,65);
    public void Release(){_squash.BeginAnimation(ScaleTransform3D.ScaleXProperty,null);_squash.BeginAnimation(ScaleTransform3D.ScaleYProperty,null);_model.Content=null;_viewport.Children.Clear();}
}

internal sealed class Room3DView : Grid
{
    private readonly Controller _c;
    private readonly Action<string> _navigate;
    private readonly Viewport3D _viewport=new();
    private readonly ModelVisual3D _model=new();
    private readonly PerspectiveCamera _camera=new();
    private readonly Dictionary<Model3D,string> _hits=new();
    private readonly List<Point3D> _sceneCorners=new();
    private double _azimuth=30,_elevation=32,_zoom=1;
    private Point _origin;
    private bool _rotating;
    private DateTimeOffset _lastCamera;
    private DateTimeOffset _lastHover;
    private string? _selected;
    private string? _appearance;
    private readonly TextBlock _hint=Ui.Text("点击物件，进入活动",11,Ui.Muted);
    private readonly Canvas _labels=new(){IsHitTestVisible=false};
    private readonly Border _plantLabel=Badge("成长盆栽");
    private readonly Border _gardenLabel=Badge("装饰花坛");
    private static readonly Point3D MoodAnchor=new(-1.83*1.1,1.071,-1.37*1.1);
    private Preferences? _preview;
    private readonly System.Windows.Threading.DispatcherTimer _dayChange=new();
    public Preferences? Preview{get=>_preview;set{_preview=value;Refresh();}}
    public Room3DView(Controller c,Action<string> navigate)
    {
        _c=c;_navigate=navigate;Height=560;MinWidth=280;ClipToBounds=true;
        SizeChanged+=(_,_)=>{Clip=new RectangleGeometry(new Rect(RenderSize),18,18);Camera();};
        _viewport.Camera=_camera;_viewport.Children.Add(_model);Children.Add(_viewport);Camera();Refresh();
        _labels.Children.Add(_plantLabel);_labels.Children.Add(_gardenLabel);Children.Add(_labels);Loaded+=(_,_)=>Camera();
        var caption=Ui.Text("拖动旋转 · 滚轮缩放",11,Ui.Muted);caption.Margin=new Thickness(16);caption.VerticalAlignment=VerticalAlignment.Top;caption.IsHitTestVisible=false;Children.Add(caption);
        var hintBorder=new Border{Child=_hint,Background=Ui.Brush("#F8F6EF"),CornerRadius=new CornerRadius(8),Padding=new Thickness(10,6,10,6),Margin=new Thickness(12),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom,IsHitTestVisible=false};Children.Add(hintBorder);
        var reset=Ui.Button("复位视角",()=>{_azimuth=30;_elevation=32;_zoom=1;Camera();});reset.FontSize=11;reset.Padding=new Thickness(8,5,8,5);reset.MinHeight=26;reset.HorizontalAlignment=HorizontalAlignment.Right;reset.VerticalAlignment=VerticalAlignment.Bottom;reset.Margin=new Thickness(8);Children.Add(reset);
        _viewport.MouseLeftButtonDown+=(_,e)=>
        {
            _origin=e.GetPosition(_viewport);_rotating=false;_selected=Hit(_origin);_viewport.CaptureMouse();e.Handled=true;
        };
        _viewport.MouseMove+=(_,e)=>
        {
            var now=e.GetPosition(_viewport);
            if(!_viewport.IsMouseCaptured)
            {
                if(DateTimeOffset.UtcNow-_lastHover<TimeSpan.FromMilliseconds(60))return;
                _lastHover=DateTimeOffset.UtcNow;ShowHint(Hit(now));return;
            }
            double dx=now.X-_origin.X,dy=now.Y-_origin.Y;
            if(Math.Abs(dx)+Math.Abs(dy)>4)_rotating=true;
            if(_rotating&&DateTimeOffset.UtcNow-_lastCamera>TimeSpan.FromMilliseconds(34)){_lastCamera=DateTimeOffset.UtcNow;_azimuth=Math.Clamp(_azimuth+dx*.35,10,65);_elevation=Math.Clamp(_elevation+dy*.20,15,48);_origin=now;_viewport.Cursor=Cursors.SizeAll;_hint.Text="松开鼠标，停在喜欢的视角";Camera();}
        };
        _viewport.MouseLeftButtonUp+=(_,e)=>
        {
            var page=Hit(e.GetPosition(_viewport));bool activate=_viewport.IsMouseCaptured&&!_rotating&&page is not null&&page==_selected;
            _viewport.ReleaseMouseCapture();ShowHint(page);if(activate)Ui.Guard(()=>_navigate(PageOf(page)!));e.Handled=true;
        };
        _viewport.LostMouseCapture+=(_,_)=>_selected=null;
        _viewport.MouseLeave+=(_,_)=>{if(!_viewport.IsMouseCaptured)ShowHint(null);};
        _viewport.MouseWheel+=(_,e)=>{_zoom=Math.Clamp(_zoom-e.Delta/120*.08,.65,1.7);Camera();e.Handled=true;};
        _dayChange.Tick+=(_,_)=>{_dayChange.Stop();Refresh();};
        IsVisibleChanged+=(_,_)=>{if(IsVisible)Refresh();else _dayChange.Stop();};
        ToolTip="点击伙伴聊天、书桌专注、心情本记录、沙发放松、植物浇水；底部导航同样可用。";
    }
    private static string? PageOf(string? id)=>id=="garden"?"plant":id;
    public string? ObjectAt(Point point)=>PageOf(Hit(point));
    private void ShowHint(string? page)
    {
        _viewport.Cursor=page is null?Cursors.Arrow:Cursors.Hand;
        _hint.Text=page switch{"chat"=>"伙伴 · 点击聊一会儿","focus"=>"书桌 · 点击开始专注","mood"=>"心情本 · 点击记录心情","relax"=>"沙发 · 点击放松一下","plant"=>"成长盆栽 · 浇水会帮助它成长","garden"=>"装饰花草 · 点击查看植物照顾","room"=>"小屋布置 · 点击调整风格",_=>"点击物件，进入活动"};
    }
    private string? Hit(Point point)
    {
        var exact=ExactHit(point);
        // Enlarge the small notebook only while it is visible, without overriding other activities.
        if(exact is null or "focus" && Project(MoodAnchor) is {} center && (point-center).Length<=18 && ExactHit(center)=="mood")return "mood";
        return exact;
    }
    private string? ExactHit(Point point)
    {
        string? hit=null;VisualTreeHelper.HitTest(_viewport,null,result=>
        {
            if(result is RayMeshGeometry3DHitTestResult mesh && _hits.TryGetValue(mesh.ModelHit,out var page)){hit=page;return HitTestResultBehavior.Stop;}
            // Frontmost unmarked walls or furniture should not select objects behind them.
            return result is RayMeshGeometry3DHitTestResult?HitTestResultBehavior.Stop:HitTestResultBehavior.Continue;
        },new PointHitTestParameters(point));return hit;
    }
    private void Camera()
    {
        if(ActualWidth<=0||ActualHeight<=0)return;
        double a=_azimuth*Math.PI/180,e=_elevation*Math.PI/180;var target=new Point3D(0,.8,1.4);
        var forward=new Vector3D(-Math.Sin(a)*Math.Cos(e),-Math.Sin(e),-Math.Cos(a)*Math.Cos(e));
        var right=Vector3D.CrossProduct(forward,new Vector3D(0,1,0));right.Normalize();var up=Vector3D.CrossProduct(right,forward);
        double horizontal=Math.Tan(41*Math.PI/360),vertical=horizontal*ActualHeight/ActualWidth;
        double usableX=horizontal*Math.Max(.3,(ActualWidth-40)/ActualWidth),usableY=vertical*Math.Max(.3,(ActualHeight-86)/ActualHeight),fit=9.5;
        var bounds=_model.Content?.Bounds??new Rect3D(-3.4,-.32,-2.85,6.8,3.62,8.6);
        foreach(var corner in _sceneCorners.Count>0?_sceneCorners:Corners(bounds))
        {
            var relative=corner-target;double depth=Vector3D.DotProduct(relative,forward);
            fit=Math.Max(fit,Math.Max(Math.Abs(Vector3D.DotProduct(relative,right))/usableX,Math.Abs(Vector3D.DotProduct(relative,up))/usableY)-depth);
        }
        double distance=fit*_zoom;_camera.Position=target-forward*distance;_camera.LookDirection=forward;_camera.UpDirection=up;_camera.FieldOfView=41;_camera.NearPlaneDistance=.1;_camera.FarPlaneDistance=Math.Max(40,distance+20);
        PlaceBadge(_plantLabel,new Point3D(2.12,.4,3.73),"plant");PlaceBadge(_gardenLabel,new Point3D(-1.96,.29,4.32),"garden");
    }
    private static IEnumerable<Point3D> Corners(Rect3D bounds)
    {foreach(double x in new[]{bounds.X,bounds.X+bounds.SizeX})foreach(double y in new[]{bounds.Y,bounds.Y+bounds.SizeY})foreach(double z in new[]{bounds.Z,bounds.Z+bounds.SizeZ})yield return new(x,y,z);}
    private void CollectSceneCorners(Model3D model,Matrix3D parent)
    {
        var matrix=model.Transform.Value;matrix.Append(parent);
        if(model is Model3DGroup group)foreach(var child in group.Children)CollectSceneCorners(child,matrix);
        else if(model is GeometryModel3D shape)foreach(var corner in Corners(shape.Geometry.Bounds))_sceneCorners.Add(matrix.Transform(corner));
    }
    private Point? Project(Point3D world)
    {
        if(ActualWidth<=0||ActualHeight<=0)return null;
        var forward=_camera.LookDirection;forward.Normalize();var right=Vector3D.CrossProduct(forward,_camera.UpDirection);right.Normalize();var up=Vector3D.CrossProduct(right,forward);var relative=world-_camera.Position;
        double depth=Vector3D.DotProduct(relative,forward);if(depth<=0)return null;
        double scale=ActualWidth/(2*Math.Tan(_camera.FieldOfView*Math.PI/360));
        return new(ActualWidth/2+Vector3D.DotProduct(relative,right)/depth*scale,ActualHeight/2-Vector3D.DotProduct(relative,up)/depth*scale);
    }
    private static Border Badge(string text)=>new(){Child=Ui.Text(text,10,Ui.Ink),Background=Ui.Brush("#F8F6EF"),CornerRadius=new CornerRadius(6),Padding=new Thickness(6,3,6,0),Visibility=Visibility.Collapsed};
    private void PlaceBadge(Border badge,Point3D anchor,string category)
    {
        var point=Project(anchor);bool visible=ActualHeight>=340&&ActualWidth>=420&&point is {} p&&p.X>40&&p.X<ActualWidth-40&&p.Y>50&&p.Y<ActualHeight-45&&ExactHit(p)==category;
        badge.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
        if(visible&&point is {} position){Canvas.SetLeft(badge,position.X-29);Canvas.SetTop(badge,position.Y-38);}
    }
    internal bool DefaultSceneFits()=>_sceneCorners.Count>0&&_sceneCorners.All(corner=>Project(corner) is {} p&&p.X>=0&&p.X<=ActualWidth&&p.Y>=0&&p.Y<=ActualHeight);
    public void Refresh()
    {
        string state=_c.Clock.Active?(_c.Clock.Running&&_c.Clock.Activity!.Kind=="focus"?"focus":"rest"):_c.Busy?"thinking":_c.Preferences.Quiet?"quiet":"idle";
        var p=_preview??_c.Preferences;int points=_c.Store.PlantPoints;bool night=p.Theme=="night"||p.Theme=="auto"&&(DateTime.Now.Hour>=19||DateTime.Now.Hour<7);
        string appearance=$"{p.Theme}/{p.Rug}/{p.Ornament}/{state}/{points}/{night}";
        if(_appearance!=appearance)
        {
            _appearance=appearance;_hits.Clear();_model.Content=MeshArt.Room(p,state,points,_hits);
            _sceneCorners.Clear();CollectSceneCorners(_model.Content,Matrix3D.Identity);
            var backdrop=new LinearGradientBrush(Ui.Brush(night?"#D8E0E3":"#F1EDE1").Color,Ui.Brush(night?"#B7C6CD":"#DCE5D7").Color,new Point(0,0),new Point(1,1));backdrop.Freeze();Background=backdrop;
            Camera();
        }
        _dayChange.Stop();
        if(IsVisible&&(_preview??_c.Preferences).Theme=="auto")
        {
            var now=DateTime.Now;var next=now.Hour<7?now.Date.AddHours(7):now.Hour<19?now.Date.AddHours(19):now.Date.AddDays(1).AddHours(7);
            _dayChange.Interval=next-now;_dayChange.Start();
        }
    }
    public void Release(){_dayChange.Stop();_model.Content=null;_viewport.Children.Clear();_hits.Clear();_sceneCorners.Clear();_appearance=null;}
}
