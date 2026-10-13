using Forme.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Forme.Tests;

[TestClass]
public sealed class ActivitySelectionTests
{
    private static Preferences Granted()=>new(){AllowPetControl=true,AllowSceneControl=true,ShareGameState=true,GameStateEndpoint="https://api.deepseek.com"};
    private static ActivityObservation Observe(string model="sprout")=>new(CompanionView.Room,"local-instance",1,SceneKind.Indoor,model,"rain",true,true,"idle",
        ActivitySelection.Options(CompanionView.Room,SceneKind.Indoor,model,Granted(),_=>null));
    [TestMethod]
    public void SelectionOnlyExposesGrantedCurrentShortActivities()
    {
        var p=Granted();var room=Observe();Assert.IsTrue(room.Options.Any(x=>x.Id=="use-book"));
        var desktop=ActivitySelection.Options(CompanionView.Desktop,SceneKind.Desktop,"sprout",p,_=>null);
        Assert.IsTrue(desktop.All(x=>x.Steps.All(s=>s.Action is "stroll" or "dance")));
        Assert.IsFalse(Observe("space-marine").Options.Any(x=>x.Id.StartsWith("use-")||x.Id=="sway"));
        Assert.AreEqual(0,ActivitySelection.Options(CompanionView.Room,SceneKind.Indoor,"sprout",new(),_=>null).Count);
        Assert.IsFalse(ActivitySelection.Options(CompanionView.Room,SceneKind.Indoor,"sprout",p,
            plan=>plan.Steps.Any(s=>s.Value=="book")?ActionResult.Failure(ActionResultCode.PathBlocked,"blocked"):null).Any(x=>x.Id.Contains("book")));
        Assert.IsTrue(room.Options.All(x=>x.Steps.All(s=>s.Action is "go" or "interact" or "stroll" or "dance")));
    }
    [TestMethod]
    public void RequestsAreConsentBoundMinimalAndWithinBudget()
    {
        var p=Granted();var turns=ActivitySelection.BuildTurns(p,"你自己选一个活动",Observe());
        Assert.AreEqual(3,turns.Count);Assert.IsTrue(turns.Sum(AiClient.Estimate)<=p.ContextBudget);
        string text=string.Join("",turns.Select(x=>x.Content));
        Assert.IsFalse(text.Contains("local-instance"));Assert.IsFalse(text.Contains("Steps"));Assert.IsFalse(text.Contains("RoleDescription"));
        Assert.Throws<InvalidDataException>(()=>ActivitySelection.BuildTurns(p with{ShareGameState=false},"玩一会儿",Observe()));
        Assert.Throws<InvalidDataException>(()=>ActivitySelection.BuildTurns(p with{Endpoint="https://other.invalid"},"玩一会儿",Observe()));
    }
    [TestMethod]
    public void StrictChoicesRejectUnknownCommandsDuplicateFieldsAndPartialOutput()
    {
        var observation=Observe();
        Assert.AreEqual("walk",ActivitySelection.Parse("{\"id\":\"walk\",\"reason\":\"先走几步\"}",observation).Id);
        Assert.IsNull(ActivitySelection.Parse("{\"id\":null,\"reason\":\"你想在小屋还是桌面玩？\"}",observation).Id);
        foreach(string invalid in new[]{"{\"id\":\"unknown\",\"reason\":\"试试\"}","{\"id\":\"walk\",\"reason\":\"试试\",\"commands\":[]}","{\"id\":\"walk\",\"id\":\"run\",\"reason\":\"试试\"}","{\"id\":\"walk\"}","[]","{\"id\":\"walk\",\"reason\":\"试试\"} extra"})
            Assert.Throws<InvalidDataException>(()=>ActivitySelection.Parse(invalid,observation));
    }
    [TestMethod]
    public void RevisionInstanceModelVisibilityAndCapabilityChangesInvalidateSelection()
    {
        var before=Observe();var choice=new ActivityChoice("walk","走几步");
        Assert.AreEqual("walk",ActivitySelection.Validate(before,before,choice).Id);
        foreach(var changed in new[]{before with{Revision=2},before with{SubjectId="replacement"},before with{Model="space-marine"},before with{Available=false},before with{View=CompanionView.Desktop},before with{Scene=SceneKind.Outdoor},before with{Options=[]}})
            Assert.Throws<ActionFailureException>(()=>ActivitySelection.Validate(before,changed,choice));
        // Light changes alone are not strong invalidations; still-valid activities are rechecked.
        Assert.AreEqual("walk",ActivitySelection.Validate(before,before with{LampOn=false},choice).Id);
    }
    [TestMethod]
    public void ChangedStepsCannotReuseOldOpaqueId()
    {
        var before=Observe();
        var changed=before with{Options=[new("walk","changed","changed",[new("scene","outdoor")])]};
        Assert.Throws<ActionFailureException>(()=>ActivitySelection.Validate(before,changed,new("walk","走几步")));
    }
}
