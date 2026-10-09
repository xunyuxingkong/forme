using Forme.Core;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Forme.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};Ui.InstallStyles(app);
        string data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Forme");
        bool testing=args.Contains("--smoke")||args.Contains("--probe");
        if(testing)data=Path.GetFullPath(Path.Combine("artifacts","ui-test-data",Guid.NewGuid().ToString("N")));
        int dataArg=Array.IndexOf(args,"--data-dir");if(dataArg>=0&&dataArg+1<args.Length)data=Path.GetFullPath(args[dataArg+1]);
        string name="Forme-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data)))[..16];
        using var mutex=new Mutex(false,"Local\\"+name);bool locked=false;
        try{locked=mutex.WaitOne(0);}catch(AbandonedMutexException){locked=true;}
        if(!locked)
        {
            try{using var pipe=new NamedPipeClientStream(".",name,PipeDirection.Out);pipe.Connect(1500);using var writer=new StreamWriter(pipe);writer.WriteLine("open");}
            catch{MessageBox.Show("Forme 已在运行，请从系统托盘打开。","Forme");}
            return 0;
        }
        Controller? controller=null;DesktopHost? host=null;
        try
        {
            controller=new Controller(data);host=new DesktopHost(app,controller,name,testing);
            app.DispatcherUnhandledException+=(_,e)=>{Ui.Error("操作未完成："+e.Exception.Message);e.Handled=true;};
            if(args.Contains("--smoke"))app.Dispatcher.BeginInvoke(async()=>await Smoke.Run(host,controller));
            if(args.Contains("--probe"))app.Dispatcher.BeginInvoke(async()=>await Probe.Run(host,controller));
            app.Run();return Environment.ExitCode;
        }
        catch(Exception ex){if(args.Contains("--smoke")){Directory.CreateDirectory("artifacts");File.WriteAllText("artifacts/smoke-error.txt",ex.ToString());}else Ui.Error("启动失败，原数据保留："+ex.Message);return 1;}
        finally{host?.Dispose();controller?.Dispose();if(locked)mutex.ReleaseMutex();}
    }
}

