using Forme.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Forme.Tests;

[TestClass]
public sealed class GoalPlayTests
{
    private const string PlanJson="[{\"intent\":\"CreateNote\",\"title\":\"会议\",\"content\":\"资料\"},{\"type\":\"StartPlay\",\"target\":\"hide\",\"mode\":\"pet-hides\"}]";
    private static GoalPlan Plan()=>GoalProtocol.Parse(PlanJson,"记下会议，然后玩藏物",SceneKind.Indoor);
    private static (string Root,Store Store,string Id) Begin()
    {
        string root=Path.Combine(Path.GetTempPath(),"Forme-goal-play-"+Guid.NewGuid().ToString("N"));var store=new Store(root);
        var session=store.NewSession("https://api.deepseek.com");string id=Guid.NewGuid().ToString("N");var now=DateTimeOffset.Now;
        store.SaveMessage(new(id,session.Id,"user","记下会议，然后玩藏物","complete",now));store.BeginGoal(id,session.Id,Plan(),now);
        store.StartGoalStep(id,0,now);store.ApplyGoalWork(id,0,Plan().Steps[0].Work!,now);store.FinishGoalStep(id,0,GoalStepState.Completed,"saved",now);store.StartGoalStep(id,1,now);
        return(root,store,id);
    }
    [TestMethod]
    public void GameCanOnlyBeTheLastSupportedStepAndLegacyActionRestrictionsRemain()
    {
        var plan=Plan();Assert.AreEqual("hide",plan.Steps[^1].Command!.Action);
        var p=new Preferences{AllowPlayControl=true,AllowPetControl=true};
        Assert.IsNull(GoalProtocol.Preflight(plan,p,null));
        Assert.AreEqual(ActionResultCode.Unauthorized,GoalProtocol.Preflight(plan,p with{AllowPlayControl=false},null)!.Code);
        var mixed=GoalProtocol.Parse("[{\"type\":\"Dance\",\"mode\":\"sway\"},{\"type\":\"StartPlay\",\"target\":\"hide\",\"mode\":\"pet-hides\"}]","跳舞再藏物",SceneKind.Indoor);
        Assert.IsNull(GoalProtocol.Preflight(mixed,p,null));
        Assert.IsNotNull(ActionPlanner.Preflight(new(mixed.Steps.Select(x=>x.Command!).ToArray()),p));
        foreach(string invalid in new[]{
            "[{\"type\":\"StartPlay\",\"target\":\"hide\",\"mode\":\"pet-hides\"},{\"intent\":\"ListTodayTasks\"}]",
            PlanJson.Replace("pet-hides","pet-seeks"),PlanJson.Replace("\"hide\",\"mode\":\"pet-hides\"","\"boat\",\"mode\":\"pause\"")})
            Assert.Throws<InvalidDataException>(()=>GoalProtocol.Parse(invalid,"明确目标",SceneKind.Indoor));
    }
    [TestMethod]
    public void LaunchAcknowledgementCannotCompleteGameAndOnlyMatchingActualOutcomeResolvesOnce()
    {
        var data=Begin();using var store=data.Store;var now=DateTimeOffset.Now;
        Assert.Throws<OperationFailureException>(()=>store.FinishGoalStep(data.Id,1,GoalStepState.Completed,"Completed",now));
        Assert.IsFalse(store.ResolveGoalPlay(data.Id,GoalStepState.Completed,"play:hide:found",now));
        store.AwaitGoalPlay(data.Id,1,"play:hide:waiting",now);
        Assert.AreEqual(GoalRunState.WaitingForPlay,store.Goal(data.Id)!.State);
        Assert.Throws<OperationFailureException>(()=>store.DeleteGoal(data.Id));
        Assert.Throws<OperationFailureException>(()=>store.ResolveGoalPlay(data.Id,GoalStepState.Completed,"play:boat:bridge",now));
        Assert.Throws<InvalidDataException>(()=>store.ResolveGoalPlay(data.Id,GoalStepState.Completed,"play:hide:waiting",now));
        Assert.IsTrue(store.ResolveGoalPlay(data.Id,GoalStepState.Completed,"play:hide:found",now));
        Assert.IsFalse(store.ResolveGoalPlay(data.Id,GoalStepState.Completed,"play:hide:found",now));
        Assert.AreEqual(GoalRunState.Completed,store.Goal(data.Id)!.State);Assert.AreEqual(1,store.UserNotes().Count);
    }
    [TestMethod]
    public void RestartMarksPendingGameUncertainAndDoesNotReplayOrLoseEarlierArtifact()
    {
        var data=Begin();data.Store.AwaitGoalPlay(data.Id,1,"play:hide:waiting",DateTimeOffset.Now);data.Store.Dispose();
        using var recovered=new Store(data.Root);recovered.RecoverGoals();
        Assert.AreEqual(GoalRunState.Interrupted,recovered.Goal(data.Id)!.State);
        Assert.AreEqual(GoalStepState.Uncertain,recovered.Goal(data.Id)!.Steps[^1].State);
        Assert.IsFalse(recovered.ResolveGoalPlay(data.Id,GoalStepState.Completed,"play:hide:found",DateTimeOffset.Now));
        Assert.AreEqual(1,recovered.UserNotes().Count);recovered.DeleteGoal(data.Id);Assert.AreEqual(1,recovered.UserNotes().Count);
    }
    [TestMethod]
    public void StoppingPendingGamePreservesEarlierWorkAndRejectsLateResult()
    {
        var data=Begin();using var store=data.Store;var now=DateTimeOffset.Now;store.AwaitGoalPlay(data.Id,1,"play:hide:waiting",now);
        store.StopGoal(data.Id,true,now);Assert.AreEqual(GoalRunState.Cancelled,store.Goal(data.Id)!.State);
        Assert.IsFalse(store.ResolveGoalPlay(data.Id,GoalStepState.Completed,"play:hide:found",now));Assert.AreEqual(1,store.UserNotes().Count);
    }
    [TestMethod]
    public void LocalRoundAndLayoutIdentityRejectStaleGameWithoutSharingIdentifiers()
    {
        var p=new Preferences{AllowPlayControl=true,ShareChatState=true,ChatStateEndpoint="https://api.deepseek.com"};
        var initial=new GoalPlayObservation("boat","private-subject","private-round",GoalPlayPhase.Ready,3);
        var issued=ChatCapabilitySnapshot.Capture(p,SceneKind.Indoor,[]) with{Games=[initial]};
        var command=new CompanionCommand("boat","start",View:CompanionView.Room);
        Assert.IsNull(ChatCapabilitySnapshot.Validate(issued,issued,new([command])));
        foreach(var changed in new[]{initial with{Round="other"},initial with{Revision=4},initial with{Phase=GoalPlayPhase.Completed,Outcome="bridge"}})
        {
            var current=issued with{Games=[changed]};Assert.AreEqual(ActionResultCode.Interrupted,ChatCapabilitySnapshot.Validate(issued,current,new([command]))!.Code);
            Assert.IsNull(ChatCapabilitySnapshot.Validate(issued,current,new([])));
        }
        Assert.DoesNotContain("private-subject",issued.PublicContext());Assert.DoesNotContain("private-round",issued.PublicContext());
        Assert.IsFalse(GoalPlay.SameRound(initial,initial with{Round="other"}));
        Assert.AreEqual("play:boat:bridge",GoalPlay.Evidence(initial with{Phase=GoalPlayPhase.Completed,Outcome="bridge"}));
    }
}
