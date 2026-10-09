using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace Forme.App;

// Desktop APIs and WinForms screen bounds use physical pixels; WPF sizes use DIPs.
internal static class ScreenCoordinateService
{
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window,out NativeRect rectangle);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,int flags);
    internal static bool TryGetWindowRect(IntPtr handle,out Rectangle rect)
    {
        if(GetWindowRect(handle,out var value)){rect=Rectangle.FromLTRB(value.Left,value.Top,value.Right,value.Bottom);return true;}
        rect=Rectangle.Empty;return false;
    }
    internal static bool SetPosition(IntPtr handle,int x,int y,int flags=0x15)=>SetWindowPos(handle,IntPtr.Zero,x,y,0,0,flags);
    internal static bool SetBounds(IntPtr handle,int x,int y,int width,int height,int flags=0x14)=>SetWindowPos(handle,IntPtr.Zero,x,y,width,height,flags);
    internal static Rectangle WorkAreaAt(Rectangle rect)
    {
        var screen=System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s=>s.WorkingArea.Contains(rect.Left+rect.Width/2,rect.Top+rect.Height/2))??System.Windows.Forms.Screen.PrimaryScreen!;
        return screen.WorkingArea;
    }
    internal static Rectangle Clamp(Rectangle rect,Rectangle workArea)
    {
        rect.X=Math.Clamp(rect.X,workArea.Left,Math.Max(workArea.Left,workArea.Right-rect.Width));
        rect.Y=Math.Clamp(rect.Y,workArea.Top,Math.Max(workArea.Top,workArea.Bottom-rect.Height));return rect;
    }
    internal static double DpiScale(Visual visual)=>VisualTreeHelper.GetDpi(visual).DpiScaleX;
    internal static int DipToPhysical(double dip,Visual visual)=>DipToPhysical(dip,DpiScale(visual));
    internal static int DipToPhysical(double dip,double scale)=>(int)Math.Round(dip*scale);
    internal static double PhysicalToDip(int pixels,Visual visual)=>PhysicalToDip(pixels,DpiScale(visual));
    internal static double PhysicalToDip(int pixels,double scale)=>pixels/scale;
}