internal sealed class DesktopHost : IDisposable
{
    private readonly Application _app;
    private readonly Controller _c;
    private readonly System.Windows.Forms.NotifyIcon _tray;
    private readonly System.Drawing.Icon? _icon;
    private PetWindow? _pet;
    public PetWindow? Pet => _pet;
    public MainWindow? House {get;private set;}
    private readonly CancellationTokenSource _pipeCancel=new();
    private readonly System.Windows.Threading.DispatcherTimer _greetings=new(){Interval=TimeSpan.FromMinutes(1)};
    private bool _exit;
    private bool _petWanted=true;
    private bool _locked;
    private string? _pendingNotice;
    private readonly bool _smoke;
    public DesktopHost(Application app,Controller c,string pipeName,bool smoke)
    {
        _app=app;_c=c;_smoke=smoke;
        _petWanted=c.Preferences.DisplayMode!="tray";
        using(var iconStream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Forme.Icon"))if(iconStream is not null)_icon=new System.Drawing.Icon(iconStream);
        _tray=new(){Icon=_icon??System.Drawing.SystemIcons.Application,Text="Forme · 陪伴小屋",Visible=!smoke};
        var menu=new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("打开小屋",null,(_,_)=>ShowHouse("home"));menu.Items.Add("显示伙伴",null,(_,_)=>ShowPet());menu.Items.Add("仅保留托盘",null,(_,_)=>HidePet());menu.Items.Add("安静模式开关",null,(_,_)=>Ui.Guard(()=>c.SavePreferences(c.Preferences with{Quiet=!c.Preferences.Quiet})));menu.Items.Add("退出",null,async(_,_)=>await Exit());_tray.ContextMenuStrip=menu;
        _tray.DoubleClick+=(_,_)=>ShowHouse("home");c.Finished+=Finished;c.Changed+=Changed;c.Tick+=TrayTick;c.Notice+=Notice;
        SystemEvents.PowerModeChanged+=Power;SystemEvents.SessionSwitch+=Session;SystemEvents.DisplaySettingsChanged+=Displays;
        _greetings.Tick+=(_,_)=>Greeting();
        if(!smoke){_ =Listen(pipeName);if(!c.Preferences.Onboarded)ShowHouse("welcome");else if(_petWanted)ShowPet(false);Changed();}
    }
    public void ShowHouse(string page)
    {
        _pet?.Close();_pet=null;
        if(House is null)
        {
            House=new(_c,Changed);House.Closed+=(_,_)=>{House=null;if(!_exit&&_petWanted)ShowPet(false);};
        }
        if(_smoke)House.ShowActivated=false;
        House.Show();if(House.WindowState==WindowState.Minimized)House.WindowState=WindowState.Normal;
        if(!_smoke)House.Activate();House.Navigate(page);
        if(_pendingNotice is {} notice){House.Toast(notice);_pendingNotice=null;}Changed();
    }
    public void ShowPet(bool expand=true)
    {
        _petWanted=true;if(expand&&_c.Preferences.DisplayMode!="pet")_c.SavePreferences(_c.Preferences with{DisplayMode="pet"});if(House is not null)return;
        _pet??=new(_c,ShowHouse,HidePet,()=>_ =Exit());_pet.Show();Changed();
    }
    public void HidePet(){_petWanted=false;if(_c.Preferences.DisplayMode!="tray")_c.SavePreferences(_c.Preferences with{DisplayMode="tray"});_pet?.Close();_pet=null;if(House is not null)House.Close();Changed();_app.Dispatcher.BeginInvoke(()=>GC.Collect(2,GCCollectionMode.Forced,false),System.Windows.Threading.DispatcherPriority.ContextIdle);}
    public async Task Exit()
    {
        if(_exit)return;_exit=true;_c.Cancel();
        for(int i=0;i<30&&_c.Busy;i++)await Task.Delay(100);
        _app.Shutdown();
    }
    private void Changed()
    {
        if(!_smoke && !_locked && (_pet is not null||House is not null) && _c.Preferences.Greetings&&!_c.Preferences.Quiet)_greetings.Start();else _greetings.Stop();
        TrayTick();
    }
    private void TrayTick()
    {
        var time=TimeSpan.FromSeconds(Math.Ceiling(_c.Clock.Remaining));
        _tray.Text="Forme · "+(_c.Clock.Active?(_c.Clock.Activity!.Kind=="focus"?"专注 ":"休息 ")+time.ToString(_c.Clock.Remaining>=3600?@"hh\:mm\:ss":@"mm\:ss")+(_c.Clock.Running?"":" · 暂停"):"陪伴小屋");
    }
    private void Notice(string message)
    {
        if(House is not null)return;_pendingNotice=message;
        if(!_smoke&&!_c.Preferences.Quiet&&_c.Preferences.Notifications){_tray.BalloonTipTitle="Forme";_tray.BalloonTipText="操作未完成，打开小屋查看状态。";_tray.ShowBalloonTip(5000);}
    }
    private void Finished(string text)
    {
        House?.Toast(text);
        if(!_c.Preferences.Quiet&&_c.Preferences.TimerSounds)System.Media.SystemSounds.Asterisk.Play();
        if(!_smoke&&!_c.Preferences.Quiet&&_c.Preferences.Notifications){_tray.BalloonTipTitle="Forme";_tray.BalloonTipText=text;_tray.ShowBalloonTip(5000);}
    }
    private void Greeting()
    {
        var p=_c.Preferences;var now=DateTimeOffset.Now;string day=now.ToString("yyyy-MM-dd");
        if(p.Quiet||_c.Clock.Active||now.Hour<p.GreetingStart||now.Hour>=p.GreetingEnd||_pet is null&&!(_c.Preferences.Onboarded&&House is not null))return;
        int count=p.GreetingDay==day?p.GreetingCount:0;if(count>=2||p.LastGreeting is {} last&&now-last<TimeSpan.FromHours(2))return;
        var text="忙的时候也记得松松肩膀，按自己的节奏来。";House?.Toast(text);
        _pet?.Greet(text);
        Ui.Guard(()=>_c.SavePreferences(p with{GreetingDay=day,GreetingCount=count+1,LastGreeting=now}));
    }
    private void Power(object? sender,PowerModeChangedEventArgs e){if(e.Mode==PowerModes.Suspend)_app.Dispatcher.Invoke(()=>Ui.Guard(()=>{if(_c.Clock.Running){_c.Pause();House?.Toast("系统即将休眠，计时已暂停。");}}));}
    private void Session(object? sender,SessionSwitchEventArgs e)
    {
        if(e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionUnlock)
            _app.Dispatcher.Invoke(()=>Ui.Guard(()=>{_locked=e.Reason==SessionSwitchReason.SessionLock;if(_locked&&_c.Clock.Running)_c.Pause();Changed();}));
    }
    private void Displays(object? sender,EventArgs e)=>_app.Dispatcher.BeginInvoke(()=>_pet?.Clamp());
    private async Task Listen(string name)
    {
        while(!_pipeCancel.IsCancellationRequested)
        {
            try
            {
                using var pipe=new NamedPipeServerStream(name,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_pipeCancel.Token);using var reader=new StreamReader(pipe);
                if(await reader.ReadLineAsync(_pipeCancel.Token)=="open")_ =_app.Dispatcher.BeginInvoke(()=>ShowHouse("home"));
            }
            catch(OperationCanceledException){break;}catch(IOException){await Task.Delay(200,_pipeCancel.Token);}
        }
    }
    public void Dispose()
    {
        _exit=true;_pipeCancel.Cancel();_greetings.Stop();SystemEvents.PowerModeChanged-=Power;SystemEvents.SessionSwitch-=Session;SystemEvents.DisplaySettingsChanged-=Displays;
        _c.Finished-=Finished;_c.Changed-=Changed;_c.Tick-=TrayTick;_c.Notice-=Notice;_tray.Dispose();_icon?.Dispose();_pet?.Close();House?.Close();_pipeCancel.Dispose();
    }
}

internal static class Smoke
{
    public static async Task Run(DesktopHost host,Controller c)
    {
        try
        {
            Directory.CreateDirectory("artifacts/screenshots");
            IconAsset.Ensure();
            c.SavePreferences(new Preferences{Onboarded=true});
            host.ShowHouse("home");await Task.Delay(300);
            host.House!.UpdateLayout();var roomView=Descendants(host.House).OfType<Room3DView>().Single();var hitPages=new HashSet<string>();
            int moodTargets=0,exactMoodTargets=0;var exactHit=typeof(Room3DView).GetMethod("ExactHit",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
            for(int x=5;x<roomView.ActualWidth;x+=10)for(int y=5;y<roomView.ActualHeight;y+=10)
                if(roomView.ObjectAt(new Point(x,y)) is {} page){hitPages.Add(page);if(page=="mood"){moodTargets++;if(exactHit.Invoke(roomView,[new Point(x,y)]) as string=="mood")exactMoodTargets++;}}
            if(!new[]{"chat","focus","relax","plant","room","mood"}.All(hitPages.Contains))throw new Exception("3D object picking incomplete: "+string.Join(",",hitPages));
            if(moodTargets<=exactMoodTargets)throw new Exception("Notebook click target was not enlarged");
            foreach(string page in new[]{"home","chat","focus","mood","relax","plant","room","settings"})
            {
                host.House!.Navigate(page);await Task.Delay(80);Save(host.House,$"artifacts/screenshots/{page}.png");
            }
            host.House!.Navigate("home");
            var roomModel=(System.Windows.Media.Media3D.ModelVisual3D)roomView.Children.OfType<System.Windows.Controls.Viewport3D>().Single().Children[0];
            var unchangedRoom=roomModel.Content;roomView.Refresh();if(!ReferenceEquals(unchangedRoom,roomModel.Content))throw new Exception("Unchanged room rebuilt its geometry");
            roomView.Preview=c.Preferences with{Theme="night",Rug="sage"};await Task.Delay(80);Save(host.House,"artifacts/screenshots/home-night.png");
            if(ReferenceEquals(unchangedRoom,roomModel.Content)||c.Preferences.Theme!="auto")throw new Exception("Room appearance preview failed");
            roomView.Preview=c.Preferences with{Theme="day",Rug="rose"};await Task.Delay(80);Save(host.House,"artifacts/screenshots/home-rose.png");
            roomView.Preview=null;
            // Exercise real page controls, not just page construction.
            host.House!.Navigate("mood");Click(host.House,"保存心情");if(c.Store.Moods().Count==0)throw new Exception("Mood UI did not save");
            host.House.Navigate("room");host.House.UpdateLayout();var selectors=Descendants(host.House).OfType<System.Windows.Controls.ComboBox>().ToArray();selectors[0].SelectedIndex=2;
            if(c.Preferences.Theme!="auto")throw new Exception("Room preview mutated saved preferences");host.House.Navigate("home");if(c.Preferences.Theme!="auto")throw new Exception("Cancelled preview persisted");
            host.House.Navigate("relax");Click(host.House,"戳泡泡");host.House.UpdateLayout();var bubbleButtons=Descendants(host.House).OfType<System.Windows.Controls.Button>().Where(x=>x.Content?.ToString()=="○").ToArray();if(bubbleButtons.Length!=20)throw new Exception("Expected 20 bubble controls");foreach(var b in bubbleButtons)b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if(!c.Store.Unlocked("cloud"))throw new Exception("Bubble UI did not unlock reward");Save(host.House,"artifacts/screenshots/bubbles.png");
            host.House!.Width=780;host.House.Navigate("home");await Task.Delay(80);Save(host.House,"artifacts/screenshots/narrow.png");Click(host.House,"查看3D小屋");Save(host.House,"artifacts/screenshots/narrow-room.png");Click(host.House,"返回活动");
            Click(host.House,"查看3D小屋");c.SavePreferences(c.Preferences with{Quiet=true});host.House.UpdateLayout();
            if(!roomView.IsVisible||!Descendants(host.House).OfType<System.Windows.Controls.Button>().Any(x=>x.Content?.ToString()=="返回活动"))throw new Exception("Setting change closed compact room view");
            Click(host.House,"返回活动");host.House.Navigate("focus");host.House.UpdateLayout();
            var focusInputs=Descendants(host.House).OfType<System.Windows.Controls.TextBox>().ToArray();focusInputs[0].Text="保留的任务草稿";focusInputs[1].Text="37";
            c.SavePreferences(c.Preferences with{Quiet=false});host.House.UpdateLayout();
            focusInputs=Descendants(host.House).OfType<System.Windows.Controls.TextBox>().ToArray();
            if(focusInputs[0].Text!="保留的任务草稿"||focusInputs[1].Text!="37")throw new Exception("Focus draft lost during settings refresh");
            host.House.Navigate("home");host.House.Navigate("focus");host.House.UpdateLayout();
            if(Descendants(host.House).OfType<System.Windows.Controls.TextBox>().First().Text!="保留的任务草稿")throw new Exception("Focus draft lost during navigation");
            foreach(var layout in new[]{(1280d,850d,"fit-normal"),(960d,850d,"fit-boundary"),(1280d,520d,"fit-short"),(650d,480d,"fit-minimum")})
            {
                host.House.Width=layout.Item1;host.House.Height=layout.Item2;host.House.Navigate("home");await Task.Delay(80);
                if(layout.Item1<940)Click(host.House,"查看3D小屋");host.House.UpdateLayout();
                if(!roomView.DefaultSceneFits())throw new Exception("Default camera clips scene in "+layout.Item3);
                Save(host.House,"artifacts/screenshots/"+layout.Item3+".png");
            }
            host.House.Width=1280;host.House.Height=850;host.House.Navigate("home");c.SavePreferences(c.Preferences with{Quiet=false});await Task.Delay(80);Save(host.House,"artifacts/screenshots/home.png");
            c.Start("focus",1,"测试任务");c.Pause();if(c.Clock.Running)throw new Exception("Pause failed");c.Resume();c.Finish();
            if(c.FocusDraft!="")throw new Exception("Started focus did not consume its task draft");
            if(c.Store.Focus().Count==0)throw new Exception("Focus record missing");
            c.Store.Water(DateOnly.FromDateTime(DateTime.Now));if(c.Store.Water(DateOnly.FromDateTime(DateTime.Now)))throw new Exception("Duplicate watering");
            var secrets=new Secrets(Path.Combine("artifacts","secret-test"));Directory.CreateDirectory(Path.Combine("artifacts","secret-test"));secrets.Save("fake-secret-for-local-test");if(secrets.Read()!="fake-secret-for-local-test")throw new Exception("DPAPI roundtrip");secrets.Delete();
            var memory=Process.GetCurrentProcess().PrivateMemorySize64/1024/1024;
            host.HidePet();host.ShowPet();await Task.Delay(150);Save(host.Pet!,"artifacts/screenshots/pet.png");
            var petSource=(HwndSource)PresentationSource.FromVisual(host.Pet)!;
            var style=GetWindowLong(petSource.Handle,-20);if((style&0x08000000)==0)throw new Exception("Pet activates keyboard focus");
            var screenPoint=host.Pet!.PointToScreen(new Point(3,3));long packed=((long)(short)screenPoint.Y<<16)|((ushort)(short)screenPoint.X);
            if(SendMessage(petSource.Handle,0x0084,IntPtr.Zero,new IntPtr(packed))!=new IntPtr(-1))throw new Exception("Transparent area captures input");
            host.Pet.CollapseToEdge();await Task.Delay(100);if(c.Preferences.DisplayMode!="edge"||host.Pet.Width>50)throw new Exception("Edge collapse not persisted");host.Pet.ExpandFromEdge();await Task.Delay(100);if(c.Preferences.DisplayMode!="pet"||host.Pet.Width<100)throw new Exception("Edge restore failed");
            host.HidePet();
            File.WriteAllText("artifacts/smoke-result.txt",$"PASS: 8 pages rendered, 3D geometry picking for all 6 activities, four camera-fit layouts, compact scene retained on settings change, focus drafts retained on refresh and navigation, responsive layout, day/night and rug previews, unchanged room geometry reuse, mood UI save, room preview rollback, bubble UI reward, focus persistence, watering deduplication, DPAPI roundtrip, native pet HWND no-activate and transparent-area hit test.\nHouse private memory snapshot: {memory} MB\nTimestamp: {DateTimeOffset.Now:O}\n");
            Environment.ExitCode=0;
            if(File.Exists("artifacts/smoke-error.txt"))File.Delete("artifacts/smoke-error.txt");
        }
        catch(Exception ex){Directory.CreateDirectory("artifacts");File.WriteAllText("artifacts/smoke-error.txt",ex.ToString());Environment.ExitCode=1;}
        await host.Exit();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window,int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window,int msg,IntPtr w,IntPtr l);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var item in Descendants(child))yield return item;}}
    private static void Click(Window window,string text)
    {window.UpdateLayout();var button=Descendants(window).OfType<System.Windows.Controls.Button>().First(x=>x.Content?.ToString()==text);button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));}
    private static void Save(Window window,string path)
    {
        var visual=(FrameworkElement)window.Content;visual.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth),(int)Math.Ceiling(visual.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
    }
}
