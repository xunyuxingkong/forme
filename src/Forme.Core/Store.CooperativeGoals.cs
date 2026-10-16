using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Forme.Core;

public sealed partial class Store
{
    public GoalRun BeginCooperativeGoal(string kind,string endpoint,string model,DateTimeOffset now)
    {
        string capability=kind switch{"hide"=>"play.hide.cooperate","boat"=>"play.boat.propose",_=>throw new InvalidDataException("未知合作玩法。")};
        if(!CapabilityCatalog.RequiresUserConfirmation(CapabilityDomain.Goal,capability))throw new InvalidOperationException("合作玩法必须由用户主动发起。");
        AiClient.ValidateEndpoint(endpoint);if(string.IsNullOrWhiteSpace(model)||model.Length>200)throw new InvalidDataException("模型标记无效。");
        using var tx=_db.BeginTransaction();
        using(var count=Command("SELECT count(*) FROM settings WHERE k LIKE 'goal-run:%'",tx))
            if(Convert.ToInt64(count.ExecuteScalar())>=50)throw new InvalidDataException("最多保留50条执行记录，请在执行记录管理中删除不需要的记录。");
        var steps=kind=="hide"?new[]{new GoalStepRecord(0,"PlanHide",CompanionView.Room,GoalStepState.Pending),new GoalStepRecord(1,"SearchHide",CompanionView.Room,GoalStepState.Pending)}:
            new[]{new GoalStepRecord(0,"ProposeBoat",CompanionView.Room,GoalStepState.Pending),new GoalStepRecord(1,"StartPlay",CompanionView.Room,GoalStepState.Pending)};
        var run=new GoalRun(Guid.NewGuid().ToString("N"),"",GoalRunState.Ready,steps,now,now,endpoint,model){Origin=GoalOrigin.Cooperative};WriteGoal(run,tx);tx.Commit();return run;
    }
    public int ReserveCooperativeRequest(string id,string endpoint,string model,DateTimeOffset now)
    {
        using var tx=_db.BeginTransaction();var run=ReadGoal(id,tx)??throw new InvalidDataException("合作记录不存在。");
        int maximum=run.Steps[0].Capability=="PlanHide"?2:1;
        if(run.Origin!=GoalOrigin.Cooperative||run.State!=GoalRunState.Running||run.Requests>=maximum||run.Endpoint!=endpoint||run.Model!=model)throw new OperationFailureException("合作目标、服务或调用额度已变化，本次未请求AI。");
        var next=run with{Requests=run.Requests+1,Updated=now};WriteGoal(next,tx);tx.Commit();return next.Requests;
    }
    public void AwaitGoalConfirmation(string id,DateTimeOffset now)
    {
        using var tx=_db.BeginTransaction();var run=ReadGoal(id,tx)??throw new InvalidDataException("合作记录不存在。");
        if(run.Origin!=GoalOrigin.Cooperative||run.State!=GoalRunState.Running||run.Steps[0].Capability!="ProposeBoat"||run.Steps[0].State!=GoalStepState.Completed||run.Steps[1].State!=GoalStepState.Pending)throw new OperationFailureException("纸船建议状态已变化。");
        var steps=run.Steps.ToArray();steps[1]=steps[1] with{State=GoalStepState.WaitingForUser};
        WriteGoal(run with{State=GoalRunState.WaitingForUser,Steps=steps,Updated=now},tx);tx.Commit();
    }
    private void DeleteGoalOrigin(GoalOrigin origin,SqliteTransaction? tx=null)
    {
        var ids=new List<string>();
        using(var command=Command("SELECT k,v FROM settings WHERE k LIKE 'goal-run:%'",tx))
        using(var reader=command.ExecuteReader())while(reader.Read())
            if(JsonSerializer.Deserialize<GoalRun>(reader.GetString(1))?.Origin==origin)ids.Add(reader.GetString(0));
        foreach(string id in ids)Exec("DELETE FROM settings WHERE k=$0",tx,id);
    }
}
