using Forme.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Forme.App;
internal static class LivingChecks
{
    private static IEnumerable<DependencyObject> Nodes(DependencyObject root){for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var node in Nodes(child))yield return node;}}
    private static void Click(Window window,string text){window.UpdateLayout();Nodes(window).OfType<Button>().First(b=>Equals(b.Content,text)).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));}
    private static void Save(Window window,string name,double dpi=96){var visual=(FrameworkElement)window.Content;visual.UpdateLayout();var image=new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth*dpi/96),(int)Math.Ceiling(visual.ActualHeight*dpi/96),dpi,dpi,PixelFormats.Pbgra32);image.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create("artifacts/screenshots/"+name+".png");encoder.Save(file);}
    public static async Task Run(DesktopHost host,Controller c,Room3DView room)
    {
        var house=host.House!;var before=c.Preferences;var oldWorld=c.Store.World();c.SavePreferences(before with{ReducedMotion=false,Quiet=false,Weather="rain",Theme="day",PetModel="sprout"});
        var world=new LivingWorld([new("cushion","cushion",-.8,0,0),new("book","book",.2,0,0),new("lamp","lamp",.9,.5,0)],[]);c.Store.SaveWorld(world);house.Navigate("furniture");await Task.Delay(120);house.UpdateLayout();
        c.SavePreferences(c.Preferences with{ReducedMotion=true});room.TryMove(new(-1.5,2.5));c.SavePreferences(c.Preferences with{ReducedMotion=false});room.SelectFurniture("cushion");Save(house,"living-before-drag");var old=room.CurrentWorld.Items.First(i=>i.Id=="cushion");
        if(!room.DragFurnitureForCheck("cushion",24,12))throw new Exception("Furniture 3D picking/drag failed");if(room.CurrentWorld.Items.First(i=>i.Id=="cushion")==old)throw new Exception("Furniture drag did not move");room.UndoFurniture();if(room.CurrentWorld.Items.First(i=>i.Id=="cushion")!=old)throw new Exception("Furniture undo lost pose");
        Click(house,"旋转90°");if(room.CurrentWorld.Items.First(i=>i.Id=="cushion").Rotation!=90)throw new Exception("Furniture rotation failed");Click(house,"撤销家具");Click(house,"复制家具");if(room.CurrentWorld.Items.Count!=4)throw new Exception("Furniture copy failed");Click(house,"撤销家具");room.SelectFurniture("cushion");Click(house,"保存家具布置");Save(house,"living-editor");
        var menu=room.OpenObjectMenu("furniture:book");if(menu.Items.Count!=4)throw new Exception("Object context menu is not bounded");menu.IsOpen=false;
        house.Navigate("living");c.SavePreferences(c.Preferences with{ReducedMotion=true});Click(house,"体验听雨读书");if(!c.Store.World().Discoveries.Contains("rain-reading"))throw new Exception("Life event did not persist discovery");Save(house,"living-events");
        house.Navigate("hide");Click(house,"伙伴藏，我来找");var select=Nodes(house).OfType<ComboBox>().Single(b=>AutomationProperties.GetName(b)=="藏物家具");for(int i=0;i<select.Items.Count&&room.HideActive;i++){select.SelectedIndex=i;Click(house,"检查选中家具");}if(room.HideActive||!room.HideStatus.Contains("找到"))throw new Exception("Player seeking could not find hidden toy");
        c.SavePreferences(c.Preferences with{ReducedMotion=false});house.Navigate("hide");Click(house,"我藏，伙伴找");for(int i=0;i<400&&room.HideActive;i++)await Task.Delay(50);if(room.HideActive||!room.HideStatus.Contains("找到"))throw new Exception("Pet did not follow search route to toy");Save(house,"living-hide");
        house.Navigate("living");Click(house,"拿起当前玩具");if(!room.ThrowToy(new(1,2)))throw new Exception("Throw rejected valid floor");await Task.Delay(100);if(!room.ToyRunning)throw new Exception("Throw animation missing");room.StopWorld();if(room.ToyRunning||room.PetMoving)throw new Exception("Stop left toy or travel running");room.RecallPet();for(int i=0;i<100&&room.PetMoving;i++)await Task.Delay(50);if(room.PetPosition!=new GroundPoint(.15,1.25))throw new Exception("Recall did not arrive");
        var firstDay=new DateOnly(2026,10,1);foreach(var day in Enumerable.Range(0,5).Select(firstDay.AddDays))foreach(var task in c.Store.GameTasks(day))c.Store.CompleteGameTask(day,task.Id);
        if(!c.Store.PurchaseGameItem("feather-mint")||!c.Store.PurchaseGameItem("flower-pot"))throw new Exception("Expanded toy/decor purchase failed");
        house.Navigate("living");house.UpdateLayout();var toyChoice=Nodes(house).OfType<ComboBox>().Single(x=>AutomationProperties.GetName(x)=="选择互动玩具");toyChoice.SelectedItem="薄荷羽毛";
        if(room.SelectedToy!="feather-mint"||!room.ThrowToy(new(1,2)))throw new Exception("Owned toy selection or throw failed");
        for(int i=0;i<160&&(room.ToyRunning||room.PetMoving);i++)await Task.Delay(50);
        var petReaction=Nodes(house).OfType<TextBlock>().FirstOrDefault(x=>x.Text.Contains("薄荷羽毛",StringComparison.Ordinal));
        if(room.ToyRunning||room.PetMoving||petReaction is null||!petReaction.Text.Contains("也很喜欢",StringComparison.Ordinal))throw new Exception("Sprout toy preference did not produce its distinct reaction");
        c.SavePreferences(c.Preferences with{PetModel="cat"});room.SelectToy("feather-mint");if(!room.ThrowToy(new(1,2)))throw new Exception("Cat feather play did not start");
        for(int i=0;i<160&&(room.ToyRunning||room.PetMoving);i++)await Task.Delay(50);
        petReaction=Nodes(house).OfType<TextBlock>().FirstOrDefault(x=>x.Text.Contains("薄荷羽毛",StringComparison.Ordinal));
        if(petReaction is null||!petReaction.Text.Contains("最喜欢",StringComparison.Ordinal))throw new Exception("Cat toy preference did not produce its distinct reaction");
        c.SavePreferences(c.Preferences with{PetModel="sprout"});house.Navigate("room");house.UpdateLayout();
        var decorSlot=Nodes(house).OfType<ComboBox>().Single(x=>AutomationProperties.GetName(x)=="选择装饰位置");decorSlot.SelectedItem="窗台";Click(house,"摆放");
        if(c.Store.GameSlots().GetValueOrDefault("window")!="flower-pot")throw new Exception("New decor position did not persist");
        house.UpdateLayout();Nodes(house).OfType<ComboBox>().Single(x=>AutomationProperties.GetName(x)=="选择装饰位置").SelectedItem="窗台";Click(house,"收起此处装饰");if(c.Store.GameSlots().ContainsKey("window"))throw new Exception("Decor position did not clear");
        room.ThrowToy(new(1,2));house.WindowState=WindowState.Minimized;await Task.Delay(100);if(room.ToyRunning||room.PetMoving)throw new Exception("Hidden world continued toy or pet motion");house.WindowState=WindowState.Normal;
        foreach(double scale in new[]{1d,1.5,2d}){house.Width=scale==1?1280:760;house.Height=scale==1?850:760;house.Navigate("furniture");house.UpdateLayout();Click(house,"收起侧栏");if(!room.IsVisible)throw new Exception("Sidebar toggle hid room");Click(house,"展开侧栏");room.SelectFurniture("cushion");Click(house,"家具向前");Click(house,"撤销家具");Save(house,"living-scale-"+scale.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture),96*scale);}
        house.Width=650;house.Height=480;house.Navigate("hide");house.UpdateLayout();if(room.ActualHeight<80)throw new Exception("Small activity scene disappeared");Save(house,"living-minimum");
        house.Width=1280;house.Height=850;house.Navigate("living");room.StopWorld();c.Store.SaveWorld(oldWorld);c.SavePreferences(before);house.Navigate("home");
        File.WriteAllText("artifacts/living-smoke-result.txt","PASS: real 3D furniture drag/undo, rotate/copy/save, bounded context menu, 20 life combinations, both hide/seek modes, 6 owned toy variants, distinct sprout/cat toy reactions, 10 decor positions with placement/removal, toy throw/stop, recall, minimized cleanup, sidebar collapse, 100/150/200% raster DPI rendering and operation checks, minimum window. Simulated rendering DPI; actual OS multi-DPI monitor switching remains untested. Offline; no AI API calls.\n");
    }
}
