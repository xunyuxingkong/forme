using Forme.Core;

namespace Forme.App;

internal interface IGoalPlayWorld
{
    GoalPlayObservation ObserveGoalPlay(string kind);
    ActionResult? PreflightGoalPlay(CompanionCommand command);
    void StopGoalPlay(GoalPlayObservation expected);
}

internal sealed partial class DesktopHost : IGoalPlayWorld
{
    GoalPlayObservation IGoalPlayWorld.ObserveGoalPlay(string kind)=>House?.ObserveGoalPlay(kind)??new(kind,"","",GoalPlayPhase.Unavailable);
    ActionResult? IGoalPlayWorld.PreflightGoalPlay(CompanionCommand command)=>House?.PreflightGoalPlay(command);
    void IGoalPlayWorld.StopGoalPlay(GoalPlayObservation expected)=>House?.StopGoalPlay(expected);
}

internal sealed partial class MainWindow
{
    internal GoalPlayObservation ObserveGoalPlay(string kind)=>_room.ObserveGoalPlay(kind);
    internal ActionResult? PreflightGoalPlay(CompanionCommand command)=>_room.PreflightGoalPlay(command);
    internal void StopGoalPlay(GoalPlayObservation expected)=>_room.StopGoalPlay(expected);
}

internal sealed partial class Room3DView
{
    private string _hideGoalRound="",_boatGoalRound="";
    private bool _hideGoalFound,_boatGoalStarted,_boatGoalCancelled;
    private int _boatGoalRevision;
    internal GoalPlayObservation ObserveGoalPlay(string kind)
    {
        string subject=ActivitySubject+":"+_modelId;
        if(kind=="hide")return new(kind,subject,_hideGoalRound,_released?GoalPlayPhase.Unavailable:
            _hideGoalFound?GoalPlayPhase.Completed:!HideActive?GoalPlayPhase.Cancelled:!ActivityVisible?GoalPlayPhase.Paused:GoalPlayPhase.Ready,
            Outcome:_hideGoalFound?"found":null);
        return new(kind,subject,_boatGoalRound,_released?GoalPlayPhase.Unavailable:!_boatOpen||_boatGoalCancelled?GoalPlayPhase.Cancelled:
            _boatGame?.Complete==true?GoalPlayPhase.Completed:BoatPaused||!ActivityVisible?GoalPlayPhase.Paused:BoatRunning?GoalPlayPhase.Running:GoalPlayPhase.Ready,
            _boatGoalRevision,_boatGame?.Complete==true?_boatGame.Dock:null);
    }
    internal ActionResult? PreflightGoalPlay(CompanionCommand command)
    {
        if(HideActive||BoatRunning||_editing||_boatOpen&&command.Action=="hide")
            return ActionResult.Failure(ActionResultCode.SceneUnavailable,"已有玩法或家具预览尚未结束，整组未执行。",command);
        if(command.Action=="boat"&&command.Value=="open"&&_boatOpen&&_boatGame?.Complete==true)
            return ActionResult.Failure(ActionResultCode.TargetUnavailable,"上次纸船已到岸；请明确重新放船，或先退出再打开。",command);
        return null;
    }
    internal void StopGoalPlay(GoalPlayObservation expected)
    {
        if(!GoalPlay.SameRound(expected,ObserveGoalPlay(expected.Kind)))return;
        if(expected.Kind=="boat")RetrieveBoat();else EndHide();
    }
    private void ResetBoatGoalLayout()
    {
        _boatGoalRevision++;
        if(_boatGoalStarted||_boatGoalCancelled){_boatGoalRound=Guid.NewGuid().ToString("N");_boatGoalStarted=false;_boatGoalCancelled=false;}
    }
}

