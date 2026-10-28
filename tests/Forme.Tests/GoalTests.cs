using System.Text.Json;
using Forme.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Forme.Tests;

[TestClass]
public sealed class GoalTests
{
    private const string Mixed="[{\"intent\":\"CreateNote\",\"title\":\"会议\",\"content\":\"private-note-body\"},{\"type\":\"Stroll\",\"mode\":\"walk\",\"view\":\"desktop\"}]";
    private static GoalPlan Plan()=>GoalProtocol.Parse(Mixed,"记下会议，然后走几步",SceneKind.Indoor);
    private static (string Id,string Session) Source(Store store)
    {
        var session=store.NewSession("https://api.deepseek.com");string id=Guid.NewGuid().ToString("N");var now=DateTimeOffset.Now;
        store.BeginChatTurn(new(id,session.Id,"user","记下会议，然后走几步","complete",now),new(Guid.NewGuid().ToString("N"),session.Id,"assistant","","streaming",now));
        return(id,session.Id);
    }
    [TestMethod]
    public void MixedPlanUsesExistingSchemasAndRejectsEveryInvalidStepBeforeExecution()
    {
        var plan=Plan();Assert.AreEqual(2,plan.Steps.Count);Assert.AreEqual(ProductivityIntentType.CreateNote,plan.Steps[0].Work!.Type);
        Assert.AreEqual(CompanionView.Desktop,plan.Steps[1].Command!.View);
        foreach(string bad in new[]{"[]","{}","[{\"type\":\"Stroll\",\"intent\":\"CreateNote\"}]",
            Mixed.Replace("\"mode\":\"walk\"","\"mode\":\"wrong\""),Mixed.Replace("\"intent\":\"CreateNote\"","\"intent\":\"DeleteNote\""),
            "[{\"type\":\"ChangeWeather\",\"mode\":\"rain\"}]",Mixed[..^1]+",{\"intent\":\"ListTodayTasks\"},{\"intent\":\"ListTodayTasks\"}]",
            "[{\"type\":\"Stop\"},{\"intent\":\"ListTodayTasks\"}]"})
            Assert.Throws<InvalidDataException>(()=>GoalProtocol.Parse(bad,"明确目标",SceneKind.Indoor));
        string future=DateTimeOffset.Now.AddDays(1).ToString("O"),later=DateTimeOffset.Now.AddDays(2).ToString("O");
        string times=JsonSerializer.Serialize(new[]{new{intent="CreateTask",title="任务",dueAt=future,remindAt=later}});
        Assert.Throws<InvalidDataException>(()=>GoalProtocol.Parse(times,"设置任务",SceneKind.Indoor));
    }
    [TestMethod]
    public void AutomaticSceneAndRepeatedDanceCountTowardTheSameThreeStepLimit()
    {
        var plan=GoalProtocol.Parse("[{\"type\":\"GoTo\",\"target\":\"pond\"},{\"intent\":\"ListTodayTasks\"}]","去池塘再查待办",SceneKind.Indoor);
        Assert.AreEqual(3,plan.Steps.Count);Assert.AreEqual("scene",plan.Steps[0].Command!.Action);
        Assert.Throws<InvalidDataException>(()=>GoalProtocol.Parse("[{\"type\":\"Dance\",\"mode\":\"hop\",\"count\":3},{\"intent\":\"ListTodayTasks\"}]","跳三次再查待办",SceneKind.Indoor));
    }
    [TestMethod]
    public void OnlyAFirstStepBlockedMoveCanBeReplannedOnce()
    {
        WithStore(store=>{
            var source=Source(store);var plan=GoalProtocol.Parse("[{\"type\":\"Stroll\",\"mode\":\"walk\",\"view\":\"desktop\"},{\"intent\":\"CreateTask\",\"title\":\"后续待办\"}]","桌面走几步，再创建待办",SceneKind.Indoor);
            store.BeginGoal(source.Id,source.Session,plan,DateTimeOffset.Now);store.StartGoalStep(source.Id,0,DateTimeOffset.Now);
            store.FinishGoalStep(source.Id,0,GoalStepState.Failed,ActionResultCode.PathBlocked.ToString(),DateTimeOffset.Now);
            var failed=store.Goal(source.Id)!;Assert.IsTrue(GoalProtocol.CanReplan(failed));
            Assert.IsTrue(store.BeginGoalReplan(source.Id,DateTimeOffset.Now));Assert.AreEqual(1,store.Goal(source.Id)!.Replans);
            Assert.IsFalse(store.BeginGoalReplan(source.Id,DateTimeOffset.Now));

            foreach(string evidence in new[]{ActionResultCode.TargetUnavailable.ToString(),"preflight:"+ActionResultCode.PathBlocked,"preflight:"+ActionResultCode.TargetUnavailable})
                Assert.IsTrue(GoalProtocol.CanReplan(failed with{Replans=0,Steps=[failed.Steps[0] with{Evidence=evidence},failed.Steps[1]]}),evidence);
            var completedEarlier=failed with{Steps=[failed.Steps[0] with{State=GoalStepState.Completed},failed.Steps[1] with{State=GoalStepState.Failed,Evidence=ActionResultCode.PathBlocked.ToString()}]};
            Assert.IsFalse(GoalProtocol.CanReplan(completedEarlier));
            Assert.IsFalse(GoalProtocol.CanReplan(failed with{Steps=[failed.Steps[0] with{Evidence="preflight:"+ActionResultCode.Unauthorized},failed.Steps[1]]}));
            Assert.IsFalse(GoalProtocol.CanReplan(failed with{Steps=[failed.Steps[0] with{State=GoalStepState.Uncertain},failed.Steps[1]]}));
            Assert.IsFalse(GoalProtocol.CanReplan(failed with{State=GoalRunState.Cancelled}));
        });
    }
    [TestMethod]
    public void ProtocolAndPartialMarkersStayOutOfTranscriptAndContext()
    {
        string response=GoalProtocol.Marker+"\n"+Mixed+"\n"+new string((char)96,3);
        Assert.IsTrue(GoalProtocol.TryExtract(response,out var json));Assert.AreEqual(Mixed,json);
        Assert.AreEqual("",ConversationText.Visible(response));Assert.AreEqual("",CompanionCommands.ContextText(response));
        for(int i=3;i<=GoalProtocol.Marker.Length;i++)Assert.AreEqual("",CompanionCommands.VisibleText(GoalProtocol.Marker[..i]));
        Assert.Throws<InvalidDataException>(()=>GoalProtocol.TryExtract(response[..^3],out _));
        Assert.IsTrue(GoalProtocol.TryExtract("好的，我来安排。\n"+response+"\n计划完成后告诉你。",out var wrapped));Assert.AreEqual(Mixed,wrapped);
        Assert.Throws<InvalidDataException>(()=>GoalProtocol.TryExtract("说明"+response+response,out _));
        Assert.AreEqual("",CompanionCommands.VisibleText(ProductivityIntentProtocol.Marker+"\nprivate"));
    }
    [TestMethod]
    public void OptInPromptAndFullSnapshotStillFitDefaultBudget()
    {
        var p=new Preferences{AllowGoalPlans=true,AllowPetControl=true,AllowSceneControl=true,AllowPlayControl=true,AllowLayoutPreview=true,ShareChatState=true,ChatStateEndpoint="https://api.deepseek.com"};
        foreach(string model in new[]{"sprout","genshin-hu-tao"})
        {
            var actors=new[]{CompanionView.Room,CompanionView.Desktop}.Select(view=>new ActivityObservation(view,"local-id",1,view==CompanionView.Room?SceneKind.Indoor:SceneKind.Desktop,
                model,"clear",true,true,"idle",ActivitySelection.Options(view,view==CompanionView.Room?SceneKind.Indoor:SceneKind.Desktop,model,p,_=>null)){Visible=true}).ToArray();
            var snapshot=ChatCapabilitySnapshot.Capture(p,SceneKind.Indoor,actors);
            string prompt=AiIntentProtocol.RoutingInstructions(snapshot);StringAssert.Contains(prompt,GoalProtocol.Marker);
            var turns=AiClient.BuildContext(p with{ContextBudget=8192},[],"记下准备开会，然后在桌面走几步",out _,additionalSystemInstruction:prompt);
            Assert.IsTrue(turns.Sum(AiClient.Estimate)<=4096,"实际字节："+turns.Sum(AiClient.Estimate));
            AiClient.BuildContext(p,[],"记下准备开会，然后在桌面走几步",out _,additionalSystemInstruction:prompt);
            Assert.IsFalse(AiIntentProtocol.RoutingInstructions(snapshot with{GoalPlans=false}).Contains(GoalProtocol.Marker));
        }
    }
    [TestMethod]
    public void LedgerOrdersStepsAndPreventsRewritingCommittedArtifact()
    {
        WithStore(store=>{
            var source=Source(store);var plan=Plan();store.BeginGoal(source.Id,source.Session,plan,DateTimeOffset.Now,"https://api.deepseek.com","test-model");
            Assert.IsFalse(store.ContextMessages(source.Session).Any(x=>x.Id==source.Id));
            Assert.Throws<OperationFailureException>(()=>store.StartGoalStep(source.Id,1,DateTimeOffset.Now));
            store.StartGoalStep(source.Id,0,DateTimeOffset.Now);Assert.AreEqual("saved",store.ApplyGoalWork(source.Id,0,plan.Steps[0].Work!,DateTimeOffset.Now));
            Assert.IsTrue(store.GoalArtifactExists(store.Goal(source.Id)!.Steps[0]));
            Assert.Throws<OperationFailureException>(()=>store.ApplyGoalWork(source.Id,0,plan.Steps[0].Work!,DateTimeOffset.Now));
            store.FinishGoalStep(source.Id,0,GoalStepState.Completed,"saved",DateTimeOffset.Now);
            Assert.Throws<OperationFailureException>(()=>store.StartGoalStep(source.Id,0,DateTimeOffset.Now));
            store.StartGoalStep(source.Id,1,DateTimeOffset.Now);store.FinishGoalStep(source.Id,1,GoalStepState.Completed,"Completed",DateTimeOffset.Now);
            var run=store.Goal(source.Id)!;Assert.AreEqual(GoalRunState.Completed,run.State);
            Assert.AreEqual("test-model",run.Model);Assert.AreEqual("https://api.deepseek.com",run.Endpoint);
            Assert.IsFalse(JsonSerializer.Serialize(run).Contains("private-note-body"));
            store.DeleteGoal(source.Id);Assert.AreEqual(1,store.UserNotes().Count);
            Assert.Throws<InvalidDataException>(()=>store.BeginGoal(source.Id,source.Session,plan,DateTimeOffset.Now));
        });
    }
    [TestMethod]
    public void InterruptedWriteBecomesUncertainAndNeverReplays()
    {
        WithStore(store=>{
            var source=Source(store);var plan=Plan();store.BeginGoal(source.Id,source.Session,plan,DateTimeOffset.Now);
            store.StartGoalStep(source.Id,0,DateTimeOffset.Now);store.ApplyGoalWork(source.Id,0,plan.Steps[0].Work!,DateTimeOffset.Now);
            store.RecoverGoals();var run=store.Goal(source.Id)!;
            Assert.AreEqual(GoalRunState.Interrupted,run.State);Assert.AreEqual(GoalStepState.Uncertain,run.Steps[0].State);Assert.AreEqual(GoalStepState.Skipped,run.Steps[1].State);
            Assert.IsTrue(store.CanReconcileGoalArtifact(run,run.Steps[0]));
            Assert.IsTrue(store.ReconcileGoalArtifact(source.Id,0,DateTimeOffset.Now));
            run=store.Goal(source.Id)!;Assert.AreEqual(GoalRunState.Interrupted,run.State);Assert.AreEqual(GoalStepState.Completed,run.Steps[0].State);
            Assert.AreEqual("recovered-artifact",run.Steps[0].Evidence);Assert.AreEqual(GoalStepState.Skipped,run.Steps[1].State);
            Assert.IsFalse(store.CanReconcileGoalArtifact(run,run.Steps[0]));Assert.IsFalse(store.ReconcileGoalArtifact(source.Id,0,DateTimeOffset.Now));
            Assert.Throws<OperationFailureException>(()=>store.StartGoalStep(source.Id,0,DateTimeOffset.Now));
            Assert.Throws<OperationFailureException>(()=>store.BeginGoal(source.Id,source.Session,plan,DateTimeOffset.Now));
            store.RecoverGoals();Assert.AreEqual(1,store.UserNotes().Count);
            store.DeleteSession(source.Session);Assert.AreEqual(0,store.GoalRuns().Count);Assert.AreEqual(1,store.UserNotes().Count);
        });
    }
    [TestMethod]
    public void InterruptedWriteWithoutItsArtifactCannotBeConfirmed()
    {
        WithStore(store=>{
            var source=Source(store);var plan=Plan();store.BeginGoal(source.Id,source.Session,plan,DateTimeOffset.Now);
            store.StartGoalStep(source.Id,0,DateTimeOffset.Now);store.RecoverGoals();var run=store.Goal(source.Id)!;
            Assert.IsFalse(store.CanReconcileGoalArtifact(run,run.Steps[0]));
            Assert.IsFalse(store.ReconcileGoalArtifact(source.Id,0,DateTimeOffset.Now));
            Assert.AreEqual(GoalStepState.Uncertain,store.Goal(source.Id)!.Steps[0].State);Assert.AreEqual(0,store.UserNotes().Count);
        });
    }
    [TestMethod]
    public void ChatImportRemovesLedgerButRetainsLocalReplayBarrier()
    {
        WithStore(store=>{
            var source=Source(store);var plan=Plan();store.BeginGoal(source.Id,source.Session,plan,DateTimeOffset.Now);store.StopGoal(source.Id,true,DateTimeOffset.Now);
            var export=store.Export(true,false,false,false);store.Import(export);
            Assert.AreEqual(0,store.GoalRuns().Count);Assert.IsFalse(store.ContextMessages(source.Session).Any(x=>x.Id==source.Id));
            Assert.Throws<InvalidDataException>(()=>store.BeginGoal(source.Id,source.Session,plan,DateTimeOffset.Now));
            var next=Source(store);store.BeginGoal(next.Id,next.Session,plan,DateTimeOffset.Now);store.StopGoal(next.Id,true,DateTimeOffset.Now);
            store.ClearCategory("chat");Assert.AreEqual(0,store.GoalRuns().Count);
        });
    }
    private static void WithStore(Action<Store> action)
    {
        string root=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"forme-goals-"+Guid.NewGuid().ToString("N")));
        try{using var store=new Store(root);action(store);}finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
