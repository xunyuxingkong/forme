namespace Forme.Core;

public sealed partial class Store
{
    public void AwaitGoalPlay(string id,int index,string evidence,DateTimeOffset now)
    {
        if(evidence is not ("play:hide:waiting" or "play:boat:waiting"))throw new InvalidDataException("玩法回执无效。");
        using var tx=_db.BeginTransaction();var run=ReadGoal(id,tx)??throw new InvalidDataException("目标记录不存在。");
        if(run.State!=GoalRunState.Running||index!=run.Steps.Count-1||run.Steps[index].State!=GoalStepState.Running||run.Steps[index].Capability!="StartPlay")throw new OperationFailureException("玩法必须为最后的执行步骤。");
        var steps=run.Steps.ToArray();steps[index]=steps[index] with{State=GoalStepState.WaitingForPlay,Evidence=evidence};
        WriteGoal(run with{State=GoalRunState.WaitingForPlay,Steps=steps,Updated=now},tx);tx.Commit();
    }
    public bool ResolveGoalPlay(string id,GoalStepState state,string evidence,DateTimeOffset now)
    {
        bool completed=state==GoalStepState.Completed;
        if(state is not (GoalStepState.Completed or GoalStepState.Cancelled or GoalStepState.Uncertain)||evidence.Length>80||
            completed&&evidence is not ("play:hide:found" or "play:boat:willow" or "play:boat:bridge" or "play:boat:lily"))throw new InvalidDataException("玩法结果无效。");
        using var tx=_db.BeginTransaction();var run=ReadGoal(id,tx);
        if(run?.State!=GoalRunState.WaitingForPlay)return false;
        var steps=run.Steps.ToArray();var last=steps[^1];
        if(last.State!=GoalStepState.WaitingForPlay||completed&&(last.Evidence=="play:hide:waiting")!=evidence.StartsWith("play:hide:",StringComparison.Ordinal))throw new OperationFailureException("玩法结果与步骤不一致。");
        steps[^1]=last with{State=state,Evidence=evidence};
        WriteGoal(run with{State=completed?GoalRunState.Completed:state==GoalStepState.Cancelled?GoalRunState.Cancelled:GoalRunState.Interrupted,Steps=steps,Updated=now},tx);tx.Commit();return true;
    }
}
