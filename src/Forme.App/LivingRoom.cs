using Forme.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
namespace Forme.App;

internal sealed partial class Room3DView
{
    private LivingWorld? _worldPreview;
    private int _activityGeneration;
    private readonly ModelVisual3D _worldOverlay=new();
    private readonly Model3DGroup _worldMarks=new();
    private string? _worldSelection,_hideTarget,_pendingLife;
    private GroundPoint? _moveTarget;
    private bool _editing,_furnitureDrag,_throwDrag;
    private string _hideMode="",_hideStatus="选择一件家具藏好小球，或者让伙伴藏、你来找。";
    private readonly Stack<LivingWorld> _worldUndo=new();
    private LivingWorld? _dragLayout;
    private DateTimeOffset _worldPaint;
    private readonly DispatcherTimer _toyTimer=new(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(33)};
    private GroundPoint _toyFrom,_toyTo;
    private double _toyTime;
    private long _toyStarted;
    private string _selectedToy="ball-yellow",_activeToy="ball-yellow";
    internal LivingWorld CurrentWorld=>_worldPreview??_c.Store.World();
    internal string? SelectedFurniture=>_worldSelection;
    internal bool EditingFurniture=>_editing;
    internal bool HideActive=>_hideMode!="";
    internal string HideStatus=>_hideStatus;
    internal bool ToyRunning=>_toyTimer.IsEnabled;
    internal IReadOnlyList<GameItem> OwnedToys
    {
        get{var owned=_c.Store.GameInventory().Where(x=>x.Quantity>0).Select(x=>x.ItemId).ToHashSet(StringComparer.Ordinal);return GameProgression.Catalog.Where(x=>x.Kind=="toy"&&owned.Contains(x.Id)).ToArray();}
    }
    internal string SelectedToy=>_selectedToy;
    internal bool SelectToy(string id)
    {
        if(!OwnedToys.Any(x=>x.Id==id))return false;_selectedToy=id;SetToyVisual(id);return true;
    }
    internal event Action? WorldChanged;
    private void InitLiving()
    {
        _worldOverlay.Content=_worldMarks;_viewport.Children.Add(_worldOverlay);_toyTimer.Tick+=ToyTick;
        _viewport.MouseRightButtonUp+=(_,e)=>{if(_editing||_boatOpen)return;string? id=Hit(e.GetPosition(_viewport));if(id is null)return;OpenObjectMenu(id);e.Handled=true;};
        _viewport.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){if(_furnitureDrag)EndFurnitureDrag(true);else StopFromUser();e.Handled=true;}else if(_editing&&e.Key==Key.Z&&Keyboard.Modifiers==ModifierKeys.Control){UndoFurniture();e.Handled=true;}};
    }
    internal ContextMenu OpenObjectMenu(string id)
    {
        var menu=new ContextMenu();void item(string title,Action action){var entry=new MenuItem{Header=title};entry.Click+=(_,_)=>Ui.Guard(action);menu.Items.Add(entry);}item("使用这个物件",()=>ActivateObject(id));item("和伙伴玩藏物",()=>_navigate("hide"));item("布置家具",()=>{_navigate("furniture");SelectFurniture(FurnitureHit(id));});item("叫回伙伴",RecallFromUser);_viewport.ContextMenu=menu;menu.IsOpen=true;return menu;
    }
    private static string? FurnitureHit(string? id)=>id?.StartsWith("furniture:",StringComparison.Ordinal)==true?id[10..]:null;
    internal void OpenFurniture(){StopWorld();if(_boatOpen)CloseBoats();SwitchScene(false);_editing=true;_worldPreview=_c.Store.World().Copy();_worldUndo.Clear();_sceneCaption.Text="拖动家具摆放 · 侧栏旋转 · Ctrl+Z撤销 · Esc取消";SelectFurniture(_worldPreview.Items.FirstOrDefault()?.Id);Refresh();}
    internal void CloseFurniture(){_editing=false;_worldPreview=null;_worldSelection=null;_furnitureDrag=false;_worldUndo.Clear();_sceneCaption.Text="点击地面行走 · 拖动旋转 · 右键物件更多动作";_appearance=null;RebuildWorldTravel();Refresh();PaintWorld();}
    internal void SelectFurniture(string? id){_worldSelection=id;PaintWorld();WorldChanged?.Invoke();}
    internal void ChangeFurniture(LivingWorld next,bool remember=true)
    {
        next.Validate();if(!_editing)throw new InvalidDataException("请先进入家具布置。");if(remember){if(_worldUndo.Count>=20){var recent=_worldUndo.Take(19).Reverse().ToArray();_worldUndo.Clear();foreach(var value in recent)_worldUndo.Push(value);}_worldUndo.Push(CurrentWorld.Copy());}
        _worldPreview=next;_appearance=null;Refresh();PaintWorld();WorldChanged?.Invoke();
    }
    internal void UndoFurniture(){if(_worldUndo.TryPop(out var previous))ChangeFurniture(previous,false);}
    internal void SaveFurniture(){var next=CurrentWorld.Copy();_c.Store.SaveWorld(next);_worldPreview=next;RebuildWorldTravel();_hint.Text="布置已保存，通道检查通过。";_c.Refresh();WorldChanged?.Invoke();}
    private void RebuildWorldTravel()
    {
        if(_outdoors)return;var before=_travel.Position;_travel=CurrentWorld.Travel();_travel.Reset(before);if(!_travel.Walkable(before))_travel.Reset(new(.15,1.25));
        _animator.AttachTravel(_travel,(point,heading)=>{_position.OffsetX=point.X;_position.OffsetY=.035;_position.OffsetZ=point.Z;_heading.Angle=heading;});
    }
    private void ApplyFurniture(Model3DGroup scene)
    {
        var templates=new Dictionary<string,Model3DGroup>();
        foreach(var child in scene.Children.OfType<Model3DGroup>().ToArray())
        {
            var kind=child.Children.Select(c=>_hits.GetValueOrDefault(c)).FirstOrDefault(h=>h?.StartsWith("interact:",StringComparison.Ordinal)==true)?[9..];
            if(kind is null||!LivingWorld.Catalog.Any(k=>k.Id==kind))continue;templates[kind]=child;scene.Children.Remove(child);foreach(var part in child.Children)_hits.Remove(part);
        }
        var cushion=new Model3DGroup();MeshArt.Ellipse(cushion,"#D4B2A6",-1,.12,1.8,.45,.12,.45);MeshArt.Ellipse(cushion,"#F1DBBD",-1,.23,1.8,.37,.04,.37);templates["cushion"]=cushion;
        foreach(var item in CurrentWorld.Items)
        {
            if(!templates.TryGetValue(item.Kind,out var source))continue;var kind=LivingWorld.Kind(item.Kind);var group=new Model3DGroup();
            // Each instance owns picking nodes, while immutable meshes/materials remain shared.
            foreach(var part in source.Children){Model3D instance=part is GeometryModel3D geometry?new GeometryModel3D(geometry.Geometry,geometry.Material){BackMaterial=geometry.BackMaterial,Transform=geometry.Transform}:part.Clone();group.Children.Add(instance);_hits[instance]="furniture:"+item.Id;}
            var transform=new Transform3DGroup();transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new(0,1,0),item.Rotation),new Point3D(kind.X,0,kind.Z)));transform.Children.Add(new TranslateTransform3D(item.X-kind.X,0,item.Z-kind.Z));group.Transform=transform;scene.Children.Add(group);
        }
    }
    private string? HoverFurniture(string? hit)=>FurnitureHit(hit)??hit;
    private void PaintWorld(string? hover=null)
    {
        _worldMarks.Children.Clear();if(_released)return;
        var item=CurrentWorld.Items.FirstOrDefault(i=>i.Id==(_editing?_worldSelection:hover));
        if(!_outdoors&&item is not null){var b=LivingWorld.Footprint(item);string color=_editing&&CurrentWorld.PlacementProblem()!=null?"#C77A67":"#E8C876";MeshArt.Box(_worldMarks,color,b.X,.09,b.Z-b.Depth/2-.06,b.Width+.12,.018,.03);MeshArt.Box(_worldMarks,color,b.X,.09,b.Z+b.Depth/2+.06,b.Width+.12,.018,.03);foreach(double x in new[]{b.X-b.Width/2-.06,b.X+b.Width/2+.06})MeshArt.Box(_worldMarks,color,x,.09,b.Z,.03,.018,b.Depth+.12);}
        if(_moveTarget is {} target){MeshArt.Ellipse(_worldMarks,_travel.Walkable(target)?"#E7BE70":"#C77A67",target.X,.105,target.Z,.14,.025,.14);foreach(var point in _travel.Route.Take(20))MeshArt.Ellipse(_worldMarks,"#D2BB83",point.X,.095,point.Z,.025,.012,.025);if(_throwDrag)for(int i=1;i<9;i++){double t=i/9d;MeshArt.Ellipse(_worldMarks,"#E6CC8A",_travel.Position.X+(target.X-_travel.Position.X)*t,.22+Math.Sin(t*Math.PI)*1.1,_travel.Position.Z+(target.Z-_travel.Position.Z)*t,.032,.032,.032);}}
        if(!_editing&&item is null&&hover is not null)
        {
            GroundObstacle? b=hover switch{"focus" or "mood"=>new(-2.25,-2.29,2.83,1.28),"relax"=>new(2.36,-2.1,3.25,1.7),"plant"=>new(2.12,3.73,.75,.75),"garden"=>new(-1.96,4.32,1.45,1.88),"chat"=>new(_travel.Position.X,_travel.Position.Z,1.3,1),"interact:pond"=>new(-3.6,-5,4.3,2.5),"interact:picnic"=>new(3.8,4.7,2.7,2.1),"interact:rest"=>new(5.45,-5.1,3,2.7),"interact:bell"=>new(.2,-7.7,1.3,.4),_=>null};
            if(b is {} box){foreach(double z in new[]{box.Z-box.Depth/2,box.Z+box.Depth/2})MeshArt.Box(_worldMarks,"#E8C876",box.X,.09,z,box.Width,.02,.03);foreach(double x in new[]{box.X-box.Width/2,box.X+box.Width/2})MeshArt.Box(_worldMarks,"#E8C876",x,.09,box.Z,.03,.02,box.Depth);}
        }
    }
    private bool BeginFurnitureDrag(Point point)
    {
        if(!_editing)return false;var id=FurnitureHit(Hit(point));SelectFurniture(id);if(id is null)return false;_dragLayout=CurrentWorld.Copy();_furnitureDrag=true;_viewport.CaptureMouse();return true;
    }
    private GroundPoint? FloorRay(Point point)
    {
        var forward=_camera.LookDirection;forward.Normalize();var right=Vector3D.CrossProduct(forward,_camera.UpDirection);right.Normalize();var up=Vector3D.CrossProduct(right,forward);double scale=2*Math.Tan(_camera.FieldOfView*Math.PI/360)/ActualWidth;var ray=forward+right*((point.X-ActualWidth/2)*scale)+up*((ActualHeight/2-point.Y)*scale);if(ray.Y>=-.001)return null;var world=_camera.Position+ray*((.03-_camera.Position.Y)/ray.Y);return new(world.X,world.Z);
    }
    private void MoveFurnitureDrag(Point point)
    {
        if(DateTimeOffset.UtcNow-_worldPaint<TimeSpan.FromMilliseconds(80)||FloorRay(point) is not {} p)return;_worldPaint=DateTimeOffset.UtcNow;var next=CurrentWorld.Copy();int index=next.Items.FindIndex(i=>i.Id==_worldSelection);if(index<0)return;var item=next.Items[index];var box=LivingWorld.Footprint(item);next.Items[index]=item with{X=Math.Clamp(Math.Round(p.X*4)/4,-4.3+box.Width/2,4.3-box.Width/2),Z=Math.Clamp(Math.Round(p.Z*4)/4,-3.6+box.Depth/2,5.65-box.Depth/2)};ChangeFurniture(next,false);
    }
    private void EndFurnitureDrag(bool cancel=false){if(!_furnitureDrag)return;_furnitureDrag=false;if(_dragLayout is {} before){if(cancel)ChangeFurniture(before,false);else{var now=CurrentWorld.Copy();_worldPreview=before;ChangeFurniture(now);}}_dragLayout=null;_viewport.ReleaseMouseCapture();}
    internal void StopWorld()
    {
        _activityGeneration++;
        if(_furnitureDrag)EndFurnitureDrag(true);if(_hideMode!="")_hideStatus="本局已结束，可以换个藏法再玩。";_throwDrag=false;if(_viewport.IsMouseCaptured)_viewport.ReleaseMouseCapture();_hideChecked.Clear();_toyTimer.Stop();_ballVisual.Content=null;_pendingInteraction=null;_pendingLife=null;_moveTarget=null;_hideMode="";_hideTarget=null;_travel.Stop();_animator.StopAction();if(_boatOpen&&BoatRunning)RetrieveBoat();_hint.Text="活动已停止，伙伴留在原地。";PaintWorld();WorldChanged?.Invoke();
    }
    internal void RecallPet(){StopWorld();TryMove(new(.15,1.25));}
    internal void StopFromUser(){if(_c.Busy)_c.StopReply();StopWorld();}
    internal void RecallFromUser(){if(_c.Busy)_c.StopReply();RecallPet();}
    private void ActivateObject(string id)
    {
        if(id=="toy"){PickToy();return;}if(FurnitureHit(id) is {} furniture){if(HideActive){GuessHidden(furniture);return;}UseFurniture(furniture);return;}if(id.StartsWith("interact:",StringComparison.Ordinal))Interact(id[9..]);else _navigate(PageOf(id)!);
    }
    internal bool UseFurniture(string id,string? action=null)
    {
        var item=CurrentWorld.Items.FirstOrDefault(i=>i.Id==id);if(item is null||!IsVisible||!_motionVisible||_c.AnimationSuspended)return false;
        if(item.Kind=="lamp"){Interact("lamp");return true;}StopWorld();var point=LivingWorld.Approach(item,_travel);if(point is null){_hint.Text="没有可达位置，请在布置页留出通道。";return false;}_animator.MoveTo(point.Value);_pendingInteraction=action??LivingWorld.Kind(item.Kind).Action;_moveTarget=point;_hint.Text="前往"+LivingWorld.Kind(item.Kind).Name;PaintWorld();if(!_travel.Moving){var pending=_pendingInteraction;_pendingInteraction=null;CompleteInteraction(pending!);}return true;
    }
    internal void RunLife(LifeEvent life){if(!UseFurniture(life.Target,life.Action))return;if(_pendingInteraction is not null)_pendingLife=life.Id;else{_c.Store.DiscoverLife(life.Id);_c.Store.RecordGameAction(DateOnly.FromDateTime(DateTime.Now),"life");_c.Refresh();}_hint.Text=life.Title+"："+life.Description;}
    internal void StartHide(bool petHides,string? selected)
    {
        StopWorld();if(_outdoors)SwitchScene(false);var items=CurrentWorld.Items;if(items.Count<2)throw new InvalidDataException("至少摆两件家具再玩藏物。");_hideMode=petHides?"player":"pet";
        _hideTarget=petHides?items[Random.Shared.Next(items.Count)].Id:selected;if(!items.Any(i=>i.Id==_hideTarget))throw new InvalidDataException("请先选择藏物家具。");
        var hidden=items.First(i=>i.Id==_hideTarget);_hideStatus=petHides?$"伙伴藏好了！线索：{(hidden.Z<0?"在小屋后半边":"在小屋前半边")}，{(hidden.X<0?"偏左":"偏右")}。点击家具或侧栏选择来找。":"小球藏好了，伙伴正在逐个寻找。";
        if(!petHides)ContinuePetSearch();WorldChanged?.Invoke();
    }
    private readonly HashSet<string> _hideChecked=new();
    private void ContinuePetSearch()
    {
        string favorite=PetBehaviors.For(_modelId).FavoriteFurniture;var items=CurrentWorld.Items.Where(i=>!_hideChecked.Contains(i.Id)).OrderBy(i=>i.Kind==favorite?0:1).ThenBy(i=>Math.Abs(i.X-_travel.Position.X)+Math.Abs(i.Z-_travel.Position.Z)).ToArray();
        foreach(var item in items){var p=LivingWorld.Approach(item,_travel);if(p is null)continue;_hideChecked.Add(item.Id);_animator.MoveTo(p.Value);_pendingInteraction="search:"+item.Id;_moveTarget=p;_hint.Text="伙伴去看看"+LivingWorld.Kind(item.Kind).Name;PaintWorld();if(!_travel.Moving)FinishSearch(item.Id);return;}_hideStatus="伙伴找不到通道，调整家具或结束这一局。";WorldChanged?.Invoke();
    }
    private void FinishSearch(string id)
    {
        if(_hideMode!="pet")return;if(id==_hideTarget){FoundHidden();return;}_animator.Play(PetAction.Look);_hideStatus=LivingWorld.Kind(CurrentWorld.Items.First(i=>i.Id==id).Kind).Name+"这里没有，再找一处。";WorldChanged?.Invoke();ContinuePetSearch();
    }
    internal void GuessHidden(string id){if(_hideMode!="player")return;if(id==_hideTarget)FoundHidden();else{_hideStatus="这里没有小球，再根据位置线索试试。";_animator.Play(PetAction.Look);WorldChanged?.Invoke();}}
    private void FoundHidden(){_hideMode="";_hideChecked.Clear();_pendingInteraction=null;_moveTarget=null;_animator.Play(PetAction.Cheer);_hideStatus="找到小球啦！换个藏法可以再玩。";_hint.Text=_hideStatus;var item=CurrentWorld.Items.First(i=>i.Id==_hideTarget);_ballPosition.OffsetX=item.X;_ballPosition.OffsetY=.35;_ballPosition.OffsetZ=item.Z;_ballVisual.Content=_ballGroup;PaintWorld();WorldChanged?.Invoke();}
    internal void EndHide(){StopWorld();_hideChecked.Clear();_hideStatus="这局已结束。可以换一件家具重新藏。";WorldChanged?.Invoke();}
    private void ToyTick(object? sender,EventArgs e)
    {
        if(!IsVisible||!_motionVisible||_c.AnimationSuspended){_toyTimer.Stop();_ballVisual.Content=null;return;}_toyTime=_c.Preferences.ReducedMotion?.75:System.Diagnostics.Stopwatch.GetElapsedTime(_toyStarted).TotalSeconds;double t=Math.Min(1,_toyTime/.75);_ballPosition.OffsetX=_toyFrom.X+(_toyTo.X-_toyFrom.X)*t;_ballPosition.OffsetZ=_toyFrom.Z+(_toyTo.Z-_toyFrom.Z)*t;_ballPosition.OffsetY=Math.Sin(t*Math.PI)*1.1;
        if(t>=1){_toyTimer.Stop();_animator.MoveTo(_toyTo);_pendingInteraction="toy:"+_activeToy;_moveTarget=_toyTo;PaintWorld();if(!_travel.Moving){_pendingInteraction=null;CompleteInteraction("toy:"+_activeToy);}}
    }
    internal bool ThrowToy(GroundPoint target,string? toyId=null)
    {
        if(toyId is not null&&!SelectToy(toyId)){_hint.Text="这件玩具还没有放进收藏。";return false;}
        if(!_travel.Walkable(target)){_hint.Text="落点被家具挡住，换一块空地。";return false;}StopWorld();_activeToy=_selectedToy;SetToyVisual(_activeToy);_toyFrom=_travel.Position;_toyTo=target;_toyTime=0;_ballPosition.OffsetX=_toyFrom.X;_ballPosition.OffsetZ=_toyFrom.Z;_ballPosition.OffsetY=.4;_ballVisual.Content=_ballGroup;_animator.Play(PetAction.Launch);_hint.Text=GameProgression.Catalog.First(x=>x.Id==_activeToy).Name+"飞出去了，伙伴随后去捡。";
        _toyStarted=System.Diagnostics.Stopwatch.GetTimestamp();if(_c.Preferences.ReducedMotion){_toyTime=.75;ToyTick(null,EventArgs.Empty);}else _toyTimer.Start();return true;
    }
    internal bool PlaySelectedToy()
    {
        var targets=_outdoors?new[]{new GroundPoint(2,2),new(-2,2),new(0,-4),new(4,4)}:new[]{new GroundPoint(.2,-.3),new(-.8,1),new(1,1),new(-1,-1)};
        foreach(var target in targets)if(_travel.Walkable(target))return ThrowToy(target);_hint.Text="周围有些拥挤，换个场景或落点再玩。";return false;
    }
    internal void PickToy(){StopWorld();_activeToy=_selectedToy;SetToyVisual(_activeToy);_ballPosition.OffsetX=_travel.Position.X;_ballPosition.OffsetY=.45;_ballPosition.OffsetZ=_travel.Position.Z+.5;_ballVisual.Content=_ballGroup;_animator.Play(PetAction.Launch);_hint.Text="已拿起"+GameProgression.Catalog.First(x=>x.Id==_activeToy).Name+" · 按住 Shift 拖动抛出。";}
    private string BlockedReason(GroundPoint point)
    {
        if(!_outdoors){var item=CurrentWorld.Items.FirstOrDefault(i=>{var b=LivingWorld.Footprint(i);return i.Kind is not ("sleep" or "cushion" or "feed")&&Math.Abs(point.X-b.X)<b.Width/2+.38&&Math.Abs(point.Z-b.Z)<b.Depth/2+.38;});if(item is not null)return LivingWorld.Kind(item.Kind).Name+"挡住了落点，请选旁边的空地。";}
        return "这里被固定家具或场景边界挡住，请点击空地。";
    }
    internal bool DragFurnitureForCheck(string id,double dx,double dy){Point? start=null;for(int x=8;x<ActualWidth&&start is null;x+=8)for(int y=8;y<ActualHeight;y+=8)if(FurnitureHit(Hit(new(x,y)))==id){start=new(x,y);break;}if(start is null||!BeginFurnitureDrag(start.Value))return false;_worldPaint=DateTimeOffset.MinValue;MoveFurnitureDrag(new(start.Value.X+dx,start.Value.Y+dy));EndFurnitureDrag();return true;}
}
