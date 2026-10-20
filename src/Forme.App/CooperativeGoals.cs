using Forme.Core;

namespace Forme.App;

internal sealed partial class Controller
{
    private string? _cooperativeGoalId,_boatProposalGoalId;
    private bool _cooperativeRequest;
    private string _cooperativeStatus="";
    private Preferences? _boatProposalPreferences;
    private GoalPlayObservation? _boatProposalObservation;
    private sealed record BoatExperiment(string GoalId,GoalPlayObservation Observation,BoatLayout Layout,string Subject,string Target,string ActualDock,DateTimeOffset Finished,Preferences Preferences);
    private BoatExperiment? _boatExperiment;
    internal bool BoatExperimentAvailable{get{ReconcileBoatExperiment();return _boatExperiment is not null;}}
    private GoalRun BeginCooperative(string kind)
    {
        if(_goalPlay is not null)throw new OperationFailureException("请先完成或停止正在等待结果的玩法目标。");
        var run=Store.BeginCooperativeGoal(kind,Preferences.Endpoint,Preferences.Model,DateTimeOffset.Now);
        Store.StartGoalStep(run.Id,0,DateTimeOffset.Now);_cooperativeGoalId=run.Id;FocusTaskCard("cooperative");EndProject();return run;
    }
    private void CooperativeStatus(string status)
    {_cooperativeStatus=status;CompanionLifeStatus=status;Tick?.Invoke();Changed?.Invoke();}
    private void FailCooperative(string id,Exception failure)
    {
        var run=Store.Goal(id);var step=run?.Steps.FirstOrDefault(x=>x.State==GoalStepState.Running);
        if(step is not null)Store.FinishGoalStep(id,step.Index,failure is OperationCanceledException?GoalStepState.Cancelled:GoalStepState.Failed,
            failure is OperationCanceledException?"Cancelled":"Failed",DateTimeOffset.Now);
    }
    private bool CooperativeGrant(Preferences p)=>p.AllowPlayControl&&p.SharePlayState&&p.ShareGameState&&AiClient.SameEndpoint(p.GameStateEndpoint,p.Endpoint);
    private bool ProposalBindingValid(Preferences p)=>_boatProposalPreferences is {} issued&&CooperativeGrant(p)&&
        AiClient.SameEndpoint(p.Endpoint,issued.Endpoint)&&p.Model==issued.Model&&p.PetModel==issued.PetModel;
    private void ClearBoatProposal()
    {CooperativeBoatBefore=null;CooperativeBoatProposal=null;CooperativeBoatSubject="";_boatProposalGoalId=null;_boatProposalPreferences=null;_boatProposalObservation=null;}
    private void CancelBoatProposal()
    {
        string? id=_boatProposalGoalId;ClearBoatProposal();
        if(id is not null){Store.StopGoal(id,true,DateTimeOffset.Now);if(SelectedActivityId==id)SelectedActivityState=CompanionActivityState.Cancelled;CooperativeStatus("纸船建议已失效或已放弃。");}
    }
    internal void ReconcileCooperativeProposal()
    {
        if(_boatProposalGoalId is null)return;
        try
        {
            if(DateTimeOffset.UtcNow-_boatProposalAt>TimeSpan.FromMinutes(10)||!ProposalBindingValid(Preferences)||
                GoalPlayWorld?.ObserveGoalPlay("boat")!=_boatProposalObservation||
                ActivityWorld is not ICooperativePlayWorld world||CooperativeBoatBefore is null||!BoatPartner.Same(CooperativeBoatBefore,world.CurrentBoat()))CancelBoatProposal();
        }
        catch(Exception ex) when(OperationErrors.Expected(ex)){CancelBoatProposal();}
    }
    private void ReconcileBoatExperiment()
    {
        if(_boatExperiment is not {} experiment)return;
        try
        {
            var source=Store.Goal(experiment.GoalId);
            if(DateTimeOffset.UtcNow-experiment.Finished>TimeSpan.FromMinutes(10)||!CooperativeGrant(Preferences)||
                Preferences.Endpoint!=experiment.Preferences.Endpoint||Preferences.Model!=experiment.Preferences.Model||Preferences.PetModel!=experiment.Preferences.PetModel||
                source is null||source.Origin!=GoalOrigin.Cooperative||source.State!=GoalRunState.Completed||source.Steps[^1].Evidence!="play:boat:"+experiment.ActualDock||
                GoalPlayWorld?.ObserveGoalPlay("boat")!=experiment.Observation||
                ActivityWorld?.Observe(CompanionView.Room).SubjectId!=experiment.Subject||
                ActivityWorld is not ICooperativePlayWorld world||!BoatPartner.Same(experiment.Layout,world.CurrentBoat()))_boatExperiment=null;
        }
        catch(Exception ex) when(OperationErrors.Expected(ex)){_boatExperiment=null;}
    }
    internal Task ProposeBoatExperiment()
    {
        ReconcileBoatExperiment();var experiment=_boatExperiment??throw new OperationFailureException("可用的纸船实测结果已变化或过期。");
        _boatExperiment=null;
        return ProposeBoat(experiment.Target,new(experiment.Target,experiment.ActualDock));
    }
    internal void ConfirmBoatFromTaskCard(string key)
    {
        if(_boatProposalGoalId is null||key!="goal:"+_boatProposalGoalId)throw new OperationFailureException("当前建议已变化，请查看最新任务卡。");
        ConfirmBoatProposal();
    }
    private void StopCooperativeTask(string key)
    {
        if(_boatProposalGoalId is {} proposal&&key=="goal:"+proposal){CancelBoatProposal();return;}
        StopReply();
    }
    private void CooperativeBoatResolved(GoalRun run)
    {
        if(run.Origin!=GoalOrigin.Cooperative||run.Steps[0].Capability!="ProposeBoat")return;
        bool complete=run.State==GoalRunState.Completed;
        string? actualEvidence=run.Steps[^1].Evidence;string? targetEvidence=run.Steps[0].Evidence;
        string? actual=complete&&actualEvidence?.StartsWith("recovered-play:boat:",StringComparison.Ordinal)==true?actualEvidence["recovered-play:boat:".Length..]:
            complete&&actualEvidence?.StartsWith("play:boat:",StringComparison.Ordinal)==true?actualEvidence[10..]:null;
        string? target=null;
        if(targetEvidence?.StartsWith("proposed:",StringComparison.Ordinal)==true)
        {
            target=targetEvidence![9..].Split(':',2)[0];
        }
        _boatExperiment=null;
        if(complete&&(actual is "willow" or "bridge" or "lily")&&(target is "willow" or "bridge" or "lily")&&actual!=target&&
            ActivityWorld is ICooperativePlayWorld world&&GoalPlayWorld?.ObserveGoalPlay("boat") is {} observation&&observation.Phase==GoalPlayPhase.Completed&&observation.Outcome==actual)
        {
            try{_boatExperiment=new(run.Id,observation,world.CurrentBoat(),ActivityWorld.Observe(CompanionView.Room).SubjectId,target,actual,DateTimeOffset.UtcNow,Preferences with{});}
            catch(Exception ex) when(OperationErrors.Expected(ex)){_boatExperiment=null;}
        }
        _cooperativeStatus=complete?"共同纸船实际抵达"+PaperBoat.DockName(actual)+"。"+(_boatExperiment is null?"":"可以让AI结合这次实测结果再比较一轮。"):
            "共同纸船已停止或中断，请核对本局。";
        if(SelectedActivityId==run.Id){SelectedActivityState=complete?CompanionActivityState.Completed:CompanionActivityState.Cancelled;CompanionLifeStatus=_cooperativeStatus;}
        if(complete&&actual is not null)RecordExperience("cooperative-boat","boat",actual,SceneKind.Indoor);
    }
}
