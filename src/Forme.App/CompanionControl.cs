using Forme.Core;
using System.Windows;

namespace Forme.App;

internal sealed partial class DesktopHost
{
    internal string ExecuteCompanionCommand(CompanionCommand command)
    {
        if(!CompanionCommands.Allowed(command,_c.Preferences))return "动作未获授权";
        if(_exit||_c.AnimationSuspended)return "应用正在暂停，动作未执行";
        switch(command.Action)
        {
            case "scene":ShowHouse("chat");House!.SwitchWorld(command.Value=="outdoor");return command.Value=="outdoor"?"已切换户外":"已切换小屋";
            case "weather":_c.SavePreferences(_c.Preferences with{Weather=command.Value!});return "已改变天气布景";
            case "light":_c.SavePreferences(_c.Preferences with{RoomLamp=command.Value=="on"});return command.Value=="on"?"灯已打开":"灯已关闭";
            case "fireplace":_c.SavePreferences(_c.Preferences with{Fireplace=command.Value=="on"});return command.Value=="on"?"壁炉已点亮":"壁炉已熄灭";
            case "interact":if(House is null)ShowHouse("chat");return House!.InteractWorld(command.Value!);
            case "stroll":if(House is not null){if(command.Value is "lap1" or "lap2")throw new OperationFailureException("屏幕环绕只支持桌面悬浮伙伴；请先返回桌面伙伴窗口");return House.MoveNaturally(command);}if(_pet is not {IsVisible:true})throw new OperationFailureException("伙伴已隐藏");return command.Value is "lap1" or "lap2"?_pet.StartScreenLaps(command.Value=="lap1"?1:2):_pet.StartStep(command.Value=="run");
            case "go":if(House is null)ShowHouse("chat");return House!.MoveNaturally(command);
            case "model":_c.SavePreferences(_c.Preferences with{PetModel=command.Value!});return "已切换伙伴模型";
            case "stop":House?.StopCompanion();_pet?.StopCommands();if(House is null)_pet?.SetAiIdleMode("idle");return "已停止当前活动和剩余动作";
            case "recall":if(House is null){_pet?.SetAiIdleMode("idle");return "伙伴已停下，留在你身边";}House.RecallCompanion();return "已叫回伙伴";
            case "life":case "hide":case "throw":case "layout":case "furniture":
            case "boat":case "boat-shape":case "boat-color":case "boat-wind":case "boat-leaf":case "boat-name":
                if(House is null)ShowHouse("chat");return House!.ExecuteCreation(command);
            case "move":if(House is null)throw new OperationFailureException("请先打开小屋，再移动到场景坐标");return House.MoveCompanion(new(command.X!.Value,command.Z!.Value));
            case "idle":if(House is null&&_pet is not null)_pet.SetAiIdleMode(command.Value!);else _c.SavePreferences(_c.Preferences with{PetIdleMode=command.Value!});return House is not null&&command.Value is "walk" or "run"?"已保存待机模式，桌面显示时生效":"已改变伙伴待机模式";
            default:
                var action=command.Action=="pat"?PetAction.Pat:command.Action=="rub"?PetAction.Rub:command.Value=="hop"?PetAction.DanceHop:command.Value=="spin"?PetAction.DanceSpin:PetAction.DanceSway;
                if(House is not null)return House.PlayCompanion(action);
                if(_pet is not {IsVisible:true})return "伙伴已隐藏，动作未执行";
                if(_c.Preferences.DisplayMode=="edge")return "伙伴已收起到边缘，动作未执行";
                _pet.ExecuteAction(action);return _c.Preferences.ReducedMotion?"减少动效模式：已显示静态回应":"已发起伙伴动作";
        }
    }
}

internal sealed partial class MainWindow
{
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
    {RevealWorld();if(WindowState==WindowState.Minimized)return "窗口已最小化，动作未执行";_room.PlayAction(action);return _c.Preferences.ReducedMotion?"减少动效模式：已显示静态回应":"已发起伙伴动作";}
}
