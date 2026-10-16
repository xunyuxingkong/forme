#if FORME_DEVELOPMENT_TOOLS
using Forme.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Forme.App;

internal static class FoundationChecks
{
    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        yield return node;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
            foreach(var child in Descendants(VisualTreeHelper.GetChild(node,i)))yield return child;
    }
    private static System.Drawing.Point Position(PetWindow pet)
    {
        if(!ScreenCoordinateService.TryGetWindowRect(new WindowInteropHelper(pet).Handle,out var rect))throw new Exception("Pet HWND unavailable");
        return rect.Location;
    }
    private static async Task Until(Func<bool> condition,string failure,int milliseconds=5000)
    {
        var until=DateTimeOffset.UtcNow.AddMilliseconds(milliseconds);
        while(!condition()){if(DateTimeOffset.UtcNow>=until)throw new Exception(failure);await Task.Delay(50);}
    }
    public static async Task Run(DesktopHost host,Controller c)
    {
        c.SavePreferences(new Preferences{Onboarded=true,AllowPetControl=true,AllowSceneControl=true,PetIdleMode="idle",ShowHome3D=true,Quiet=false,ReducedMotion=false,DisplayMode="pet"});
        host.ShowPet();host.ShowFloatingChat();await Task.Delay(200);
        var pet=host.Pet!;
        var choices=Descendants(pet).OfType<ComboBox>().First();
        var start=Position(pet);choices.SelectedIndex=2;
        await Until(()=>Position(pet)!=start,"Selector saved walk but HWND did not move: "+pet.WanderStatus);
        if(c.Store.LoadPreferences().PetIdleMode!="walk")throw new Exception("Selector did not persist mode");
        c.SavePreferences(c.Preferences with{PetIdleMode="run"});start=Position(pet);
        await Until(()=>Position(pet)!=start,"Settings run did not resume with chat open: "+pet.WanderStatus);
        pet.SetIdleMode("idle");
        var interrupted=new CompanionCommand("stroll","walk",View:CompanionView.Desktop);
        host.ExecuteAction(interrupted);c.SavePreferences(c.Preferences with{PetModel="cat"});
        if(await c.CommandWaiter!(interrupted,CancellationToken.None))throw new Exception("Model replaced before waiter was falsely completed");
        c.SavePreferences(c.Preferences with{PetModel="sprout"});
        host.ShowHouse("home");host.House!.Width=1200;host.House.Height=850;host.House.UpdateLayout();await Task.Delay(300);
        var house=host.House;var room=Descendants(house).OfType<Room3DView>().Single();
        var roomStart=room.PetPosition;house.WindowState=WindowState.Minimized;
        var desktop=ActionPlanner.Build(AiIntentProtocol.Parse("[{\"type\":\"Stroll\",\"mode\":\"walk\",\"view\":\"desktop\"}]"),SceneKind.Indoor);
        start=Position(pet);
        var desktopResults=await ActionExecutor.Execute(desktop,()=>c.Preferences,host.PreflightActions,host.ExecuteAction,c.CommandWaiter,()=>c.StopCommands?.Invoke(),CancellationToken.None);
        if(desktopResults.Last().Code!=ActionResultCode.Completed||Position(pet)==start||room.PetPosition!=roomStart)
            throw new Exception("Explicit desktop did not move independently of minimized room");
        house.WindowState=WindowState.Normal;house.UpdateLayout();await Task.Delay(250);
        var roomPlan=ActionPlanner.Build(AiIntentProtocol.Parse("[{\"type\":\"Stroll\",\"mode\":\"walk\",\"view\":\"room\"}]"),SceneKind.Desktop);
        roomStart=room.PetPosition;start=Position(pet);
        var roomResults=await ActionExecutor.Execute(roomPlan,()=>c.Preferences,host.PreflightActions,host.ExecuteAction,c.CommandWaiter,()=>c.StopCommands?.Invoke(),CancellationToken.None);
        if(roomResults.Last().Code!=ActionResultCode.Completed||room.PetPosition==roomStart||Position(pet)!=start)
            throw new Exception("Room action did not stay on its selected body: "+roomResults.Last().UserMessage);
        house.Close();await Task.Delay(100);
        var now=DateTimeOffset.Now;
        c.Store.SaveUserReminder(new("foundation-first","宿主提醒一",now.AddMilliseconds(100),null,"pending","",now,now,null));c.Refresh();
        await Until(()=>host.ActiveReminderCount==1,"Reminder did not fire with house closed");
        c.Store.SaveUserReminder(new("foundation-next","宿主提醒二",DateTimeOffset.Now.AddMilliseconds(100),null,"pending","",now,now,null));c.Refresh();
        await Until(()=>host.ActiveReminderCount==2,"Visible reminder blocked next reminder");
        host.ShowHouse("home");host.House!.Close();
        if(host.ActiveReminderCount!=2)throw new Exception("Closing house closed host reminders");
        foreach(var window in Application.Current.Windows.OfType<Window>().Where(x=>x.Title=="Forme 提醒").ToArray())
            Descendants(window).OfType<Button>().Single(x=>Equals(x.Content,"关闭")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(host.ActiveReminderCount!=0||c.Store.DueReminders(DateTimeOffset.Now.AddMinutes(1)).Count!=0)throw new Exception("Reminder dismissal did not persist");
        File.WriteAllText("artifacts/foundation-smoke-result.txt","PASS: actual native HWND moves after idle selector and settings changes with chat open; explicit desktop movement with minimized house; room movement without moving desktop; host reminders after house closure; visible reminder does not block next; house reopening/closing preserves reminders; dismissed reminders persist. Isolated test data; no model requests.\n");
    }
}
#endif
