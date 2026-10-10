namespace Forme.Core;

public static class TargetNavigation
{
    public static GroundPoint Start { get; } = new(.15,1.25);
    public static PetTravel Travel(SceneKind scene,LivingWorld world)
    {
        if(scene==SceneKind.Indoor) return world.Travel();
        return new(-10,10,-10,10,[new(-5,-1,2.2,1.2),new(5,-1,2.2,1.2),new(-4,5,2,2),new(-7,-7,1,1),new(7,-7,1,1),new(-8,6,1,1),new(8,6,1,1),new(-3.6,-5,4.3,2.5),new(5.45,-5.7,2,.8),new(4.44,4.23,.6,.45)]);
    }
    // Simulates scene and position changes without opening windows or touching user data.
    public static ActionResult? Preflight(ActionPlan plan,LivingWorld world,SceneKind current,GroundPoint position)
    {
        SceneKind scene=current;var travel=Travel(scene==SceneKind.Outdoor?SceneKind.Outdoor:SceneKind.Indoor,world);travel.Reset(position);
        foreach(var command in plan.Steps)
        {
            if(command.Action=="scene") {scene=command.Value=="outdoor"?SceneKind.Outdoor:SceneKind.Indoor;travel=Travel(scene,world);travel.Reset(Start);continue;}
            if(command.Action is not ("go" or "interact")) continue;
            var target=TargetCatalog.Find(command.Value);
            if(target is null) return ActionResult.Failure(ActionResultCode.TargetUnavailable,"没有找到这个目标。",command);
            if(target.FurnitureKind is {} kind && !world.Items.Any(x=>x.Kind==kind)) return ActionResult.Failure(ActionResultCode.TargetUnavailable,"目标家具已收纳，整组未执行。",command);
            if(target.Id=="ball") continue;
            if(TargetCatalog.Resolve(target,world,travel,scene) is not {} point) return ActionResult.Failure(ActionResultCode.PathBlocked,"目标附近没有可达位置，整组未执行。",command);
            travel.Reset(point);
        }
        return null;
    }
}
