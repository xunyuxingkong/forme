using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Forme.App;

internal static class IconAsset
{
    public static void Ensure()
    {
        string destination=Path.GetFullPath("src/Forme.App/Assets/forme.ico");if(File.Exists(destination))return;
        var visual=new DrawingVisual();using(var d=visual.RenderOpen()){Art.Rect(d,"#F7F5EE",0,0,256,256,50);Art.Pet(d,6,-3,1.5,"idle");}
        var bitmap=new RenderTargetBitmap(256,256,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var memory=new MemoryStream();encoder.Save(memory);var bytes=memory.ToArray();Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var file=File.Create(destination);using var writer=new BinaryWriter(file);writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)1);writer.Write((byte)0);writer.Write((byte)0);writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(bytes.Length);writer.Write(22);writer.Write(bytes);
    }
}
