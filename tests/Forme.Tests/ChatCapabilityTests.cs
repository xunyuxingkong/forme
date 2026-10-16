using Forme.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Forme.Tests;

[TestClass]
public sealed class ChatCapabilityTests
{
    private static Preferences Granted()=>new(){AllowPetControl=true,AllowSceneControl=true,AllowPlayControl=true,AllowLayoutPreview=true,
        ShareChatState=true,ChatStateEndpoint="https://api.deepseek.com"};
    private static ActivityObservation Actor(CompanionView view)=>
        new ActivityObservation(view,"private-subject-"+view,7,view==CompanionView.Room?SceneKind.Indoor:SceneKind.Desktop,"sprout","rain",true,true,"idle",
            ActivitySelection.Options(view,view==CompanionView.Room?SceneKind.Indoor:SceneKind.Desktop,"sprout",Granted(),_=>null),"private-experience"){Visible=true};
    private static ChatCapabilitySnapshot Snapshot(Preferences? p=null)=>ChatCapabilitySnapshot.Capture(p??Granted(),SceneKind.Indoor,[Actor(CompanionView.Room),Actor(CompanionView.Desktop)]);
    private static ActionPlan Walk(CompanionView view)=>ActionPlanner.Build([new(CompanionIntentType.Stroll,Mode:"walk",View:view)],SceneKind.Indoor);

    [TestMethod]
    public void ChatStateIsSeparateOptInAndEndpointBound()
    {
        var p=Granted() with{ShareChatState=false,ShareGameState=true,GameStateEndpoint="https://api.deepseek.com"};
        Assert.IsFalse(Snapshot(p).Shared);Assert.AreEqual("",Snapshot(p).PublicContext());
        Assert.IsFalse(Snapshot(p with{ShareChatState=true,Endpoint="https://other.invalid"}).Shared);
        Assert.IsFalse(AiIntentProtocol.RoutingInstructions(Snapshot(p)).Contains("本轮已授权的伙伴快照"));
        Assert.IsTrue(Snapshot().Shared);
    }
    [TestMethod]
    public void SharedContextExcludesLocalIdentityExperiencesAndPrivateWorldData()
    {
        var snapshot=Snapshot();string json=snapshot.PublicContext();
        Assert.IsTrue(json.Contains("visit-book"));Assert.IsTrue(json.Contains("visible"));Assert.IsTrue(json.Contains("permissions"));
        foreach(string privateValue in new[]{"private-subject","private-experience","Revision","Weather","LampOn","Steps","ApproachPoints","Endpoint"})
            Assert.IsFalse(json.Contains(privateValue),privateValue);
        Assert.IsFalse(snapshot.Actors.Any(x=>x.LifeContext.Length>0));
    }
    [TestMethod]
    public void SharedRoutingFitsDefaultBudgetWithBothViewsAndAllGrants()
    {
        var p=Granted();var turns=AiClient.BuildContext(p with{ContextBudget=8192},[],"在小屋里自己选个适合的短活动",out _,additionalSystemInstruction:AiIntentProtocol.RoutingInstructions(Snapshot(p)));
        Assert.IsTrue(turns.Sum(AiClient.Estimate)<=p.ContextBudget,"实际预算字节："+turns.Sum(AiClient.Estimate));
        Assert.IsTrue(turns[0].Content.Contains("本轮已授权的伙伴快照"));
        Assert.IsFalse(turns[0].Content.Contains("private-subject"));
        AiClient.BuildContext(p,[],"在小屋里自己选个适合的短活动",out _,additionalSystemInstruction:AiIntentProtocol.RoutingInstructions(Snapshot(p)));
    }
    [TestMethod]
    public void StaleSubjectRevisionVisibilityModelOrSceneRejectsOnlyTargetView()
    {
        var before=Snapshot();var room=before.Actors[0];var desktop=before.Actors[1];
        foreach(var changed in new[]{room with{SubjectId="replacement"},room with{Revision=8},room with{Visible=false},room with{Model="space-marine"},room with{Scene=SceneKind.Outdoor},room with{Available=false},room with{State="hide"}})
        {
            var current=before with{Actors=[changed,desktop]};
            Assert.AreEqual(ActionResultCode.Interrupted,ChatCapabilitySnapshot.Validate(before,current,Walk(CompanionView.Room))?.Code);
            Assert.IsNull(ChatCapabilitySnapshot.Validate(before,current,Walk(CompanionView.Desktop)));
        }
        Assert.IsNull(ChatCapabilitySnapshot.Validate(before,before with{Actors=[room with{LampOn=false},desktop]},Walk(CompanionView.Room)));
    }
    [TestMethod]
    public void RevocationServiceChangeAndNewlyGrantedPermissionsCannotAuthorizeOldPlan()
    {
        var before=Snapshot();
        Assert.AreEqual(ActionResultCode.Unauthorized,ChatCapabilitySnapshot.Validate(before,before with{Shared=false},Walk(CompanionView.Room))?.Code);
        Assert.AreEqual(ActionResultCode.Interrupted,ChatCapabilitySnapshot.Validate(before,before with{Model="other-model"},Walk(CompanionView.Room))?.Code);
        Assert.AreEqual(ActionResultCode.Interrupted,ChatCapabilitySnapshot.Validate(before,before with{Endpoint="https://other.invalid"},new([]))?.Code);
        Assert.AreEqual(ActionResultCode.Unauthorized,ChatCapabilitySnapshot.Validate(before with{PetControl=false},before,Walk(CompanionView.Room))?.Code);
        Assert.IsNull(ChatCapabilitySnapshot.Validate(before,before with{Shared=false},new([new("stop")])));
    }
    [TestMethod]
    public void HiddenRoomDefaultsToVisibleDesktopAndKeepsExplicitRoomBinding()
    {
        var snapshot=ChatCapabilitySnapshot.Capture(Granted(),SceneKind.Indoor,[Actor(CompanionView.Room) with{Visible=false},Actor(CompanionView.Desktop)]);
        Assert.AreEqual(SceneKind.Desktop,snapshot.InitialScene);
        Assert.AreEqual(CompanionView.Desktop,ActionPlanner.Build([new(CompanionIntentType.Stroll,Mode:"walk")],snapshot.InitialScene).Steps.Single().View);
        Assert.AreEqual(CompanionView.Room,ActionPlanner.Build([new(CompanionIntentType.Stroll,Mode:"walk",View:CompanionView.Room)],snapshot.InitialScene).Steps.Single().View);
    }
    [TestMethod]
    public void CatalogHasParameterContractsAndKeepsUserPromotionOutOfModelDirectory()
    {
        Assert.IsTrue(CapabilityCatalog.ModelSignatures(CapabilityDomain.Companion).Contains("Stroll(mode=walk/run,count=1/2)"));
        Assert.IsFalse(CapabilityCatalog.AllowsModel(CapabilityDomain.Companion,"Shortcut"));
        Assert.IsFalse(CapabilityCatalog.AllowsModel(CapabilityDomain.Project,"task.promote"));
        Assert.IsTrue(CapabilityCatalog.RequiresUserConfirmation(CapabilityDomain.Project,"task.promote"));
        Assert.IsFalse(CapabilityCatalog.ModelSignatures(CapabilityDomain.Companion,Snapshot(Granted() with{AllowPetControl=false})).Contains("Stroll("));
    }
}
