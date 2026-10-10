namespace Forme.Core;

public static class ActionPlanner
{
    public static CompanionIntent FromCommand(CompanionCommand command) => command.Action switch
    {
        "go" => new(CompanionIntentType.GoTo, command.Value, command.Motion, Source:"rule"),
        "interact" => new(CompanionIntentType.Interact, command.Value, Source:"rule"),
        _ => new(CompanionIntentType.Shortcut, Source:"rule", Shortcut:command)
    };
    public static ActionPlan Build(IReadOnlyList<CompanionIntent> intents, SceneKind scene)
    {
        if(intents.Count is <1 or >3) throw new ActionFailureException(ActionResultCode.NotRecognized,"没有识别到完整动作，或动作超过三步。");
        var steps = new List<CompanionCommand>();
        foreach(var intent in intents)
        {
            if(!double.IsFinite(intent.Confidence) || intent.Confidence < .85 || intent.Confidence > 1)
                throw new ActionFailureException(ActionResultCode.NotRecognized,"动作含义不够明确，请换一种说法。");
            CompanionCommand command = intent.Type switch
            {
                CompanionIntentType.GoTo => new("go",intent.Target,Motion:intent.Mode),
                CompanionIntentType.Interact => new("interact",intent.Target),
                CompanionIntentType.Stroll => new("stroll", intent.Count is {} count ? count switch { 1 => "lap1", 2 => "lap2", _ => "invalid" } : intent.Mode ?? "walk"),
                CompanionIntentType.Dance => new("dance",intent.Mode ?? "sway"),
                CompanionIntentType.Idle => new("idle",intent.Mode),
                CompanionIntentType.Recall => new("recall"), CompanionIntentType.Stop => new("stop"),
                CompanionIntentType.ChangeScene => new("scene",intent.Target),
                CompanionIntentType.ChangeWeather => new("weather",intent.Mode),
                CompanionIntentType.ChangeModel => new("model",intent.Target),
                CompanionIntentType.ToggleDevice when intent.Target is "light" or "fireplace" => new(intent.Target,intent.Mode),
                CompanionIntentType.StartPlay when intent.Target == "boat" => new("boat",intent.Mode ?? "open"),
                CompanionIntentType.StartPlay when intent.Target == "hide" => new("hide",intent.Mode),
                CompanionIntentType.UseToy => new("toy",intent.Target??"ball-yellow"),
                CompanionIntentType.Touch when intent.Mode is "pat" or "rub" => new(intent.Mode),
                CompanionIntentType.Life => new("life",intent.Target),
                CompanionIntentType.LayoutPreview when intent.Mode is "reading" or "undo" => new("layout",intent.Mode),
                CompanionIntentType.LayoutPreview when intent.Mode == "furniture" => new("furniture",intent.Target,intent.X,intent.Z,intent.Rotation),
                CompanionIntentType.BoatConfiguration when intent.Target is "shape" or "color" or "wind" => new("boat-"+intent.Target,intent.Mode),
                CompanionIntentType.BoatConfiguration when intent.Target is "1" or "2" or "3" => new("boat-leaf",intent.Target,intent.X,intent.Z,intent.Rotation),
                CompanionIntentType.NameBoat => new("boat-name",intent.Target),
                CompanionIntentType.Shortcut when intent.Source == "rule" && intent.Shortcut is {} shortcut => shortcut,
                _ => throw new ActionFailureException(ActionResultCode.NotRecognized,"此动作尚不支持。")
            };
            if(command.Action is "go" or "interact")
            {
                var target = TargetCatalog.Find(command.Value) ?? throw new ActionFailureException(ActionResultCode.TargetUnavailable,"没有找到这个目标。");
                var capability = command.Action == "go" ? TargetCapabilities.GoTo : TargetCapabilities.Interact;
                if(!target.Capabilities.HasFlag(capability)) throw new ActionFailureException(ActionResultCode.TargetUnavailable,"目标不支持此操作。");
                if(target.Scene is {} required && required != scene)
                {
                    steps.Add(new("scene",required == SceneKind.Outdoor ? "outdoor" : "indoor")); scene = required;
                }
            }
            steps.Add(command);
            if(command.Action == "scene") scene = command.Value == "outdoor" ? SceneKind.Outdoor : SceneKind.Indoor;
            if(steps.Count > 3) throw new ActionFailureException(ActionResultCode.NotRecognized,"包含切换场景后超过三步，请分开请求。");
        }
        return new(steps);
    }
    public static ActionResult? Preflight(ActionPlan plan, Preferences preferences, Func<ActionPlan,ActionResult?>? availability = null)
    {
        if(plan.Steps.Count is <1 or >3) return ActionResult.Failure(ActionResultCode.NotRecognized,"动作计划必须为一至三步。");
        if(plan.Steps.Any(x=>x.Action is "layout" or "furniture")&&plan.Steps.Any(x=>x.Action is not ("layout" or "furniture")))return ActionResult.Failure(ActionResultCode.NotRecognized,"家具预览请单独请求，整组未执行。");
        if(plan.Steps.Any(x=>x.Action=="hide")&&plan.Steps.Count>1)return ActionResult.Failure(ActionResultCode.NotRecognized,"藏物玩法请单独请求，整组未执行。");
        if(plan.Steps.Any(x=>x.Action.StartsWith("boat",StringComparison.Ordinal))&&plan.Steps.Any(x=>!x.Action.StartsWith("boat",StringComparison.Ordinal)))return ActionResult.Failure(ActionResultCode.NotRecognized,"纸船玩法请单独请求，整组未执行。");
        foreach(var command in plan.Steps)
        {
            var grants = new Preferences { AllowPetControl=true, AllowSceneControl=true, AllowPlayControl=true, AllowLayoutPreview=true };
            if(!CompanionCommands.Allowed(command,grants)) return ActionResult.Failure(ActionResultCode.NotRecognized,"动作参数无效，整组未执行。",command);
            if(!CompanionCommands.Allowed(command,preferences)) return ActionResult.Failure(ActionResultCode.Unauthorized,"没有执行：请先在设置开启对应动作权限。",command);
        }
        return availability?.Invoke(plan);
    }
}
