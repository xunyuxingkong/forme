using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Forme.Core;

public sealed partial class Store
{
    private const string GoalPrefix="goal-run:";
    public IReadOnlyList<GoalRun> GoalRuns(string? sessionId=null)=>Query("SELECT v FROM settings WHERE k LIKE 'goal-run:%'",r=>JsonSerializer.Deserialize<GoalRun>(r.GetString(0))!).Where(x=>sessionId is null||x.SessionId==sessionId).OrderByDescending(x=>x.Updated).ToArray();
    public GoalRun? Goal(string id)=>ReadGoal(id,null);
    public string? GoalSource(GoalRun run)=>run.Origin==GoalOrigin.Cooperative?(run.Steps[0].Capability=="PlanHide"?"AI合作藏物":"AI合作纸船"):Query("SELECT content FROM messages WHERE id=$0 AND session=$1 AND role='user'",r=>r.GetString(0),run.Id,run.SessionId).FirstOrDefault();
    private GoalRun? ReadGoal(string id,SqliteTransaction? tx)
    {using var c=Command("SELECT v FROM settings WHERE k=$0",tx,GoalPrefix+id);return c.ExecuteScalar() is string value?JsonSerializer.Deserialize<GoalRun>(value):null;}
    private void WriteGoal(GoalRun run,SqliteTransaction? tx)=>Exec("INSERT INTO settings VALUES($0,$1) ON CONFLICT(k) DO UPDATE SET v=excluded.v",tx,GoalPrefix+run.Id,JsonSerializer.Serialize(run));
    public GoalRun BeginGoal(string id,string sessionId,GoalPlan plan,DateTimeOffset now,string? endpoint=null,string? model=null)
    {
        if(!Guid.TryParseExact(id,"N",out _)||!Guid.TryParseExact(sessionId,"N",out _)||plan.Steps.Count is <1 or >3)throw new InvalidDataException("目标计划记录无效。");
        if(endpoint is not null)AiClient.ValidateEndpoint(endpoint);
        if(model?.Length>100)throw new InvalidDataException("目标模型标记无效。");
        using var tx=_db.BeginTransaction();
        using(var count=Command("SELECT count(*) FROM settings WHERE k LIKE 'goal-run:%'",tx))
            if(Convert.ToInt64(count.ExecuteScalar())>=50)throw new InvalidDataException("最多保留50条目标执行记录，请在聊天记录管理中删除不需要的记录。");
        if(ReadGoal(id,tx) is not null)throw new OperationFailureException("这条消息已有执行记录，不会重复执行；请查看记录，或明确发送一个新目标。");
        using(var source=Command("SELECT count(*) FROM messages WHERE id=$0 AND session=$1 AND role='user' AND status<>'local'",tx,id,sessionId))
            if(Convert.ToInt64(source.ExecuteScalar())!=1)throw new InvalidDataException("目标原消息不存在或已经处理。");
        var steps=plan.Steps.Select((x,i)=>new GoalStepRecord(i,x.Capability,x.Command?.View,GoalStepState.Pending,
            x.Work?.Type is ProductivityIntentType.CreateNote or ProductivityIntentType.CreateTask or ProductivityIntentType.CreateReminder?GoalProtocol.ArtifactId(id,i):null)).ToArray();
        var run=new GoalRun(id,sessionId,GoalRunState.Ready,Array.AsReadOnly(steps),now,now,endpoint,model);WriteGoal(run,tx);
        Exec("UPDATE messages SET status='local' WHERE id=$0",tx,id);tx.Commit();return run;
    }
    public bool BeginGoalReplan(string id,DateTimeOffset now)
    {
        using var tx=_db.BeginTransaction();var run=ReadGoal(id,tx);
        if(run is null||!GoalProtocol.CanReplan(run))return false;
        WriteGoal(run with{Replans=1,Updated=now},tx);tx.Commit();return true;
    }
    public void StartGoalStep(string id,int index,DateTimeOffset now)
    {
        using var tx=_db.BeginTransaction();var run=ReadGoal(id,tx)??throw new InvalidDataException("目标记录不存在。");
        if(run.State is not (GoalRunState.Ready or GoalRunState.Running or GoalRunState.WaitingForUser)||index<0||index>=run.Steps.Count||
            !(run.Steps[index].State==GoalStepState.Pending||run.Origin==GoalOrigin.Cooperative&&run.State==GoalRunState.WaitingForUser&&run.Steps[index].State==GoalStepState.WaitingForUser)||run.Steps.Take(index).Any(x=>x.State!=GoalStepState.Completed))
            throw new OperationFailureException("目标步骤已经处理或顺序改变，没有重复执行。");
        var steps=run.Steps.ToArray();steps[index]=steps[index] with{State=GoalStepState.Running};
        WriteGoal(run with{State=GoalRunState.Running,Steps=steps,Updated=now},tx);tx.Commit();
    }
    public void FinishGoalStep(string id,int index,GoalStepState state,string evidence,DateTimeOffset now)
    {
        if(state is not (GoalStepState.Completed or GoalStepState.Failed or GoalStepState.Cancelled or GoalStepState.Uncertain)||evidence.Length>80)throw new InvalidDataException("步骤回执无效。");
        using var tx=_db.BeginTransaction();var run=ReadGoal(id,tx)??throw new InvalidDataException("目标记录不存在。");
        if(index<0||index>=run.Steps.Count||run.Steps[index].State!=GoalStepState.Running)throw new OperationFailureException("步骤状态已改变，没有重复写入回执。");
        if(run.Steps[index].Capability=="StartPlay"&&state==GoalStepState.Completed)throw new OperationFailureException("玩法启动不等于本局完成，需等待真实结果。");
        var steps=run.Steps.ToArray();steps[index]=steps[index] with{State=state,Evidence=evidence};
        bool complete=state==GoalStepState.Completed;
        if(!complete)for(int i=index+1;i<steps.Length;i++)steps[i]=steps[i] with{State=GoalStepState.Skipped};
        var result=complete?(index==steps.Length-1?GoalRunState.Completed:GoalRunState.Running):state==GoalStepState.Cancelled?GoalRunState.Cancelled:GoalRunState.Failed;
        WriteGoal(run with{State=result,Steps=steps,Updated=now},tx);tx.Commit();
    }
    public void StopGoal(string id,bool cancelled,DateTimeOffset now)
    {
        using var tx=_db.BeginTransaction();var run=ReadGoal(id,tx);if(run is null||run.State is not (GoalRunState.Ready or GoalRunState.Running or GoalRunState.WaitingForPlay or GoalRunState.WaitingForUser))return;
        var steps=run.Steps.Select(x=>x.State is GoalStepState.Running or GoalStepState.WaitingForPlay or GoalStepState.WaitingForUser?x with{State=cancelled?GoalStepState.Cancelled:GoalStepState.Uncertain}:x.State==GoalStepState.Pending?x with{State=GoalStepState.Skipped}:x).ToArray();
        WriteGoal(run with{State=cancelled?GoalRunState.Cancelled:GoalRunState.Failed,Steps=steps,Updated=now},tx);tx.Commit();
    }
    public void RecoverGoals()
    {
        foreach(var run in GoalRuns().Where(x=>x.State is GoalRunState.Ready or GoalRunState.Running or GoalRunState.WaitingForPlay or GoalRunState.WaitingForUser))
            WriteGoal(run with{State=GoalRunState.Interrupted,Updated=DateTimeOffset.Now,Steps=run.Steps.Select(x=>x.State is GoalStepState.Running or GoalStepState.WaitingForPlay or GoalStepState.WaitingForUser?x with{State=GoalStepState.Uncertain}:x.State==GoalStepState.Pending?x with{State=GoalStepState.Skipped}:x).ToArray()},null);
    }
    public void DeleteGoal(string id)
    {
        if(Goal(id)?.State is GoalRunState.Ready or GoalRunState.Running or GoalRunState.WaitingForPlay or GoalRunState.WaitingForUser)throw new OperationFailureException("请先停止目标执行。");
        Exec("DELETE FROM settings WHERE k=$0",null,GoalPrefix+id);RemoveBackup();
    }
    public bool GoalArtifactExists(GoalStepRecord step)
    {
        return GoalArtifactExists(step,null);
    }
    public bool CanReconcileGoalArtifact(GoalRun run,GoalStepRecord step)=>
        run.State==GoalRunState.Interrupted&&step.State==GoalStepState.Uncertain&&GoalArtifactExists(step);
    public bool ReconcileGoalArtifact(string id,int index,DateTimeOffset now)
    {
        using var tx=_db.BeginTransaction();var run=ReadGoal(id,tx);
        if(run is null||run.State!=GoalRunState.Interrupted||index<0||index>=run.Steps.Count)return false;
        var step=run.Steps[index];
        if(step.State!=GoalStepState.Uncertain||!GoalArtifactExists(step,tx))return false;
        var steps=run.Steps.ToArray();steps[index]=step with{State=GoalStepState.Completed,Evidence="recovered-artifact"};
        var state=steps.All(x=>x.State==GoalStepState.Completed)?GoalRunState.Completed:GoalRunState.Interrupted;
        WriteGoal(run with{State=state,Steps=steps,Updated=now},tx);tx.Commit();return true;
    }
    private bool GoalArtifactExists(GoalStepRecord step,SqliteTransaction? tx)
    {
        if(step.ArtifactId is not {} id)return false;
        string? table=step.Capability switch{"CreateNote"=>"user_notes","CreateTask"=>"user_tasks","CreateReminder"=>"user_reminders",_=>null};
        if(table is null)return false;
        using var command=Command("SELECT count(*) FROM "+table+" WHERE id=$0",tx,id);
        return Convert.ToInt64(command.ExecuteScalar())==1;
    }
    public string ApplyGoalWork(string id,int index,ProductivityIntent work,DateTimeOffset now)
    {
        var run=Goal(id)??throw new InvalidDataException("目标记录不存在。");
        if(index<0||index>=run.Steps.Count||run.State!=GoalRunState.Running||run.Steps[index].State!=GoalStepState.Running||run.Steps[index].Capability!=work.Type.ToString())throw new OperationFailureException("目标步骤未进入执行状态。");
        var step=run.Steps[index];GoalProtocol.ValidateWork(work,now);
        if(GoalArtifactExists(step))throw new OperationFailureException("该步骤的产物已存在，未覆盖或重复保存；请查看已有记录。");
        switch(work.Type)
        {
            case ProductivityIntentType.CreateNote:SaveUserNote(GoalProtocol.Note(work,step.ArtifactId!,now));break;
            case ProductivityIntentType.CreateTask:SaveUserTask(GoalProtocol.Task(work,step.ArtifactId!,now));break;
            case ProductivityIntentType.CreateReminder:SaveUserReminder(GoalProtocol.Reminder(work,step.ArtifactId!,now));break;
            case ProductivityIntentType.SearchNote:return "found:"+UserNotes(search:work.Query,limit:50).Count;
            case ProductivityIntentType.ListTodayTasks:
                var today=DateOnly.FromDateTime(now.LocalDateTime);return "today:"+UserTasks(status:"todo",limit:100).Count(x=>x.DueAt is null||DateOnly.FromDateTime(x.DueAt.Value.LocalDateTime)==today);
        }
        if(!GoalArtifactExists(step))throw new OperationFailureException("未能读回保存的产物，后续步骤停止。");
        return "saved";
    }
}
