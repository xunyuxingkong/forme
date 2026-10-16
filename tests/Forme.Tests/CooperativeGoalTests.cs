using System.Text.Json;
using Forme.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Forme.Tests;

[TestClass]
public sealed class CooperativeGoalTests
{
    private const string Endpoint="https://cooperative.invalid/v1";
    private static readonly DateTimeOffset Now=DateTimeOffset.Now;
    private static GoalRun Begin(Store store,string kind)
    {var run=store.BeginCooperativeGoal(kind,Endpoint,"test-model",Now);store.StartGoalStep(run.Id,0,Now);return run;}
    [TestMethod]
    public void CooperativeLedgerHasNoChatSourceAndReservesBoundedRequestsBeforeSending()
    {
        WithStore(store=>{
            var hide=Begin(store,"hide");Assert.AreEqual(GoalOrigin.Cooperative,hide.Origin);Assert.AreEqual("",hide.SessionId);Assert.AreEqual(0,store.Sessions().Count);
            Assert.AreEqual("AI合作藏物",store.GoalSource(hide));
            Assert.Throws<OperationFailureException>(()=>store.ReserveCooperativeRequest(hide.Id,Endpoint,"changed-model",Now));
            Assert.AreEqual(0,store.Goal(hide.Id)!.Requests);
            Assert.AreEqual(1,store.ReserveCooperativeRequest(hide.Id,Endpoint,"test-model",Now));
            Assert.AreEqual(2,store.ReserveCooperativeRequest(hide.Id,Endpoint,"test-model",Now));
            Assert.Throws<OperationFailureException>(()=>store.ReserveCooperativeRequest(hide.Id,Endpoint,"test-model",Now));
            store.StopGoal(hide.Id,true,Now);Assert.AreEqual(2,store.Goal(hide.Id)!.Requests);
            Assert.Throws<OperationFailureException>(()=>store.ReserveCooperativeRequest(hide.Id,Endpoint,"test-model",Now));
            var boat=Begin(store,"boat");Assert.AreEqual(1,store.ReserveCooperativeRequest(boat.Id,Endpoint,"test-model",Now));
            Assert.Throws<OperationFailureException>(()=>store.ReserveCooperativeRequest(boat.Id,Endpoint,"test-model",Now));
            Assert.Throws<InvalidDataException>(()=>store.BeginCooperativeGoal("unknown",Endpoint,"test-model",Now));
            Assert.IsTrue(CapabilityCatalog.RequiresUserConfirmation(CapabilityDomain.Goal,"play.hide.cooperate"));
            Assert.IsTrue(CapabilityCatalog.RequiresUserConfirmation(CapabilityDomain.Goal,"play.boat.propose"));
        });
    }
    [TestMethod]
    public void BoatSuggestionNeedsExplicitStartAndActualDockingToCompleteOnce()
    {
        WithStore(store=>{
            var boat=Begin(store,"boat");store.ReserveCooperativeRequest(boat.Id,Endpoint,"test-model",Now);
            Assert.Throws<OperationFailureException>(()=>store.StartGoalStep(boat.Id,1,Now));
            store.FinishGoalStep(boat.Id,0,GoalStepState.Completed,"proposed:bridge",Now);store.AwaitGoalConfirmation(boat.Id,Now);
            Assert.AreEqual(GoalRunState.WaitingForUser,store.Goal(boat.Id)!.State);
            Assert.Throws<OperationFailureException>(()=>store.DeleteGoal(boat.Id));
            Assert.IsFalse(store.ResolveGoalPlay(boat.Id,GoalStepState.Completed,"play:boat:bridge",Now));
            store.StartGoalStep(boat.Id,1,Now);
            Assert.Throws<OperationFailureException>(()=>store.StartGoalStep(boat.Id,1,Now));
            Assert.Throws<OperationFailureException>(()=>store.FinishGoalStep(boat.Id,1,GoalStepState.Completed,"Completed",Now));
            store.AwaitGoalPlay(boat.Id,1,"play:boat:waiting",Now);
            Assert.IsTrue(store.ResolveGoalPlay(boat.Id,GoalStepState.Completed,"play:boat:bridge",Now));
            Assert.IsFalse(store.ResolveGoalPlay(boat.Id,GoalStepState.Completed,"play:boat:bridge",Now));
            Assert.AreEqual(GoalRunState.Completed,store.Goal(boat.Id)!.State);Assert.AreEqual(1,store.Goal(boat.Id)!.Requests);
        });
    }
    [TestMethod]
    public void InterruptedConfirmationAndSearchNeverResumeOrRestoreQuota()
    {
        WithStore(store=>{
            var boat=Begin(store,"boat");store.ReserveCooperativeRequest(boat.Id,Endpoint,"test-model",Now);
            store.FinishGoalStep(boat.Id,0,GoalStepState.Completed,"proposed:lily",Now);store.AwaitGoalConfirmation(boat.Id,Now);
            var hide=Begin(store,"hide");store.ReserveCooperativeRequest(hide.Id,Endpoint,"test-model",Now);
            store.RecoverGoals();
            foreach(var run in store.GoalRuns()){
                Assert.AreEqual(GoalRunState.Interrupted,run.State);Assert.AreEqual(1,run.Requests);
                Assert.Throws<OperationFailureException>(()=>store.StartGoalStep(run.Id,run.Id==boat.Id?1:0,Now));
                Assert.Throws<OperationFailureException>(()=>store.ReserveCooperativeRequest(run.Id,Endpoint,"test-model",Now));
            }
            Assert.AreEqual(GoalStepState.Uncertain,store.Goal(boat.Id)!.Steps[1].State);
        });
    }
    [TestMethod]
    public void BoatExperimentFeedbackContainsOnlyTargetAndObservedDock()
    {
        var preferences=new Preferences{Endpoint=Endpoint,Model="test-model",SharePlayState=true,ShareGameState=true,GameStateEndpoint=Endpoint};
        var turns=BoatPartner.Build(preferences,BoatLayout.Default(),"willow",new("willow","bridge"));
        using var payload=JsonDocument.Parse(turns[1].Content);var root=payload.RootElement;
        Assert.AreEqual("willow",root.GetProperty("goal").GetString());
        Assert.AreEqual("willow",root.GetProperty("previous").GetProperty("target").GetString());
        Assert.AreEqual("bridge",root.GetProperty("previous").GetProperty("actual").GetString());
        Assert.AreEqual(3,root.GetProperty("options").GetArrayLength());
        Assert.Throws<InvalidDataException>(()=>BoatPartner.Build(preferences,BoatLayout.Default(),"willow",new("private","bridge")));
    }
    [TestMethod]
    public void ChatAndRoomDeletionAndImportRespectIndependentOrigins()
    {
        foreach(string operation in new[]{"clear-chat","clear-room","import-chat","import-room","staged-chat","staged-room"})WithStore(store=>{
            var session=store.NewSession(Endpoint);string id=Guid.NewGuid().ToString("N");
            store.SaveMessage(new(id,session.Id,"user","查今天待办","complete",Now));
            store.BeginGoal(id,session.Id,GoalProtocol.Parse("[{\"intent\":\"ListTodayTasks\"}]","查今天待办",SceneKind.Indoor),Now);
            store.StopGoal(id,true,Now);var coop=Begin(store,"hide");store.StopGoal(coop.Id,true,Now);
            bool chat=operation.EndsWith("chat",StringComparison.Ordinal);
            if(operation.StartsWith("clear",StringComparison.Ordinal))store.ClearCategory(chat?"chat":"room");
            else if(operation.StartsWith("import",StringComparison.Ordinal))store.Import(store.Export(chat,false,false,!chat));
            else{
                string path=Path.Combine(store.DirectoryPath,"transfer.json");
                store.ExportFile(path,chat,false,false,!chat);using var plan=Store.PrepareImport(path);store.ApplyImport(plan);
            }
            Assert.AreEqual(1,store.GoalRuns().Count,operation);
            Assert.AreEqual(chat?coop.Id:id,store.GoalRuns().Single().Id,operation);
        });
    }
    private static void WithStore(Action<Store> action)
    {
        string root=Path.Combine(Path.GetTempPath(),"forme-cooperative-"+Guid.NewGuid().ToString("N"));
        try{using var store=new Store(root);action(store);}finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
