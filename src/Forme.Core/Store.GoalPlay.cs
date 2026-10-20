using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Forme.Core;

public sealed partial class Store
{
    private sealed record BoatPlayReceipt(string Round,string Dock,DateTimeOffset Completed);
    private const string BoatReceiptsKey="boat-play-receipts";

    public void AwaitGoalPlay(string id,int index,string evidence,DateTimeOffset now)
    {
        bool hide=evidence=="play:hide:waiting";
        bool boat=evidence.StartsWith("play:boat:waiting:",StringComparison.Ordinal)&&Guid.TryParseExact(evidence["play:boat:waiting:".Length..],"N",out _);
        if(!hide&&!boat)throw new InvalidDataException("玩法回执无效。");
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

    public void SaveBoatPlayReceipt(string round,string dock,DateTimeOffset completed)
    {
        if(!Guid.TryParseExact(round,"N",out _)||dock is not ("willow" or "bridge" or "lily"))throw new InvalidDataException("纸船结果无效。");
        using var tx=_db.BeginTransaction();var receipts=ReadBoatPlayReceipts(tx).Where(x=>x.Round!=round).Append(new(round,dock,completed)).OrderBy(x=>x.Completed).TakeLast(50).ToArray();
        Exec("INSERT INTO settings(k,v) VALUES($0,$1) ON CONFLICT(k) DO UPDATE SET v=excluded.v",tx,BoatReceiptsKey,JsonSerializer.Serialize(receipts));tx.Commit();
    }
    public bool CanReconcileGoalBoat(GoalRun run)
    {
        var round=WaitingBoatRound(run);return round is not null&&ReadBoatPlayReceipts(null).Any(x=>x.Round==round);
    }
    public bool ReconcileGoalBoat(string id,DateTimeOffset now)
    {
        using var tx=_db.BeginTransaction();var run=ReadGoal(id,tx);if(run is null||run.State!=GoalRunState.Interrupted)return false;
        string? round=WaitingBoatRound(run);if(round is null)return false;var receipt=ReadBoatPlayReceipts(tx).FirstOrDefault(x=>x.Round==round);if(receipt is null)return false;
        var steps=run.Steps.ToArray();int index=steps.Length-1;var step=steps[index];
        steps[index]=step with{State=GoalStepState.Completed,Evidence="recovered-play:boat:"+receipt.Dock};
        var nextReceipts=ReadBoatPlayReceipts(tx).Where(x=>x.Round!=round).ToArray();
        Exec("INSERT INTO settings(k,v) VALUES($0,$1) ON CONFLICT(k) DO UPDATE SET v=excluded.v",tx,BoatReceiptsKey,JsonSerializer.Serialize(nextReceipts));
        WriteGoal(run with{State=GoalRunState.Completed,Steps=steps,Updated=now},tx);tx.Commit();return true;
    }
    private static string? WaitingBoatRound(GoalRun run)
    {
        if(run.State!=GoalRunState.Interrupted||run.Steps.Count==0||run.Steps.Take(run.Steps.Count-1).Any(x=>x.State!=GoalStepState.Completed))return null;
        var step=run.Steps[^1];const string prefix="play:boat:waiting:";
        if(step.Capability!="StartPlay"||step.State!=GoalStepState.Uncertain||step.Evidence?.StartsWith(prefix,StringComparison.Ordinal)!=true)return null;
        string round=step.Evidence[prefix.Length..];return Guid.TryParseExact(round,"N",out _)?round:null;
    }
    private List<BoatPlayReceipt> ReadBoatPlayReceipts(SqliteTransaction? tx)
    {
        using var command=Command("SELECT v FROM settings WHERE k=$0",tx,BoatReceiptsKey);if(command.ExecuteScalar() is not string json)return [];
        try
        {
            var rows=JsonSerializer.Deserialize<List<BoatPlayReceipt>>(json)??[];
            return rows.Where(x=>Guid.TryParseExact(x.Round,"N",out _)&&x.Dock is "willow" or "bridge" or "lily").TakeLast(50).ToList();
        }
        catch(JsonException){return [];}
    }
}
