using System.Windows.Media;
using System.Windows.Media.Media3D;
namespace Forme.App;
internal static class CatDetails
{
    internal static Model3DGroup Ear()
    {
        var mesh=new MeshGeometry3D{Positions=new Point3DCollection{new(-.16,0,.09),new(.16,0,.09),new(0,.35,.02),new(-.13,0,-.08),new(.13,0,-.08)},TriangleIndices=new Int32Collection{0,1,2,4,3,2,0,2,3,2,1,4,0,3,4,0,4,1}};
        var fur=new DiffuseMaterial(Ui.Brush("#D8B897"));var group=new Model3DGroup();group.Children.Add(new GeometryModel3D(mesh,fur){BackMaterial=fur});MeshArt.Ellipse(group,"#E1AEA6",0,.11,.087,.075,.11,.012);group.Freeze();return group;
    }
    internal static Model3DGroup Tail(){var shape=new Model3DGroup();MeshArt.Ellipse(shape,"#D9BD9A",.10,.12,0,.13,.31,.13,-32);MeshArt.Ellipse(shape,"#BA9878",.23,.35,0,.12,.14,.12);shape.Freeze();return shape;}
}
