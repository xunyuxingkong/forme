using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Runtime.InteropServices;

namespace Forme.App;

internal sealed class PetWindow : Window
{
    private readonly Controller _c;
    private readonly Action<string> _open;
    private readonly Action _hide;
    private readonly Action _exit;
    private readonly Pet3DView _pet=new();
    private readonly DispatcherTimer _blink=new(){Interval=TimeSpan.FromSeconds(7)};
    private readonly DispatcherTimer _reset=new(){Interval=TimeSpan.FromMilliseconds(180)};
    private readonly Border _bubble;
    private readonly TextBlock _timerText=Ui.Text("",11);
    private readonly DispatcherTimer _bubbleTimeout=new(){Interval=TimeSpan.FromSeconds(8)};
    private Point _start;
    private bool _dragged;
    private bool _edge;
    private readonly Grid _normalContent;
    private readonly ContextMenu _menu;
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window,int index,int value);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window,out NativeRect rectangle);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,int flags);
    public PetWindow(Controller c,Action<string> open,Action hide,Action exit)
    {
        _c=c;_open=open;_hide=hide;_exit=exit;
        Title="Forme 桌面伙伴";Width=190;Height=260;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=System.Windows.Media.Brushes.Transparent;
        ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=false;Topmost=c.Preferences.Topmost;
        var grid=new Grid();_normalContent=grid;grid.RowDefinitions.Add(new(){Height=new GridLength(85)});grid.RowDefinitions.Add(new(){Height=new GridLength(170)});
        var buttons=Ui.Row(Ui.Button("小屋",()=>_open("home")),Ui.Button("专注",()=>_open("focus")),Ui.Button("放松",()=>_open("relax")));
        foreach(Button b in buttons.Children){b.Padding=new Thickness(7,5,7,5);b.FontSize=11;b.MinHeight=27;b.Margin=new Thickness(2);}
        _bubble=Ui.Card(Ui.Stack(_timerText,buttons),new Thickness(6));_bubble.Visibility=Visibility.Collapsed;grid.Children.Add(_bubble);
        var box=new Viewbox{Child=_pet,Stretch=System.Windows.Media.Stretch.Uniform};Grid.SetRow(box,1);grid.Children.Add(box);Content=grid;
        _pet.Cursor=Cursors.Hand;_pet.ToolTip="点击互动 · 拖动移动 · 右键菜单";
        _pet.MouseLeftButtonDown+=(_,e)=>{_start=e.GetPosition(this);_dragged=false;_pet.CaptureMouse();e.Handled=true;};
        _pet.MouseMove+=(_,e)=>
        {
            if(e.LeftButton!=MouseButtonState.Pressed || !_pet.IsMouseCaptured)return;
            var point=e.GetPosition(this);
            if(!_dragged && (Math.Abs(point.X-_start.X)>5 || Math.Abs(point.Y-_start.Y)>5))
            {
                _dragged=true;_pet.ReleaseMouseCapture();_pet.State="thinking";_pet.InvalidateVisual();
                try{DragMove();Clamp();var p=_c.Preferences with{PetX=Left,PetY=Top};_c.SavePreferences(p);}catch(InvalidOperationException){}
                Update();
            }
        };
        _pet.MouseLeftButtonUp+=(_,_)=>{_pet.ReleaseMouseCapture();if(!_dragged){_bubble.Visibility=_bubble.IsVisible?Visibility.Collapsed:Visibility.Visible;_bubbleTimeout.Stop();_bubbleTimeout.Start();_pet.State="happy";_pet.InvalidateVisual();if(c.Preferences.Sounds&&!c.Preferences.Quiet)System.Media.SystemSounds.Asterisk.Play();}};
        var menu=new ContextMenu();_menu=menu;
        void Item(string label,Action action){var i=new MenuItem{Header=label};i.Click+=(_,_)=>Ui.Guard(action);menu.Items.Add(i);}
        Item("打开小屋",()=>_open("home"));Item("聊一会儿",()=>_open("chat"));Item("开始专注",()=>_open("focus"));Item("安静模式开关",()=>_c.SavePreferences(_c.Preferences with{Quiet=!_c.Preferences.Quiet}));
        Item("置顶开关",()=>_c.SavePreferences(_c.Preferences with{Topmost=!_c.Preferences.Topmost}));Item("收起到边缘",CollapseToEdge);Item("收起到托盘",_hide);Item("退出",_exit);_pet.ContextMenu=menu;
        _blink.Tick+=(_,_)=>{if(!_c.Preferences.ReducedMotion&&!_c.Preferences.Quiet){_pet.Blink=true;_pet.InvalidateVisual();_reset.Start();}};
        _reset.Tick+=(_,_)=>{_reset.Stop();_pet.Blink=false;Update();};
        _bubbleTimeout.Tick+=(_,_)=>{_bubbleTimeout.Stop();if(!_c.Clock.Active){_bubble.Visibility=Visibility.Collapsed;_timerText.Text="";}Update();};
        IsVisibleChanged+=(_,_)=>{if(IsVisible&&!c.Preferences.ReducedMotion)_blink.Start();else{_blink.Stop();_reset.Stop();}};
        SourceInitialized+=(_,_)=>
        {
            var handle=new WindowInteropHelper(this).Handle;
            SetWindowLong(handle,-20,GetWindowLong(handle,-20)|0x08000000|0x00000080); // no-activate + toolwindow
            var source=HwndSource.FromHwnd(handle);source?.AddHook(Hook);
            Left=c.Preferences.PetX<0?SystemParameters.WorkArea.Right-Width-24:c.Preferences.PetX;
            Top=c.Preferences.PetY<0?SystemParameters.WorkArea.Bottom-Height-24:c.Preferences.PetY;Clamp();if(c.Preferences.DisplayMode=="edge")CollapseToEdge();
        };
        c.Changed+=Update;
        c.Tick+=UpdateTimer;
        Closed+=(_,_)=>{c.Changed-=Update;c.Tick-=UpdateTimer;_blink.Stop();_reset.Stop();_bubbleTimeout.Stop();_pet.Release();};
        Update();
    }
    private IntPtr Hook(IntPtr hwnd,int msg,IntPtr w,IntPtr l,ref bool handled)
    {
        if(msg==0x0084)
        {
            var raw=l.ToInt64();var point=PointFromScreen(new Point((short)(raw&0xffff),(short)((raw>>16)&0xffff)));
            if(_edge)return IntPtr.Zero;
            var petPoint=TranslatePoint(point,_pet);
            var bubblePoint=TranslatePoint(point,_bubble);
            bool interactive=_pet.HasMeshAt(petPoint) ||
                _bubble.IsVisible && new Rect(0,0,_bubble.ActualWidth,_bubble.ActualHeight).Contains(bubblePoint);
            if(!interactive){handled=true;return new IntPtr(-1);}
        }
        if(msg is 0x007E or 0x02E0)Dispatcher.BeginInvoke(Clamp);
        return IntPtr.Zero;
    }
    public void Clamp()
    {
        var handle=new WindowInteropHelper(this).Handle;if(handle==IntPtr.Zero||!GetWindowRect(handle,out var rect))return;
        var screen=System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s=>s.WorkingArea.Contains((rect.Left+rect.Right)/2,(rect.Top+rect.Bottom)/2))??System.Windows.Forms.Screen.PrimaryScreen!;
        var area=screen.WorkingArea;int x=Math.Clamp(rect.Left,area.Left,Math.Max(area.Left,area.Right-(rect.Right-rect.Left))),y=Math.Clamp(rect.Top,area.Top,Math.Max(area.Top,area.Bottom-(rect.Bottom-rect.Top)));
        if(x!=rect.Left||y!=rect.Top)SetWindowPos(handle,IntPtr.Zero,x,y,0,0,0x15);
    }
    public void CollapseToEdge()
    {
        _edge=true;_blink.Stop();_bubbleTimeout.Stop();var tab=Ui.Button("团",ExpandFromEdge,true);tab.Margin=new Thickness(0);tab.ContextMenu=_menu;tab.ToolTip="点击恢复伙伴，右键打开菜单";Content=tab;Width=44;Height=60;
        Dispatcher.BeginInvoke(()=>{if(!IsVisible||!_edge)return;Clamp();var handle=new WindowInteropHelper(this).Handle;if(GetWindowRect(handle,out var rect)){var area=System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;SetWindowPos(handle,IntPtr.Zero,area.Right-(rect.Right-rect.Left),rect.Top,0,0,0x15);}_c.SavePreferences(_c.Preferences with{DisplayMode="edge",PetX=Left,PetY=Top});});
    }
    public void ExpandFromEdge(){_edge=false;Content=_normalContent;_c.SavePreferences(_c.Preferences with{DisplayMode="pet"});Update();Dispatcher.BeginInvoke(Clamp);}
    private void Update()
    {
        Topmost=_c.Preferences.Topmost;if(!_edge){Width=190*_c.Preferences.PetScale;Height=260*_c.Preferences.PetScale;}
        _pet.State=_c.Clock.Active?(_c.Clock.Running&&_c.Clock.Activity!.Kind=="focus"?"focus":"rest"):_c.Busy?"thinking":_c.Preferences.Quiet?"quiet":"idle";_pet.InvalidateVisual();
        if(_edge||_c.Preferences.ReducedMotion||_c.Preferences.Quiet)_blink.Stop();else if(IsVisible)_blink.Start();
        if(_c.Clock.Active){_bubble.Visibility=Visibility.Visible;UpdateTimer();}else if(_timerText.Text.StartsWith("专注")||_timerText.Text.StartsWith("休息")){_timerText.Text="";_bubble.Visibility=Visibility.Collapsed;}
    }
    private void UpdateTimer()
    {
        if(!_c.Clock.Active)return;
        var remaining=TimeSpan.FromSeconds(Math.Ceiling(_c.Clock.Remaining));
        _timerText.Text=(_c.Clock.Activity!.Kind=="focus"?"专注 ":"休息 ")+remaining.ToString(_c.Clock.Remaining>=3600?@"hh\:mm\:ss":@"mm\:ss")+(_c.Clock.Running?"":" · 暂停");
    }
    public void Greet(string text){_timerText.Text=text;_bubble.Visibility=Visibility.Visible;_bubbleTimeout.Stop();_bubbleTimeout.Start();}
}
