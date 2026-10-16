using Forme.Core;

namespace Forme.App;

internal sealed partial class Controller
{
    private string? _cooperativeGoalId,_boatProposalGoalId;
    private bool _cooperativeRequest;
    private string _cooperativeStatus="";
    private Preferences? _boatProposalPreferences;
    private GoalPlayObservation? _boatProposalObservation;
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
        _cooperativeStatus=complete?"共同纸船实际抵达"+PaperBoat.DockName(run.Steps[^1].Evidence?[10..])+"。":"共同纸船已停止或中断，请核对本局。";
        if(SelectedActivityId==run.Id){SelectedActivityState=complete?CompanionActivityState.Completed:CompanionActivityState.Cancelled;CompanionLifeStatus=_cooperativeStatus;}
        if(complete)RecordExperience("cooperative-boat","boat",run.Steps[^1].Evidence![10..],SceneKind.Indoor);
    }
}
