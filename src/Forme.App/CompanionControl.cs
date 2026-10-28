using Forme.Core;
using System.Windows;

namespace Forme.App;

internal sealed partial class DesktopHost
{
    private MainWindow? _actionHouse;
    private PetWindow? _actionPet;
    private int _actionRevision;
    private string? _actionModel;
    private CompanionView ActionView(CompanionCommand command)=>command.View??
        (ActionSurface.IsRoomAction(command)?CompanionView.Room:
        command.Action=="idle"||command.Action=="stroll"&&command.Value is "lap1" or "lap2"?CompanionView.Desktop:
        House is {IsVisible:true}?CompanionView.Room:CompanionView.Desktop);
    internal ActionResult? PreflightActions(ActionPlan plan)
    {
        if(_exit||_c.AnimationSuspended)return ActionResult.Failure(ActionResultCode.SceneUnavailable,"应用正在暂停，整组未执行。");
        if(plan.Steps.Any(x=>x.Action=="model"&&x.Value==LocalPetModels.HuTaoId)&&!LocalPetModels.HuTaoAvailable)return ActionResult.Failure(ActionResultCode.TargetUnavailable,"胡桃模型尚未导入或文件已损坏，请到设置中的伙伴模型处导入；整组未执行。");
        foreach(var command in plan.Steps.Where(ActionSurface.IsBodyAction))
        {
            if(ActionView(command)==CompanionView.Desktop&&(_pet is not {IsVisible:true}||_c.Preferences.DisplayMode=="edge"))
                return ActionResult.Failure(ActionResultCode.SceneUnavailable,"桌面伙伴已隐藏或收起，整组未执行。",command);
        }
        if(plan.Steps.Any(x=>x.Action=="stroll"&&x.Value is "lap1" or "lap2")&&plan.Steps.Count!=1)
            return ActionResult.Failure(ActionResultCode.SceneUnavailable,"屏幕环绕请单独发送，整组未执行。");
        var roomSteps=plan.Steps.Where(x=>ActionSurface.IsRoomAction(x)||ActionSurface.IsBodyAction(x)&&ActionView(x)==CompanionView.Room).ToArray();
        if(roomSteps.Length==0)return null;
        if(!_c.Preferences.ShowHome3D)return ActionResult.Failure(ActionResultCode.SceneUnavailable,"小屋场景已隐藏，整组未执行。");
        bool opensRoom=roomSteps.Any(ActionSurface.IsRoomAction);
        if(!opensRoom&&House is not {IsVisible:true})return ActionResult.Failure(ActionResultCode.SceneUnavailable,"小屋伙伴未显示，整组未执行。");
        if(House is {WindowState:WindowState.Minimized}&&!roomSteps.Any(x=>x.Action=="scene"))return ActionResult.Failure(ActionResultCode.SceneUnavailable,"小屋已最小化，整组未执行。");
        var roomPlan=new ActionPlan(roomSteps);
        return House?.PreflightActions(roomPlan)??TargetNavigation.Preflight(roomPlan,_c.Store.World(),SceneKind.Indoor,TargetNavigation.Start);
    }
    internal ActionResult ExecuteAction(CompanionCommand command)
    {
        // The legacy string is presentation only. Failure branches throw a typed error.
        string message=ExecuteCompanionCommand(command);
        _actionRevision=_actionHouse?.ActivityRevision??_actionPet?.ActivityRevision??-1;
        _actionModel=_c.Preferences.PetModel;
        bool waits=command.Action is "go" or "stroll" or "move" or "dance" or "pat" or "rub" or "interact" or "life" or "throw" or "toy" or "recall";
        return new(waits?ActionResultCode.Started:ActionResultCode.Completed,command.Action,command.Value,message,TimeSpan.Zero);
    }
    private Task<bool> WaitAction(CompanionCommand command,CancellationToken token)
    {
        if(_actionModel!=_c.Preferences.PetModel)return Task.FromResult(false);
        if(ActionView(command)==CompanionView.Room)
            return _actionHouse is {} room&&ReferenceEquals(room,House)&&room.ActivityRevision==_actionRevision?room.WaitCreation(command,token):Task.FromResult(false);
        return _actionPet is {} pet&&ReferenceEquals(pet,_pet)&&pet.ActivityRevision==_actionRevision?pet.WaitCommand(command,token):Task.FromResult(false);
    }
    internal string ExecuteCompanionCommand(CompanionCommand command)
    {
        if(!CompanionCommands.Allowed(command,_c.Preferences))throw new ActionFailureException(ActionResultCode.Unauthorized,"没有执行：动作权限未开启。");
        if(_exit||_c.AnimationSuspended)throw new ActionFailureException(ActionResultCode.SceneUnavailable,"应用正在暂停，动作未执行。");
        bool roomAction=ActionView(command)==CompanionView.Room;
        _actionHouse=roomAction?House:null;_actionPet=roomAction?null:_pet;
        switch(command.Action)
        {
            case "scene":ShowHouse("chat");House!.SwitchWorld(command.Value=="outdoor");return command.Value=="outdoor"?"已切换户外":"已切换小屋";
            case "weather":_c.SavePreferences(_c.Preferences with{Weather=command.Value!});return "已改变天气布景";
            case "light":_c.SavePreferences(_c.Preferences with{RoomLamp=command.Value=="on"});return command.Value=="on"?"灯已打开":"灯已关闭";
            case "fireplace":_c.SavePreferences(_c.Preferences with{Fireplace=command.Value=="on"});return command.Value=="on"?"壁炉已点亮":"壁炉已熄灭";
            case "interact":if(House is null)ShowHouse("chat");_actionHouse=House;return House!.InteractWorld(command.Value!);
            case "stroll":if(roomAction){if(House is null)throw new ActionFailureException(ActionResultCode.SceneUnavailable,"小屋伙伴未显示，动作未执行。");return House.MoveNaturally(command);}if(_pet is not {IsVisible:true})throw new OperationFailureException("桌面伙伴已隐藏");return command.Value is "lap1" or "lap2"?_pet.StartScreenLaps(command.Value=="lap1"?1:2):_pet.StartStep(command.Value=="run");
            case "go":if(House is null)ShowHouse("chat");_actionHouse=House;return House!.MoveNaturally(command);
            case "model":LocalPetModels.RequireAvailable(command.Value);_c.SavePreferences(_c.Preferences with{PetModel=command.Value!});return "已切换伙伴模型";
            case "stop":House?.StopCompanion();_pet?.SetAiIdleMode("idle");return "已停止当前活动和剩余动作";
            case "recall":if(!roomAction){_pet?.SetAiIdleMode("idle");return "桌面伙伴已停下，留在你身边";}if(House is null)throw new ActionFailureException(ActionResultCode.SceneUnavailable,"小屋伙伴未显示。");House.RecallCompanion();return "已叫回小屋伙伴";
            case "life":case "hide":case "throw":case "toy":case "layout":case "furniture":
            case "boat":case "boat-shape":case "boat-color":case "boat-wind":case "boat-leaf":case "boat-name":
                if(House is null)ShowHouse("chat");_actionHouse=House;return House!.ExecuteCreation(command);
            case "move":if(House is null)throw new OperationFailureException("请先打开小屋，再移动到场景坐标");return House.MoveCompanion(new(command.X!.Value,command.Z!.Value));
            case "idle":if(_pet is not null)_pet.SetAiIdleMode(command.Value!);else _c.SavePreferences(_c.Preferences with{PetIdleMode=command.Value!});return "已改变桌面伙伴待机模式";
            default:
                var action=command.Action=="pat"?PetAction.Pat:command.Action=="rub"?PetAction.Rub:command.Value=="hop"?PetAction.DanceHop:command.Value=="spin"?PetAction.DanceSpin:PetAction.DanceSway;
                if(roomAction){if(House is null)throw new ActionFailureException(ActionResultCode.SceneUnavailable,"小屋伙伴未显示。");return House.PlayCompanion(action);}
                if(_pet is not {IsVisible:true})throw new ActionFailureException(ActionResultCode.SceneUnavailable,"伙伴已隐藏，动作未执行。");
                if(_c.Preferences.DisplayMode=="edge")throw new ActionFailureException(ActionResultCode.SceneUnavailable,"伙伴已收起到边缘，动作未执行。");
                _pet.ExecuteAction(action);return _c.Preferences.ReducedMotion?"减少动效模式：已显示静态回应":"已发起伙伴动作";
        }
    }
}

