using Forme.Core;
using System.Windows;

namespace Forme.App;

internal sealed partial class DesktopHost : ICompanionActivityWorld
{
    private void InitializeActivitySelection()=>_c.ActivityWorld=this;
    ActivityObservation ICompanionActivityWorld.Observe(CompanionView view)
    {
        var p=_c.Preferences;
        bool available=!_exit&&!_c.AnimationSuspended;
        string subject;int revision;SceneKind scene;string state;
        if(view==CompanionView.Room)
        {
            var room=House;
            subject=room?.ActivitySubject??"";revision=room?.ActivityRevision??-1;scene=room?.CompanionScene??SceneKind.Indoor;
            available&=room is {IsVisible:true}&&room.WindowState!=WindowState.Minimized&&room.ActivityAvailable&&p.ShowHome3D;
            state=room?.ActivityMode??"hidden";
        }
        else
        {
            subject=_pet?.ActivitySubject??"";revision=_pet?.ActivityRevision??-1;scene=SceneKind.Desktop;
            available&=_pet is {IsVisible:true}&&_pet.ActivityAvailable&&p.DisplayMode=="pet";
            state=available?"idle":"unavailable";
        }
        var options=available?ActivitySelection.Options(view,scene,p.PetModel,p,plan=>SelectionPreflight(view,plan),view==CompanionView.Room?House?.AvailableLife():null):Array.Empty<ActivityOption>();
        return new(view,subject,revision,scene,p.PetModel,p.Weather,p.RoomLamp,available,state,options);
    }
    private ActionResult? SelectionPreflight(CompanionView view,ActionPlan plan)
    {
        if(_exit||_c.AnimationSuspended)return ActionResult.Failure(ActionResultCode.SceneUnavailable,"应用正在暂停，没有执行。");
        if(view==CompanionView.Room)
        {
            if(House is not {IsVisible:true}||House.WindowState==WindowState.Minimized||!_c.Preferences.ShowHome3D||!House.ActivityVisible)
                return ActionResult.Failure(ActionResultCode.SceneUnavailable,"小屋伙伴已隐藏或暂停，没有执行。");
            return House.PreflightActions(plan);
        }
        if(_pet is not {IsVisible:true}||!_pet.ActivityVisible||_c.Preferences.DisplayMode!="pet")
            return ActionResult.Failure(ActionResultCode.SceneUnavailable,"桌面伙伴已隐藏或暂停，没有执行。");
        if(plan.Steps.Any(x=>x.Action is not ("stroll" or "dance")))
            return ActionResult.Failure(ActionResultCode.TargetUnavailable,"该活动不属于桌面伙伴。");
        return null;
    }
    ActionResult? ICompanionActivityWorld.Preflight(CompanionView view,ActionPlan plan)=>SelectionPreflight(view,plan);
    ActionResult ICompanionActivityWorld.Start(CompanionView view,CompanionCommand command)
    {
        if(SelectionPreflight(view,new([command])) is {} failure)return failure;
        if(!CompanionCommands.Allowed(command,_c.Preferences))return ActionResult.Failure(ActionResultCode.Unauthorized,"动作权限已关闭。",command);
        string text;
        if(view==CompanionView.Room)
            text=command.Action=="life"?House!.StartLifeForSelection(command.Value!):
                command.Action=="go"||command.Action=="stroll"?House!.MoveNaturally(command):
                command.Action=="interact"?House!.InteractWorld(command.Value!):House!.PlayCompanion(PetAction.DanceSway);
        else if(command.Action=="stroll")text=_pet!.StartStep(command.Value=="run");
        else{_pet!.ExecuteAction(PetAction.DanceSway);text="桌面伙伴开始摇摆。";}
        return new(ActionResultCode.Started,command.Action,command.Value,text,TimeSpan.Zero);
    }
    Task<bool> ICompanionActivityWorld.Wait(CompanionView view,CompanionCommand command,CancellationToken token)=>
        view==CompanionView.Room?House?.WaitCreation(command,token)??Task.FromResult(false):_pet?.WaitCommand(command,token)??Task.FromResult(false);
    void ICompanionActivityWorld.Stop(CompanionView view){if(view==CompanionView.Room)House?.StopCompanion();else _pet?.StopCommands();}
}
internal sealed partial class MainWindow
{
    internal string ActivitySubject=>_room.ActivitySubject;
    internal int ActivityRevision=>_room.ActivityRevision;
    internal bool ActivityAvailable=>_room.ActivityAvailable;
    internal bool ActivityVisible=>_room.ActivityVisible;
    internal string ActivityMode=>_room.ActivityMode;
    internal IReadOnlyList<LifeEvent> AvailableLife()=>_room.AvailableLife();
    internal string StartLifeForSelection(string id)=>_room.StartLifeForSelection(id);
}
internal sealed partial class Room3DView
{
    internal string ActivitySubject {get;}=Guid.NewGuid().ToString("N");
    internal int ActivityRevision=>_activityGeneration;
    internal bool ActivityVisible=>!_released&&IsVisible&&_motionVisible&&!_c.AnimationSuspended;
    internal bool ActivityAvailable=>ActivityVisible&&!_editing&&!_boatOpen&&!HideActive&&!ToyRunning&&!_travel.Moving&&!_animator.Reacting;
    internal string ActivityMode=>_editing?"layout-preview":_boatOpen?"boat":HideActive?"hide":_travel.Moving||_animator.Reacting||ToyRunning?"acting":"idle";
}
