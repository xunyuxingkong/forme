namespace Forme.Core;

public enum CompanionIntentType { GoTo, Interact, Stroll, Dance, Idle, Recall, Stop, ChangeScene, ChangeWeather, UseToy, StartPlay, ChangeModel, ToggleDevice, Touch, Life, LayoutPreview, BoatConfiguration, NameBoat, Shortcut }
public sealed record CompanionIntent(CompanionIntentType Type, string? Target = null, string? Mode = null, int? Count = null, double Confidence = 1, string Source = "local", CompanionCommand? Shortcut = null, double? X = null, double? Z = null, int? Rotation = null);
public enum ActionResultCode { Started, Completed, NotRecognized, Unauthorized, TargetUnavailable, SceneUnavailable, PathBlocked, Interrupted, Cancelled, Timeout, Failed }
public sealed record ActionResult(ActionResultCode Code, string Action, string? Target, string UserMessage, TimeSpan Elapsed)
{
    public bool Success => Code is ActionResultCode.Started or ActionResultCode.Completed;
    public static ActionResult Failure(ActionResultCode code, string message, CompanionCommand? command = null) => new(code, command?.Action ?? "plan", command?.Value, message, TimeSpan.Zero);
}
public sealed class ActionFailureException(ActionResultCode code, string message) : Exception(message)
{
    public ActionResultCode Code { get; } = code;
}
public sealed record ActionPlan(IReadOnlyList<CompanionCommand> Steps);
public enum SceneKind { Indoor, Outdoor, Desktop, Tray }
[Flags] public enum TargetCapabilities { GoTo = 1, Interact = 2 }
public sealed record CompanionTarget(string Id, string Name, SceneKind? Scene, IReadOnlyList<string> Aliases, TargetCapabilities Capabilities, IReadOnlyList<GroundPoint> ApproachPoints, string? FurnitureKind = null);

public static class TargetCatalog
{
    public static IReadOnlyList<CompanionTarget> All { get; } = [
        new("pond", "池塘边", SceneKind.Outdoor, ["池塘", "水池", "水边", "木桥", "池子", "水面"], TargetCapabilities.GoTo | TargetCapabilities.Interact, [new(-3.7,-2.8)]),
        new("picnic", "野餐毯", SceneKind.Outdoor, ["野餐毯", "野餐区", "野餐"], TargetCapabilities.GoTo | TargetCapabilities.Interact, [new(3,4.5)]),
        new("rest", "凉亭", SceneKind.Outdoor, ["凉亭", "亭子", "休息亭"], TargetCapabilities.GoTo | TargetCapabilities.Interact, [new(5.5,-3)]),
        new("bell", "风铃", SceneKind.Outdoor, ["风铃"], TargetCapabilities.GoTo | TargetCapabilities.Interact, [new(.65,-6.5)]),
        new("book", "书架旁", SceneKind.Indoor, ["书架", "书柜", "看书的地方"], TargetCapabilities.GoTo | TargetCapabilities.Interact, [], "book"),
        new("fish", "鱼缸旁", SceneKind.Indoor, ["鱼缸", "小鱼", "看鱼的地方"], TargetCapabilities.GoTo | TargetCapabilities.Interact, [], "fish"),
        new("feed", "零食碗", SceneKind.Indoor, ["零食碗", "饭碗", "零食"], TargetCapabilities.GoTo | TargetCapabilities.Interact, [], "feed"),
        new("sleep", "小窝旁", SceneKind.Indoor, ["小窝", "睡垫", "床边"], TargetCapabilities.GoTo | TargetCapabilities.Interact, [], "sleep"),
        new("window", "窗边", SceneKind.Indoor, ["窗边", "窗户", "窗台"], TargetCapabilities.GoTo, [new(0,-1.4),new(.6,-1.4),new(-.6,-1.4)]),
        new("door", "门边", SceneKind.Indoor, ["门边", "门口"], TargetCapabilities.GoTo, [new(0,2.5),new(-.6,2.5),new(.6,2.5)]),
        new("water", "植物", null, ["植物", "花盆"], TargetCapabilities.Interact, [new(1.3,3.5),new(-2.4,5)]),
        new("ball", "小球", null, ["小球"], TargetCapabilities.Interact, [])
    ];
    public static CompanionTarget? Find(string? id) => All.FirstOrDefault(x => x.Id == id);
    public static bool Supports(string? id, TargetCapabilities capability) => Find(id)?.Capabilities.HasFlag(capability) == true;
    public static GroundPoint? Resolve(CompanionTarget target, LivingWorld world, PetTravel travel, SceneKind scene)
    {
        if(target.Scene is {} required && required != scene) return null;
        if(target.FurnitureKind is {} kind)
        {
            foreach(var item in world.Items.Where(x => x.Kind == kind).OrderBy(x => Math.Abs(x.X-travel.Position.X)+Math.Abs(x.Z-travel.Position.Z)))
                if(LivingWorld.Approach(item,travel) is {} point) return point;
            return null;
        }
        var points = target.Id == "water" ? new[] { target.ApproachPoints[scene == SceneKind.Outdoor ? 1 : 0] } : target.ApproachPoints;
        return points.Cast<GroundPoint?>().FirstOrDefault(x => x is {} p && travel.CanReach(p));
    }
}