internal sealed partial class MainWindow
{
    internal SceneKind CompanionScene=>_room.Outdoors?SceneKind.Outdoor:SceneKind.Indoor;
    internal ActionResult? PreflightActions(ActionPlan plan)=>_room.PreflightActions(plan);
    public string SceneDescription=>_room.CreationContext();
    public void StopCompanion()=>_room.StopWorld();
    public void RecallCompanion(){if(WindowState==WindowState.Minimized)throw new OperationFailureException("窗口已最小化，叫回未执行");RevealWorld();_room.RecallPet();}
    public void SwitchWorld(bool outdoor){RevealWorld();_room.SwitchScene(outdoor);}
    private void RevealWorld(){if(ActualWidth<940){_compactScene=true;ApplyLayout();UpdateLayout();}}
    public string InteractWorld(string id){RevealWorld();return _room.Interact(id);}
    public string MoveCompanion(GroundPoint point)
    {RevealWorld();if(WindowState==WindowState.Minimized)throw new OperationFailureException("窗口已最小化，移动未执行");if(!_room.TryMove(point))throw new OperationFailureException("目标超出场景或被家具遮挡，移动未执行");return "伙伴正在向目标移动";}
    internal string MoveNaturally(CompanionCommand command)
    {if(WindowState==WindowState.Minimized)throw new OperationFailureException("窗口已最小化，移动未执行");RevealWorld();return _room.MoveNaturally(command);}
    public string PlayCompanion(PetAction action)
    {RevealWorld();if(WindowState==WindowState.Minimized)throw new ActionFailureException(ActionResultCode.SceneUnavailable,"窗口已最小化，动作未执行。");_room.PlayAction(action);return _c.Preferences.ReducedMotion?"减少动效模式：已显示静态回应":"已发起伙伴动作";}
}
