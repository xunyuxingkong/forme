using Forme.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace Forme.App;

internal sealed partial class Room3DView
{
    private readonly ModelVisual3D _boatView=new();
    private readonly Canvas _boatLabels=new(){IsHitTestVisible=false};
    private readonly Border[] _boatDockTags=[Badge("柳树码头"),Badge("木桥码头"),Badge("睡莲码头")];
    private readonly DispatcherTimer _boatTimer=new(DispatcherPriority.Background);
    private BoatLayout _boatLayout=BoatLayout.Default();
    private readonly Stack<BoatLayout> _boatUndo=new();
    private readonly Dictionary<Model3D,int> _boatLeafHits=new();
    private readonly TranslateTransform3D _shipPosition=new();
    private readonly AxisAngleRotation3D _shipHeading=new(new(0,1,0),90);
    private Model3DGroup? _ship;
    private readonly Model3DGroup _boatPetal=new();
    private PaperBoat? _boatGame;
    private bool _boatOpen,_boatPaused,_boatWaiting;
    private int _boatDragging=-1;
    private BoatLayout? _boatDragBefore;
    private DateTimeOffset _boatDragPaint;
    private double _boatFollowTime;
    private BoatSouvenir? _boatLast;
    public event Action? BoatChanged;
    public BoatLayout BoatSettings=>_boatLayout.Copy();
    public BoatSouvenir? LastBoat=>_boatLast;
    public void NameLastBoat(string title){if(string.IsNullOrWhiteSpace(title)||title.Length>40)throw new InvalidDataException("纪念船名字最多40字。");if(_boatLast is not null)_boatLast=_boatLast with{Title=title};}
    internal bool BoatTimerRunning=>_boatTimer.IsEnabled;
    internal bool BoatOpen=>_boatOpen;
    public bool BoatRunning=>_boatGame is {Complete:false};
    public bool BoatPaused=>_boatPaused;
    public string BoatStatus=>_boatGame is {Complete:true} finished?$"抵达{PaperBoat.DockName(finished.Dock)}！{(finished.PetalFound?"途中遇到了小花瓣。":"")}可以收藏这次旅行，或换一种摆法。":BoatRunning?(_boatPaused?"已暂停，可以继续或捞回纸船。":_boatWaiting?"伙伴正在赶往岸边，纸船马上出发。":$"纸船旅行中 · {_boatGame!.Elapsed:0.0}秒 · 可以随时暂停或捞回"):_boatLast is not null?"上次旅行已完成；可以收藏，或开始下一次。":"先选船形与风向，再拖动叶片。虚线是路线预览，放船后会按这条路线前进。";
    private static Point3D BoatPoint(GroundPoint p,double y=.10)=>new(-3.6+p.X*.65,y,-5+p.Z*.60);
    private void InitBoats()
    {
        _viewport.Children.Add(_boatView);_boatTimer.Tick+=BoatTick;
        foreach(var tag in _boatDockTags)_boatLabels.Children.Add(tag);Children.Add(_boatLabels);
        _viewport.PreviewKeyDown+=BoatKey;
        _viewport.Focusable=true;_viewport.MouseLeftButtonDown+=(_,_)=>_viewport.Focus();
    }
    public void OpenBoats()
    {
        if(_boatOpen)return;
        SwitchScene(true);_boatOpen=true;_pendingInteraction=null;_ballVisual.Content=null;
        _worldPetScale.ScaleX=_worldPetScale.ScaleY=_worldPetScale.ScaleZ=.72;
        _roomInteractions.Visibility=Visibility.Collapsed;_sceneCaption.Text="拖动绿叶改变水流 · 拖空地旋转 · 滚轮缩放";
        _azimuth=15;_elevation=44;_zoom=1;BuildBoatWorld();Camera();_animator.MoveTo(new(-5.0,-2.85));BoatChanged?.Invoke();
    }
    public void CloseBoats()
    {
        _boatOpen=false;_boatTimer.Stop();_boatGame=null;_boatDragging=-1;_boatView.Content=null;_boatLeafHits.Clear();
        _worldPetScale.ScaleX=_worldPetScale.ScaleY=_worldPetScale.ScaleZ=1;
        _roomInteractions.Visibility=Visibility.Visible;_sceneCaption.Text="点击地面行走 · 远处奔跑 · 拖动旋转 · 滚轮缩放";
        _animator.StopAction();_travel.Stop();_azimuth=30;_elevation=32;_zoom=1;Camera();BoatChanged?.Invoke();
    }
    public void ConfigureBoat(BoatLayout layout,bool remember=true)
    {
        if(BoatRunning)throw new OperationFailureException("先捞回纸船，再修改路线。");layout.Validate();
        if(remember)RememberBoatLayout(_boatLayout);
        _boatLayout=layout.Copy();_boatGame=null;if(_boatOpen)BuildBoatWorld();BoatChanged?.Invoke();
    }
    public void UndoBoat(){if(BoatRunning)throw new OperationFailureException("先捞回纸船，再撤销布置。");if(_boatUndo.TryPop(out var layout))ConfigureBoat(layout,false);}
    private void RememberBoatLayout(BoatLayout layout)
    {
        if(_boatUndo.Count>=20){var recent=_boatUndo.Take(19).Reverse().ToArray();_boatUndo.Clear();foreach(var item in recent)_boatUndo.Push(item);}
        _boatUndo.Push(layout.Copy());
    }
    public void StartBoat()
    {
        if(!_boatOpen||!IsVisible||!_motionVisible||_c.AnimationSuspended)throw new OperationFailureException("请先显示纸船场景。");
        if(BoatRunning)return;
        _pendingInteraction=null;_boatLast=null;_boatPaused=false;_boatFollowTime=0;_boatGame=new(_boatLayout);
        _boatWaiting=_animator.MoveTo(new(-5.0,-2.85))&&_travel.Moving;BuildBoatWorld();
        if(_c.Preferences.ReducedMotion){_boatWaiting=false;for(int i=0;i<500&&!_boatGame.Complete;i++)_boatGame.Advance(.05);FinishBoat();}
        else{if(!_boatWaiting)_animator.Play(PetAction.Launch);SyncBoatTimer();}
        BoatChanged?.Invoke();
    }
    public void PauseBoat(){if(!BoatRunning)return;_boatPaused=!_boatPaused;if(_boatPaused){_travel.Stop();_boatWaiting=false;}SyncBoatTimer();BoatChanged?.Invoke();}
    public void RetrieveBoat(){_boatTimer.Stop();_boatGame=null;_boatPaused=false;_boatWaiting=false;_travel.Stop();_animator.StopAction();if(_boatOpen)BuildBoatWorld();BoatChanged?.Invoke();}
    private void SyncBoatTimer()
    {
        if(!_boatOpen||!BoatRunning||_released){_boatTimer.Stop();return;}
        if(!IsVisible||!IsLoaded||!_motionVisible||_c.AnimationSuspended)
        {bool changed=!_boatPaused;_boatPaused=true;_boatTimer.Stop();if(changed)BoatChanged?.Invoke();return;}
        if(_boatPaused){_boatTimer.Stop();return;}
        _boatTimer.Interval=TimeSpan.FromMilliseconds(_c.Preferences.PerformanceMode=="economy"?100:50);_boatTimer.Start();
    }
    private void BoatTick(object? sender,EventArgs e)
    {
        if(!_boatOpen||!IsVisible||!_motionVisible||_c.AnimationSuspended){SyncBoatTimer();return;}
        if(_boatGame is not {} game){_boatTimer.Stop();return;}
        if(_boatWaiting){if(_travel.Moving)return;_boatWaiting=false;_animator.Play(PetAction.Launch);}
        game.Advance(_boatTimer.Interval.TotalSeconds);PositionShip(game.Position,game.Heading,game.Elapsed);
        if(game.PetalFound&&_boatPetal.Children.Count>0){_boatPetal.Children.Clear();_hint.Text="纸船带上了小花瓣，这次旅行有新发现！";_animator.Play(PetAction.Pat);}
        _boatFollowTime+=_boatTimer.Interval.TotalSeconds;
        if(_boatFollowTime>=1){_boatFollowTime=0;_animator.MoveTo(new(-3.6+game.Position.X*.55,-2.85));BoatChanged?.Invoke();}
        if(game.Complete)FinishBoat();
    }
    private void FinishBoat()
    {
        _boatTimer.Stop();if(_boatGame is not {Complete:true} game)return;
        PositionShip(game.Position,game.Heading,0);_travel.Stop();_heading.Angle=0;_animator.Play(PetAction.Cheer);
        _boatLast=new(Guid.NewGuid().ToString("N"),PaperBoat.DockName(game.Dock)+"的小旅行",game.Layout.Copy(),game.Dock!,game.PetalFound,_c.Preferences.PetModel,DateTimeOffset.Now);
        _hint.Text=game.PetalFound?"船带回了一片小花瓣，伙伴开心地拍了拍手。":"到岸啦！伙伴为你的小船欢呼。";BoatChanged?.Invoke();
    }
    private void PositionShip(GroundPoint p,double heading,double elapsed)
    {var world=BoatPoint(p);_shipPosition.OffsetX=world.X;_shipPosition.OffsetY=world.Y+(elapsed==0?0:.009*Math.Sin(elapsed*4));_shipPosition.OffsetZ=world.Z;_shipHeading.Angle=heading;}
    private void PositionBoatLabels()
    {
        for(int i=0;i<_boatDockTags.Length;i++)
        {
            var tag=_boatDockTags[i];var point=Project(BoatPoint(new(2.8,(i-1)*1.3),.33));
            tag.Visibility=_boatOpen&&point is {} p&&p.X>35&&p.X<ActualWidth-35&&p.Y>55&&p.Y<ActualHeight-35?Visibility.Visible:Visibility.Collapsed;
            if(point is {} projected){Canvas.SetLeft(tag,projected.X-23+(ActualHeight<360?(i-1)*62:0));Canvas.SetTop(tag,projected.Y-28);}
        }
    }
    private int BoatLeafAt(Point point)
    {
        int selected=-1;if(!_boatOpen||BoatRunning)return selected;
        VisualTreeHelper.HitTest(_viewport,null,result=>
        {if(result is RayMeshGeometry3DHitTestResult hit){_boatLeafHits.TryGetValue(hit.ModelHit,out var index);if(_boatLeafHits.ContainsKey(hit.ModelHit))selected=index;return HitTestResultBehavior.Stop;}return HitTestResultBehavior.Continue;},new PointHitTestParameters(point));return selected;
    }
    private bool BeginBoatDrag(Point point)
    {int index=BoatLeafAt(point);if(index<0)return false;_boatDragging=index;_boatDragBefore=_boatLayout.Copy();_viewport.CaptureMouse();_hint.Text=$"正在移动第{index+1}片叶子 · 松开保存 · Esc取消";return true;}
    private void MoveBoatDrag(Point point)
    {
        if(_boatDragging<0||DateTimeOffset.UtcNow-_boatDragPaint<TimeSpan.FromMilliseconds(80))return;_boatDragPaint=DateTimeOffset.UtcNow;
        var forward=_camera.LookDirection;forward.Normalize();var right=Vector3D.CrossProduct(forward,_camera.UpDirection);right.Normalize();var up=Vector3D.CrossProduct(right,forward);
        double scale=2*Math.Tan(_camera.FieldOfView*Math.PI/360)/ActualWidth;var ray=forward+right*((point.X-ActualWidth/2)*scale)+up*((ActualHeight/2-point.Y)*scale);
        if(ray.Y>=-.001)return;var world=_camera.Position+ray*((.14-_camera.Position.Y)/ray.Y);
        var leaves=_boatLayout.Leaves.ToList();leaves[_boatDragging]=leaves[_boatDragging] with{X=Math.Clamp((world.X+3.6)/.65,-2,2),Z=Math.Clamp((world.Z+5)/.60,-1.35,1.35)};
        _boatLayout=_boatLayout with{Leaves=leaves};_boatGame=null;BuildBoatWorld();BoatChanged?.Invoke();
    }
    private void EndBoatDrag(bool cancel=false)
    {
        if(_boatDragging<0)return;
        if(_boatDragBefore is {} before){if(cancel)_boatLayout=before;else RememberBoatLayout(before);}
        _boatDragging=-1;_boatDragBefore=null;_viewport.ReleaseMouseCapture();BuildBoatWorld();BoatChanged?.Invoke();
    }
    private void BoatKey(object sender,KeyEventArgs e)
    {
        if(!_boatOpen)return;
        if(e.Key==Key.Escape){if(_boatDragging>=0)EndBoatDrag(true);else StopFromUser();e.Handled=true;}
        if(e.Key==Key.Z&&Keyboard.Modifiers==ModifierKeys.Control){Ui.Guard(UndoBoat);e.Handled=true;}
    }
    internal Point? ProjectBoatLeaf(int index)=>Project(BoatPoint(new(_boatLayout.Leaves[index].X,_boatLayout.Leaves[index].Z),.15));
    internal bool DragBoatForCheck(int index,Point point){var start=ProjectBoatLeaf(index);if(start is null||!BeginBoatDrag(start.Value))return false;MoveBoatDrag(point);EndBoatDrag();return true;}
}
