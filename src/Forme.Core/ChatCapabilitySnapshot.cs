using System.Text.Json;

namespace Forme.Core;

// Subject identities and revisions stay local. Only PublicContext is eligible for sharing.
public sealed record ChatCapabilitySnapshot(string Endpoint,string Model,SceneKind InitialScene,bool Shared,
    bool PetControl,bool SceneControl,bool PlayControl,bool LayoutControl,IReadOnlyList<ActivityObservation> Actors)
{
    public bool GoalPlans {get;init;}
    public IReadOnlyList<GoalPlayObservation> Games {get;init;}=[];
    public static bool SharingAllowed(Preferences p)=>p.ShareChatState&&AiClient.SameEndpoint(p.ChatStateEndpoint,p.Endpoint);
    public static ChatCapabilitySnapshot Capture(Preferences p,SceneKind scene,IReadOnlyList<ActivityObservation> actors)
    {
        if(scene is SceneKind.Indoor or SceneKind.Outdoor&&actors.Any(x=>x.View==CompanionView.Room&&!x.Visible)&&actors.Any(x=>x.View==CompanionView.Desktop&&x.Visible))scene=SceneKind.Desktop;
        return new(p.Endpoint,p.Model,scene,SharingAllowed(p),p.AllowPetControl,p.AllowSceneControl,p.AllowPlayControl,p.AllowLayoutPreview,
            Array.AsReadOnly(actors.Select(x=>x with{Options=Array.AsReadOnly(x.Options.ToArray()),LifeContext=""}).ToArray())){GoalPlans=p.AllowGoalPlans};
    }

    public string PublicContext()
    {
        if(!Shared)return "";
        return JsonSerializer.Serialize(new{
            defaultView=InitialScene is SceneKind.Indoor or SceneKind.Outdoor?"room":"desktop",
            permissions=new{pet=PetControl,scene=SceneControl,play=PlayControl,layout=LayoutControl},
            views=Actors.Select(x=>new{
                view=x.View==CompanionView.Room?"room":"desktop",scene=x.Scene.ToString(),model=x.Model,
                visible=x.Visible,available=x.Available,state=x.State,
                activities=x.Options.Take(12).Select(option=>option.Id)
            })
        },new JsonSerializerOptions{Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping});
    }

    public static ActionResult? Validate(ChatCapabilitySnapshot issued,ChatCapabilitySnapshot current,ActionPlan plan)
    {
        // A stop must remain available even if a sharing grant has just been withdrawn.
        if(plan.Steps.Count>0&&plan.Steps.All(x=>x.Action=="stop"))return null;
        if(!AiClient.SameEndpoint(issued.Endpoint,current.Endpoint)||issued.Model!=current.Model)
            return ActionResult.Failure(ActionResultCode.Interrupted,"AI服务或模型已改变，本次旧动作计划未执行。");
        if(issued.Shared&&!current.Shared)
            return ActionResult.Failure(ActionResultCode.Unauthorized,"聊天状态分享已关闭，本次旧动作计划未执行。");
        var grants=new Preferences{AllowPetControl=issued.PetControl,AllowSceneControl=issued.SceneControl,AllowPlayControl=issued.PlayControl,AllowLayoutPreview=issued.LayoutControl};
        if(plan.Steps.Any(x=>!CompanionCommands.Allowed(x,grants)))
            return ActionResult.Failure(ActionResultCode.Unauthorized,"该能力不在本轮请求时的权限范围内，未执行。");
        foreach(string kind in plan.Steps.Select(GoalPlay.Kind).OfType<string>().Distinct())
            if(issued.Games.SingleOrDefault(x=>x.Kind==kind)!=current.Games.SingleOrDefault(x=>x.Kind==kind))
                return ActionResult.Failure(ActionResultCode.Interrupted,"本局玩法或布置已变化，旧计划未执行。");
        foreach(var view in plan.Steps.Where(x=>ActionSurface.IsRoomAction(x)||ActionSurface.IsBodyAction(x)).Select(x=>x.View).Where(x=>x is not null).Distinct())
        {
            var before=issued.Actors.SingleOrDefault(x=>x.View==view);
            var now=current.Actors.SingleOrDefault(x=>x.View==view);
            if(before is null&&now is null)continue; // Hosts without observations retain existing execution preflight.
            if(before is null||now is null||before.SubjectId!=now.SubjectId||before.Revision!=now.Revision||
                before.Scene!=now.Scene||before.Model!=now.Model||before.Visible!=now.Visible||before.Available!=now.Available||before.State!=now.State)
                return ActionResult.Failure(ActionResultCode.Interrupted,"目标伙伴、场景或当前活动已变化，本次旧动作计划未执行，请重新发送。");
        }
        return null;
    }
}
