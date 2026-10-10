using Forme.Core;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Forme.App;

internal static class BoatArt
{
    private static readonly Dictionary<string,Model3DGroup> Ships=new();
    public static string Color(string color)=>color switch{"rose"=>"#DFA8AE","mint"=>"#99BDA5",_=>"#E8C975"};
    public static Model3DGroup Ship(string shape,string color)
    {
        string key=shape+"/"+color;if(Ships.TryGetValue(key,out var cached))return cached;
        double wide=shape=="wide"?1.35:1;
        var mesh=new MeshGeometry3D();
        mesh.Positions=new Point3DCollection{new(-.22*wide,0,0),new(0,.015,.32),new(.22*wide,0,0),new(0,.015,-.28),new(0,.20,0),new(0,-.055,0)};
        mesh.TriangleIndices=new Int32Collection{0,1,4,1,2,4,2,3,4,3,0,4,0,5,1,1,5,2,2,5,3,3,5,0};mesh.Freeze();
        var material=new DiffuseMaterial(Ui.Brush(Color(color)));material.Freeze();var ship=new Model3DGroup();ship.Children.Add(new GeometryModel3D(mesh,material){BackMaterial=material});
        MeshArt.Box(ship,"#FFF2D8",0,.115,0,.012,.015,.31);ship.Freeze();Ships[key]=ship;return ship;
    }
}

internal sealed partial class Room3DView
{
    private void BuildBoatWorld()
    {
        if(!_boatOpen)return;
        var group=new Model3DGroup();_boatLeafHits.Clear();
        foreach(var (dock,z,color) in new[]{("willow",-1.3,"#B7C68E"),("bridge",0d,"#E8C975"),("lily",1.3,"#DFA8AE")})
        {
            var p=BoatPoint(new(2.8,z));MeshArt.Box(group,"#AD9070",p.X,.16,p.Z,.40,.12,.41);
            MeshArt.Ellipse(group,color,p.X,.30,p.Z,.10,.10,.08);
        }
        for(int i=0;i<3;i++)
        {
            var leaf=_boatLayout.Leaves[i];var p=BoatPoint(new(leaf.X,leaf.Z),.14);var model=new Model3DGroup();
            MeshArt.Ellipse(model,"#87AD7A",0,0,0,.34,.025,.14);MeshArt.Box(model,"#D8E3B2",.04,.029,0,.43,.009,.025);
            MeshArt.Ellipse(model,"#FAF2D5",.21,.029,0,.035,.012,.04);
            var transform=new Transform3DGroup();transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new(0,1,0),-leaf.Angle)));transform.Children.Add(new TranslateTransform3D(p.X,p.Y,p.Z));model.Transform=transform;
            foreach(var part in model.Children)_boatLeafHits[part]=i;model.Freeze();group.Children.Add(model);
        }
        var route=PaperBoat.Predict(_boatLayout);
        for(int i=0;i<route.Count;i+=2){var p=BoatPoint(route[i],.085);MeshArt.Ellipse(group,"#EAF1D4",p.X,p.Y,p.Z,.025,.008,.025);}
        _boatPetal.Children.Clear();if(_boatGame?.PetalFound!=true){var flower=BoatPoint(new(-.65,1.05),.13);MeshArt.Ellipse(_boatPetal,"#E1A9B8",flower.X,flower.Y,flower.Z,.10,.018,.08);MeshArt.Ellipse(_boatPetal,"#F4D5BD",flower.X+.035,flower.Y+.019,flower.Z,.032,.014,.032);}group.Children.Add(_boatPetal);
        for(int i=0;i<10;i++){double x=-5.4+i*.4,z=-5.9+(i%3)*.45;MeshArt.Box(group,"#BED0D1",x,1.0+(i%4)*.18,z,.008,.15,.008,-12);}
        _ship=new Model3DGroup();_ship.Children.Add(BoatArt.Ship(_boatLayout.Shape,_boatLayout.Color));
        var shipTransform=new Transform3DGroup();shipTransform.Children.Add(new RotateTransform3D(_shipHeading));shipTransform.Children.Add(_shipPosition);_ship.Transform=shipTransform;group.Children.Add(_ship);
        _boatView.Content=group;var current=_boatGame;PositionShip(current?.Position??new(-2.55,0),current?.Heading??90,0);
    }
    private void AddBoatKeepsake(Model3DGroup scene)
    {
        var album=_c.Store.Boats();var selected=album.Entries.FirstOrDefault(e=>e.Id==album.DisplayedId);if(selected is null)return;
        var stand=new Model3DGroup();MeshArt.Box(stand,"#B9A588",.2,.075,.28,.75,.06,.60);
        var ship=new Model3DGroup();ship.Children.Add(BoatArt.Ship(selected.Layout.Shape,selected.Layout.Color));ship.Transform=new TranslateTransform3D(.2,.17,.28);stand.Children.Add(ship);
        stand.Transform=new TranslateTransform3D(-.65,.72,-2.0);scene.Children.Add(stand);
        if(selected.PetalFound)MeshArt.Ellipse(stand,"#E1A9B8",.46,.12,.35,.075,.018,.06);
        foreach(var child in stand.Children){if(child is Model3DGroup nested)foreach(var part in nested.Children){if(part is Model3DGroup parts)foreach(var leaf in parts.Children)_hits[leaf]="boat";else _hits[part]="boat";}else _hits[child]="boat";}
    }
}
