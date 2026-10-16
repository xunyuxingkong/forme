namespace Forme.Core;

// Semantic choice comes from the model; this binds the execution target once per plan.
public static class ActionSurface
{
    public static bool IsRoomAction(CompanionCommand command)=>command.Action is
        "scene" or "go" or "interact" or "move" or "life" or "hide" or "throw" or "toy" or "layout" or "furniture"
        ||command.Action.StartsWith("boat",StringComparison.Ordinal);
    public static bool IsBodyAction(CompanionCommand command)=>command.Action is
        "stroll" or "dance" or "pat" or "rub" or "recall";
    public static ActionPlan Bind(ActionPlan plan,SceneKind scene)
    {
        var view=scene is SceneKind.Indoor or SceneKind.Outdoor?CompanionView.Room:CompanionView.Desktop;
        var steps=new List<CompanionCommand>();
        foreach(var command in plan.Steps)
        {
            if(command.View is {} explicitView&&!Enum.IsDefined(explicitView))
                throw new ActionFailureException(ActionResultCode.NotRecognized,"动作主体无效，没有执行。");
            if(IsRoomAction(command))view=CompanionView.Room;
            var target=command.View??(command.Action=="idle"||command.Action=="stroll"&&command.Value is "lap1" or "lap2"?CompanionView.Desktop:view);
            if(IsRoomAction(command)&&target!=CompanionView.Room||command.Action=="stroll"&&command.Value is "lap1" or "lap2"&&target!=CompanionView.Desktop)
                throw new ActionFailureException(ActionResultCode.TargetUnavailable,"动作能力与所选主体不一致，没有执行。");
            steps.Add(IsRoomAction(command)||IsBodyAction(command)||command.Action=="idle"?command with{View=target}:command);
        }
        return new(steps);
    }
}
