using Forme.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Forme.App;

internal static class BoatChecks
{
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
    private static void Click(Window window,string name){window.UpdateLayout();Descendants(window).OfType<Button>().First(b=>AutomationProperties.GetName(b)==name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));}
    private static void Save(FrameworkElement view,string name)
    {view.UpdateLayout();var bitmap=new RenderTargetBitmap((int)view.ActualWidth,(int)view.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create("artifacts/screenshots/"+name+".png");encoder.Save(file);}
    public static async Task Run(DesktopHost host,Controller c,Room3DView room)
    {
        var before=c.Preferences;var house=host.House!;
        c.SavePreferences(before with{PetModel="cat",Theme="day",ReducedMotion=false,Quiet=false});house.Navigate("boat");await Task.Delay(120);
        if(!room.BoatOpen||!room.Outdoors)throw new Exception("Boat activity did not open the pond");
        var origin=room.ProjectBoatLeaf(0)??throw new Exception("Leaf not projected");var old=room.BoatSettings;
        if(!room.DragBoatForCheck(0,new(origin.X+25,origin.Y+12)))throw new Exception("3D leaf picking failed");
        if(room.BoatSettings.Leaves[0]==old.Leaves[0])throw new Exception("Dragging did not move leaf");
        room.UndoBoat();if(room.BoatSettings.Leaves[0]!=old.Leaves[0])throw new Exception("Leaf undo failed");
        Click(house,"导流转向睡莲");if(room.BoatSettings.Leaves[0].Angle!=20)throw new Exception("Accessible leaf control failed");room.UndoBoat();
        Save(house,"boat-setup");room.StartBoat();await Task.Delay(200);
        if(!room.BoatTimerRunning)throw new Exception("Boat did not start its bounded timer");
        house.WindowState=WindowState.Minimized;await Task.Delay(100);
        if(room.BoatTimerRunning||!room.BoatPaused)throw new Exception("Minimized boat activity kept running");
        house.WindowState=WindowState.Normal;house.UpdateLayout();room.PauseBoat();
        for(int i=0;i<500&&room.LastBoat is null;i++)await Task.Delay(50);
        if(room.LastBoat?.Dock!="bridge"||room.BoatTimerRunning)throw new Exception("Real-time boat did not finish and stop");
        Save(house,"boat-finished");
        var name=Descendants(house).OfType<TextBox>().Single(x=>AutomationProperties.GetName(x)=="纪念船名字");name.Text="和奶糖一起的小船";Click(house,"收藏这次旅行");
        if(c.Store.Boats().Entries.Single().Title!=name.Text)throw new Exception("Souvenir UI failed to persist");
        room.ConfigureBoat(BoatLayout.Default() with{Wind=1});Click(house,"重玩这条路线");if(room.BoatSettings.Wind!=0)throw new Exception("Saved route was not restored");
        c.SavePreferences(c.Preferences with{ReducedMotion=true});room.ConfigureBoat(BoatLayout.Default() with{Wind=-1});room.StartBoat();
        if(room.LastBoat?.Dock!="willow"||room.BoatTimerRunning)throw new Exception("Reduced-motion boat did not resolve locally");
        house.Width=760;house.Height=760;house.UpdateLayout();Save(house,"boat-compact");
        if(room.ActualHeight<100||!Descendants(house).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="放船，开始旅行"&&b.IsVisible))throw new Exception("Compact boat scene or controls hidden");
        house.Width=1280;house.Height=850;house.Navigate("home");room.SwitchScene(false);house.UpdateLayout();Save(house,"boat-keepsake");
        if(room.BoatTimerRunning||room.BoatOpen)throw new Exception("Leaving boat retained timer or world overlay");
        c.SavePreferences(before);
        File.WriteAllText("artifacts/boat-smoke-result.txt","PASS: 3D leaf picking/drag/undo, accessible controls, real-time trip, minimized pause, bounded timer cleanup, souvenir save/replay, reduced-motion instant outcome, compact scene+controls, indoor keepsake. Offline; no AI requests.\n");
    }
}