internal sealed partial class Controller
{
    private sealed record WaitingPlay(string Id,GoalPlayObservation Initial,GoalPlayPhase Phase);
    private WaitingPlay? _goalPlay;
    private bool _reconcilingGoalPlay;
    private IGoalPlayWorld? GoalPlayWorld=>ActivityWorld as IGoalPlayWorld;
    private void AwaitGoalPlay(string id,int index,CompanionCommand command,CancellationToken cancellation)
    {
        string kind=GoalPlay.Kind(command)!;
        var observation=GoalPlayWorld?.ObserveGoalPlay(kind)??throw new OperationFailureException("没有取得本局玩法观察，结果需核对。");
        if(cancellation.IsCancellationRequested)
        {if(observation.Round.Length>0)GoalPlayWorld?.StopGoalPlay(observation);cancellation.ThrowIfCancellationRequested();}
        if(observation.Subject.Length==0||observation.Round.Length==0||observation.Phase is GoalPlayPhase.Cancelled or GoalPlayPhase.Unavailable)
            throw new OperationFailureException("未确认玩法已经开始，结果需核对。");
        Store.AwaitGoalPlay(id,index,"play:"+kind+":waiting",DateTimeOffset.Now);
        _goalPlay=new(id,observation,observation.Phase);
        if(cancellation.IsCancellationRequested){StopWaitingGoalPlay();cancellation.ThrowIfCancellationRequested();}
        ReconcileGoalPlay();
    }
    internal void ReconcileGoalPlay()
    {
        if(_goalPlay is not {} waiting||_reconcilingGoalPlay)return;
        _reconcilingGoalPlay=true;
        try
        {
            var now=GoalPlayWorld?.ObserveGoalPlay(waiting.Initial.Kind);
            GoalStepState? terminal=now is null||!GoalPlay.SameRound(waiting.Initial,now)||now.Phase==GoalPlayPhase.Unavailable?GoalStepState.Uncertain:
                now.Phase==GoalPlayPhase.Cancelled?GoalStepState.Cancelled:now.Phase==GoalPlayPhase.Completed?GoalStepState.Completed:null;
            if(terminal is {} state)
            {
                string evidence=state==GoalStepState.Completed?GoalPlay.Evidence(now!):state==GoalStepState.Cancelled?"Cancelled":"Interrupted";
                bool resolved=Store.ResolveGoalPlay(waiting.Id,state,evidence,DateTimeOffset.Now);_goalPlay=null;
                if(Store.Goal(waiting.Id) is {} run){ChatStatus=GoalSummary(run);if(resolved)CooperativeBoatResolved(run);}
                Changed?.Invoke();
            }
            else if(waiting.Phase!=now!.Phase){_goalPlay=waiting with{Phase=now.Phase};Changed?.Invoke();}
        }
        catch(Exception ex) when(OperationErrors.Expected(ex))
        {_goalPlay=waiting with{Phase=GoalPlayPhase.Unavailable};ChatStatus="玩法结果未能写回，需核对；不会自动重放。";Changed?.Invoke();}
        finally{_reconcilingGoalPlay=false;}
    }
    private void StopWaitingGoalPlay()
    {
        if(_goalPlay is not {} waiting)return;
        _goalPlay=null;
        try
        {
            GoalPlayWorld?.StopGoalPlay(waiting.Initial);
            var now=GoalPlayWorld?.ObserveGoalPlay(waiting.Initial.Kind);
            if(now is not null&&GoalPlay.SameRound(waiting.Initial,now)&&now.Phase is GoalPlayPhase.Cancelled or GoalPlayPhase.Completed)
                Store.StopGoal(waiting.Id,true,DateTimeOffset.Now);
            else Store.ResolveGoalPlay(waiting.Id,GoalStepState.Uncertain,"Interrupted",DateTimeOffset.Now);
            if(Store.Goal(waiting.Id) is {} run)CooperativeBoatResolved(run);
        }
        catch(Exception ex) when(OperationErrors.Expected(ex))
        {_goalPlay=waiting with{Phase=GoalPlayPhase.Unavailable};ChatStatus="玩法停止或结果登记未能确认，请核对本局；不会自动重放。";}
        finally{Changed?.Invoke();}
    }
}
