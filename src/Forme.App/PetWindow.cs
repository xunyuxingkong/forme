using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using Forme.Core;

namespace Forme.App;

internal sealed class PetWindow : Window
{
    private readonly Controller _c;
    private readonly Action<string> _open;
    private readonly Action _hide;
    private readonly Action _exit;
    private readonly Pet3DView _pet=new();
    private readonly Border _bubble;
    private readonly TextBlock _timerText=Ui.Text("",11);
    private readonly DispatcherTimer _bubbleTimeout=new(){Interval=TimeSpan.FromSeconds(8)};
    private Point _start;
    private bool _dragged;
    private bool _edge;
    private bool _chatOpen;
    internal void ChatOpen(bool open){_chatOpen=open;Update();}
    private readonly Grid _normalContent;
    private readonly ContextMenu _menu;
    private readonly ComboBox _idleChoice;
    private readonly DesktopWander _wander=new();
    private bool _wanderReady;
    private bool _stepActive,_stepRun,_stepLap;
    private bool _allowChatWander;
    private bool CanStep=>_stepActive&&IsVisible&&!_edge&&!_dragged&&!_c.AnimationSuspended;
    private Rect _wanderLimits;
    private string _lastIdle="";
    private string _modelId="";
    private static readonly string[] IdleModes=["原地待机","睡觉","随机走动","随机奔跑"];
    private static readonly string[] IdleIds=["idle","sleep","walk","run"];
    private static readonly string[] ActionNames=["轻轻跳跃","揉揉","摇摆舞","蹦跳舞","转圈舞","吃零食","读书","休息","看看周围"];
    private static readonly PetAction[] Actions=[PetAction.Pat,PetAction.Rub,PetAction.DanceSway,PetAction.DanceHop,PetAction.DanceSpin,PetAction.Eat,PetAction.Read,PetAction.Rest,PetAction.Look];
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window,int index,int value);
    public PetWindow(Controller c,Action<string> open,Action hide,Action exit)
    {
        _c=c;_open=open;_hide=hide;_exit=exit;
        Title="Forme 桌面伙伴";Width=190;Height=260;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=System.Windows.Media.Brushes.Transparent;
        ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=false;Topmost=c.Preferences.Topmost;
        var grid=new Grid();_normalContent=grid;grid.RowDefinitions.Add(new(){Height=new GridLength(85)});grid.RowDefinitions.Add(new(){Height=new GridLength(170)});
        var buttons=Ui.Row(Ui.Button("聊天",()=>_open("chat")),Ui.Button("小屋",()=>_open("home")),Ui.Button("专注",()=>_open("focus")),Ui.Button("放松",()=>_open("relax")));
        foreach(Button b in buttons.Children){b.Padding=new Thickness(7,5,7,5);b.FontSize=11;b.MinHeight=27;b.Margin=new Thickness(2);}
        _idleChoice=Ui.Select(IdleModes,IdleModes[Array.IndexOf(IdleIds,c.Preferences.PetIdleMode)],"桌宠待机模式");_idleChoice.FontSize=11;_idleChoice.MinHeight=27;_idleChoice.Margin=new Thickness(0,0,0,6);
        _idleChoice.SelectionChanged+=(_,_)=>{if(_idleChoice.SelectedIndex>=0&&_c.Preferences.PetIdleMode!=IdleIds[_idleChoice.SelectedIndex])SetIdleMode(IdleIds[_idleChoice.SelectedIndex]);};
        var actionChoice=Ui.Select(ActionNames,ActionNames[0],"选择桌宠动作");actionChoice.FontSize=11;actionChoice.MinHeight=27;actionChoice.Margin=new Thickness(0,0,0,6);
        var actionButtons=Ui.Row(Ui.Button("执行",()=>ExecuteAction(Actions[actionChoice.SelectedIndex])),Ui.Button("停止动作",()=>{if(_c.Busy)_c.StopReply();StopCommands();}));
        foreach(Button b in actionButtons.Children){b.FontSize=11;b.MinHeight=27;b.Padding=new Thickness(7,5,7,5);b.Margin=new Thickness(2);}
        _bubble=Ui.Card(Ui.Stack(_timerText,buttons,Ui.Text("待机运行模式",11),_idleChoice,Ui.Text("打开面板时暂停走动",10,Ui.Muted),Ui.Text("选择动作 · 舞蹈执行一次",11),actionChoice,actionButtons),new Thickness(6));_bubble.Visibility=Visibility.Collapsed;grid.Children.Add(_bubble);
        var box=new Viewbox{Child=_pet,Stretch=System.Windows.Media.Stretch.Uniform};Grid.SetRow(box,1);grid.Children.Add(box);Content=grid;
        _pet.Cursor=Cursors.Hand;_pet.ToolTip="点击互动 · 拖动移动 · 右键菜单";
        _pet.MouseLeftButtonDown+=(_,e)=>{StopCommands();_start=e.GetPosition(this);_dragged=false;_pet.CaptureMouse();e.Handled=true;};
        _pet.MouseMove+=(_,e)=>
        {
            if(e.LeftButton!=MouseButtonState.Pressed || !_pet.IsMouseCaptured)return;
            var point=e.GetPosition(this);
            if(!_dragged && (Math.Abs(point.X-_start.X)>5 || Math.Abs(point.Y-_start.Y)>5))
            {
                _dragged=true;_pet.ReleaseMouseCapture();_pet.Drag(true);
                try{DragMove();Clamp();if(ScreenCoordinateService.TryGetWindowRect(new WindowInteropHelper(this).Handle,out var rect))_c.SavePreferences(_c.Preferences with{PetX=rect.Left,PetY=rect.Top,PetPositionVersion=1});}catch(InvalidOperationException){}
                finally{_pet.Drag(false);_wanderReady=false;}
                Update();
            }
        };
        _pet.MouseLeftButtonUp+=(_,_)=>{_pet.ReleaseMouseCapture();bool wasDragged=_dragged;_dragged=false;if(!wasDragged){_bubble.Visibility=_bubble.IsVisible?Visibility.Collapsed:Visibility.Visible;_bubbleTimeout.Stop();_bubbleTimeout.Start();_pet.Pat();Update();Dispatcher.BeginInvoke(Clamp);if(c.Preferences.Sounds&&!c.Preferences.Quiet)System.Media.SystemSounds.Asterisk.Play();}};
        var menu=new ContextMenu();_menu=menu;
        void Item(string label,Action action){var i=new MenuItem{Header=label};i.Click+=(_,_)=>Ui.Guard(action);menu.Items.Add(i);}
        Item("打开小屋",()=>_open("home"));Item("聊一会儿",()=>_open("chat"));Item("开始专注",()=>_open("focus"));Item("安静模式开关",()=>_c.SavePreferences(_c.Preferences with{Quiet=!_c.Preferences.Quiet}));
        var idleMenu=new MenuItem{Header="待机运行模式"};menu.Items.Add(idleMenu);
        for(int index=0;index<IdleModes.Length;index++){int mode=index;var item=new MenuItem{Header=IdleModes[index]};item.Click+=(_,_)=>Ui.Guard(()=>SetIdleMode(IdleIds[mode]));idleMenu.Items.Add(item);}
        var actionsMenu=new MenuItem{Header="执行动作"};menu.Items.Add(actionsMenu);
        for(int index=0;index<Actions.Length;index++){int action=index;var item=new MenuItem{Header=ActionNames[index]};item.Click+=(_,_)=>Ui.Guard(()=>ExecuteAction(Actions[action]));actionsMenu.Items.Add(item);}
        Item("停止动作",()=>{_pet.StopAction();_wander.Pause();});
        Item("置顶开关",()=>_c.SavePreferences(_c.Preferences with{Topmost=!_c.Preferences.Topmost}));Item("收起到边缘",CollapseToEdge);Item("收起到托盘",_hide);Item("退出",_exit);_pet.ContextMenu=menu;
        _bubbleTimeout.Tick+=(_,_)=>{_bubbleTimeout.Stop();if(!_c.Clock.Active){_bubble.Visibility=Visibility.Collapsed;_timerText.Text="";}Update();};
        SourceInitialized+=(_,_)=>
        {
            var handle=new WindowInteropHelper(this).Handle;
            SetWindowLong(handle,-20,GetWindowLong(handle,-20)|0x08000000|0x00000080); // no-activate + toolwindow
            var source=HwndSource.FromHwnd(handle);source?.AddHook(Hook);
            var area=System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            if(ScreenCoordinateService.TryGetWindowRect(handle,out var rect))
            {
                int scale=ScreenCoordinateService.DipToPhysical(1,this),margin=ScreenCoordinateService.DipToPhysical(24,this);
                int x=c.Preferences.PetX<0?area.Right-rect.Width-margin:(int)Math.Round(c.Preferences.PetPositionVersion==0?c.Preferences.PetX*scale:c.Preferences.PetX);
                int y=c.Preferences.PetY<0?area.Bottom-rect.Height-margin:(int)Math.Round(c.Preferences.PetPositionVersion==0?c.Preferences.PetY*scale:c.Preferences.PetY);
                ScreenCoordinateService.SetPosition(handle,x,y);
                if(c.Preferences.PetPositionVersion==0&&(c.Preferences.PetX>=0||c.Preferences.PetY>=0))c.SavePreferences(c.Preferences with{PetX=x,PetY=y,PetPositionVersion=1});
            }
            Clamp();if(c.Preferences.DisplayMode=="edge")CollapseToEdge();
        };
        c.Changed+=Update;
        c.Tick+=UpdateTimer;
        IsVisibleChanged+=(_,_)=>{_wanderReady=false;Update();};
        Closed+=(_,_)=>{c.Changed-=Update;c.Tick-=UpdateTimer;_bubbleTimeout.Stop();_pet.Release();};
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
        var handle=new WindowInteropHelper(this).Handle;if(handle==IntPtr.Zero||!ScreenCoordinateService.TryGetWindowRect(handle,out var rect))return;
        var clamped=ScreenCoordinateService.Clamp(rect,ScreenCoordinateService.WorkAreaAt(rect));
        if(clamped.Location!=rect.Location)ScreenCoordinateService.SetPosition(handle,clamped.Left,clamped.Top);
    }
    public void CollapseToEdge()
    {
        _edge=true;_wanderReady=false;_bubbleTimeout.Stop();var tab=Ui.Button("团",ExpandFromEdge,true);tab.Margin=new Thickness(0);tab.ContextMenu=_menu;tab.ToolTip="点击恢复伙伴，右键打开菜单";Content=tab;Width=44;Height=60;
        Dispatcher.BeginInvoke(()=>{if(!IsVisible||!_edge)return;Clamp();var handle=new WindowInteropHelper(this).Handle;if(ScreenCoordinateService.TryGetWindowRect(handle,out var rect)){var area=ScreenCoordinateService.WorkAreaAt(rect);ScreenCoordinateService.SetPosition(handle,area.Right-rect.Width,rect.Top);if(ScreenCoordinateService.TryGetWindowRect(handle,out rect))_c.SavePreferences(_c.Preferences with{DisplayMode="edge",PetX=rect.Left,PetY=rect.Top,PetPositionVersion=1});}});
    }
    public void ExpandFromEdge(){_edge=false;Content=_normalContent;_c.SavePreferences(_c.Preferences with{DisplayMode="pet"});Update();Dispatcher.BeginInvoke(Clamp);}
    private void Update()
    {
        if(_modelId!=_c.Preferences.PetModel){_modelId=_c.Preferences.PetModel;_pet.ReplaceModel(PetModels.For(_modelId));}
        _pet.Economy(_c.Preferences.PerformanceMode=="economy");
        Topmost=_c.Preferences.Topmost;
        if(_c.Clock.Active){_bubble.Visibility=Visibility.Visible;UpdateTimer();}else if(_timerText.Text.StartsWith("专注")||_timerText.Text.StartsWith("休息")){_timerText.Text="";_bubble.Visibility=Visibility.Collapsed;}
        if(!_edge){bool expanded=_bubble.Visibility==Visibility.Visible;Width=(expanded?240:190)*_c.Preferences.PetScale;Height=(expanded?435:260)*_c.Preferences.PetScale;_normalContent.RowDefinitions[0].Height=new GridLength(expanded?260:85);}
        var mode=_c.Preferences.PetIdleMode;if(_lastIdle!=mode){_lastIdle=mode;_wanderReady=false;_pet.StopAction();}_idleChoice.SelectedIndex=Array.IndexOf(IdleIds,mode);
        bool roaming=CanWander||CanStep;
        if(!roaming){_wanderReady=false;_wander.Pause();}
        _pet.State=_c.Clock.Active?(_c.Clock.Running&&_c.Clock.Activity!.Kind=="focus"?"focus":"rest"):_c.Busy?"thinking":mode=="sleep"?"sleep":_c.Preferences.Quiet?"quiet":"idle";
        _pet.MotionSettings(_c.Preferences.ReducedMotion,_c.Preferences.Quiet,_c.AnimationSuspended||_edge);
        _pet.DesktopMotion(WanderFrame,roaming,()=>_wander.Moving);
    }
    private bool CanWander=>IsVisible&&(!_chatOpen||_allowChatWander)&&!_edge&&!_dragged&&!_pet.IsMouseCaptured&&!_menu.IsOpen&&_bubble.Visibility!=Visibility.Visible&&!_c.Clock.Active&&!_c.Busy&&!_c.Preferences.Quiet&&!_c.Preferences.ReducedMotion&&!_c.AnimationSuspended&&_c.Preferences.PetIdleMode is "walk" or "run";
    internal void SetIdleMode(string mode){_wanderReady=false;_pet.StopAction();_c.SavePreferences(_c.Preferences with{PetIdleMode=mode});_bubbleTimeout.Stop();_bubbleTimeout.Start();Clamp();}
    internal void SetAiIdleMode(string mode){StopCommands();_allowChatWander=mode is "walk" or "run";_bubble.Visibility=Visibility.Collapsed;SetIdleMode(mode);}
    private int _commandGeneration;
    internal void ExecuteAction(PetAction action){if(_edge)return;_commandGeneration++;_wander.Pause();_wanderReady=false;_pet.Play(action);_bubbleTimeout.Stop();_bubbleTimeout.Start();}
    internal void StopCommands(){_commandGeneration++;_stepActive=false;_stepLap=false;_wander.Pause();_wanderReady=false;_pet.StopAction();}
    internal string StartStep(bool run)
    {
        if(!IsVisible||_edge||_c.AnimationSuspended)throw new OperationFailureException("伙伴隐藏、收起或暂停，移动未执行");
        StopCommands();var handle=new WindowInteropHelper(this).Handle;
        if(!ScreenCoordinateService.TryGetWindowRect(handle,out var rect))throw new OperationFailureException("无法取得伙伴窗口位置");
        var area=ScreenCoordinateService.WorkAreaAt(rect);_wanderLimits=new(area.Left,area.Top,Math.Max(0,area.Width-rect.Width),Math.Max(0,area.Height-rect.Height));
        _wander.Reset(new(rect.Left,rect.Top),_wanderLimits.Left,_wanderLimits.Right,_wanderLimits.Top,_wanderLimits.Bottom);_wanderReady=true;
        if(!_wander.BeginStep((run?180:80)*ScreenCoordinateService.DpiScale(this)))throw new OperationFailureException("当前屏幕工作区没有移动空间");
        _stepRun=run;_stepLap=false;_stepActive=true;
        if(_c.Preferences.ReducedMotion){_wander.FinishStep();ScreenCoordinateService.SetPosition(handle,(int)_wander.Position.X,(int)_wander.Position.Z);_stepActive=false;}
        Update();return run?"伙伴在桌面短途奔跑，结束后停下":"伙伴在桌面走几步，结束后停下";
    }
    internal string StartScreenLaps(int count)
    {
        if(!IsVisible||_edge||_c.AnimationSuspended)throw new OperationFailureException("伙伴隐藏、收起或暂停，屏幕环绕未执行");
        StopCommands();var handle=new WindowInteropHelper(this).Handle;
        if(!ScreenCoordinateService.TryGetWindowRect(handle,out var rect))throw new OperationFailureException("无法取得伙伴窗口位置");
        var area=ScreenCoordinateService.WorkAreaAt(rect);_wanderLimits=new(area.Left,area.Top,Math.Max(0,area.Width-rect.Width),Math.Max(0,area.Height-rect.Height));
        _wander.Reset(new(rect.Left,rect.Top),_wanderLimits.Left,_wanderLimits.Right,_wanderLimits.Top,_wanderLimits.Bottom);
        if(!_wander.BeginLaps(count))throw new OperationFailureException("当前显示器可移动空间太小，无法绕屏幕跑");
        _wanderReady=true;_stepRun=true;_stepLap=true;_stepActive=true;
        if(_c.Preferences.ReducedMotion){_wander.FinishStep();ScreenCoordinateService.SetPosition(handle,(int)_wander.Position.X,(int)_wander.Position.Z);_stepActive=false;_stepLap=false;}
        Update();return $"伙伴开始沿当前显示器工作区跑{count}圈，完成后停下";
    }
    internal async Task<bool> WaitCommand(CompanionCommand command,CancellationToken token)
    {
        if(command.Action is not ("stroll" or "dance" or "pat" or "rub"))return true;
        int generation=_commandGeneration;
        while(true)
        {
            token.ThrowIfCancellationRequested();
            if(!IsVisible||_edge||_dragged||_c.AnimationSuspended||!CompanionCommands.Allowed(command,_c.Preferences)||generation!=_commandGeneration){StopCommands();return false;}
            if(!_pet.Reacting&&!_stepActive)return true;
            await Task.Delay(100,token);
        }
    }
    private PetPose? WanderFrame(double seconds,bool reacting)
    {
        if((!CanWander&&!CanStep)||reacting){_wanderReady=false;_wander.Pause();return null;}
        var handle=new WindowInteropHelper(this).Handle;if(handle==IntPtr.Zero||!ScreenCoordinateService.TryGetWindowRect(handle,out var rect))return null;
        var area=ScreenCoordinateService.WorkAreaAt(rect);
        var limits=new Rect(area.Left,area.Top,Math.Max(0,area.Width-rect.Width),Math.Max(0,area.Height-rect.Height));
        if(!_wanderReady||limits!=_wanderLimits){if(_stepActive){StopCommands();return null;}_wanderLimits=limits;_wander.Reset(new(rect.Left,rect.Top),limits.Left,limits.Right,limits.Top,limits.Bottom);_wanderReady=true;}
        var pose=_wander.Advance(seconds,_stepActive?_stepRun:_c.Preferences.PetIdleMode=="run",ScreenCoordinateService.DpiScale(this),_stepLap?4:1);
        int x=(int)Math.Round(_wander.Position.X),y=(int)Math.Round(_wander.Position.Z);
        if(x!=rect.Left||y!=rect.Top)ScreenCoordinateService.SetPosition(handle,x,y);
        if(_stepActive&&!_wander.Moving){_stepActive=false;_stepLap=false;Dispatcher.BeginInvoke(Update);}
        return pose;
    }
    private void UpdateTimer()
    {
        if(!_c.Clock.Active)return;
        var remaining=TimeSpan.FromSeconds(Math.Ceiling(_c.Clock.Remaining));
        _timerText.Text=(_c.Clock.Activity!.Kind=="focus"?"专注 ":"休息 ")+remaining.ToString(_c.Clock.Remaining>=3600?@"hh\:mm\:ss":@"mm\:ss")+(_c.Clock.Running?"":" · 暂停");
    }
    public void Greet(string text){_timerText.Text=text;_bubble.Visibility=Visibility.Visible;_bubbleTimeout.Stop();_bubbleTimeout.Start();Update();Dispatcher.BeginInvoke(Clamp);}
}
