using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Forme.Core;

public sealed partial class Store
{
    private const string ProjectPrefix="project-run:";
    public IReadOnlyList<ProjectRun> Projects()=>Query("SELECT v FROM settings WHERE k LIKE 'project-run:%' ORDER BY k",r=>JsonSerializer.Deserialize<ProjectRun>(r.GetString(0))??throw new InvalidDataException("项目记录无效。")).OrderByDescending(x=>x.Updated).ToArray();
    public ProjectRun? Project(string id)=>ReadProject(id,null);
    private ProjectRun? ReadProject(string id,SqliteTransaction? tx)
    {using var command=Command("SELECT v FROM settings WHERE k=$0",tx,ProjectPrefix+id);return command.ExecuteScalar() is string value?JsonSerializer.Deserialize<ProjectRun>(value):null;}
    public void SaveProject(ProjectRun run){ProjectWork.Validate(run);if(Project(run.Id) is null&&Projects().Count>=20)throw new InvalidDataException("最多保留20个项目记录；请先删除不需要的记录（可保留笔记草稿）。");Set(ProjectPrefix+run.Id,run);}
    public void DeleteProject(string id){Exec("DELETE FROM settings WHERE k=$0",null,ProjectPrefix+id);RemoveBackup();}
    public UserNote? ProjectArtifact(ProjectRun run)=>run.ArtifactId is null?null:Query("SELECT * FROM user_notes WHERE id=$0",r=>new UserNote(r.GetString(0),r.GetString(1),r.GetString(2),ReadTags(r.GetString(3)),r.GetInt32(4)==1,r.GetInt32(5)==1,r.GetString(6),ReadDate(r,7),ReadDate(r,8)),run.ArtifactId).SingleOrDefault();
    public ProjectRun BeginProjectAttempt(string id,int revision,DateTimeOffset now,string? endpoint=null,string? model=null)
    {
        using var tx=_db.BeginTransaction();
        var run=ReadProject(id,tx)??throw new InvalidDataException("项目不存在。");
        if(run.Revision!=revision||run.State is ProjectRunState.Running or ProjectRunState.Cancelled or ProjectRunState.Closed||run.Requests>=2||run.ExpiresAt is {} expiry&&expiry<=now)throw new InvalidDataException("项目目标已改变、已结束或达到本次额度；请重新建立目标。");
        var next=run with{State=ProjectRunState.Running,Requests=run.Requests+1,ExpiresAt=run.ExpiresAt??now.AddSeconds(120),Updated=now,Notice=null,Endpoint=endpoint??run.Endpoint,Model=model??run.Model};ProjectWork.Validate(next);
        Exec("UPDATE settings SET v=$1 WHERE k=$0",tx,ProjectPrefix+id,JsonSerializer.Serialize(next));tx.Commit();return next;
    }
    public bool FinishProjectAttempt(ProjectRun issued,ProjectDecision? decision,ProjectRunState failure,DateTimeOffset now)
    {
        using var tx=_db.BeginTransaction();var current=ReadProject(issued.Id,tx);
        if(current is null||current.Revision!=issued.Revision||current.Requests!=issued.Requests||current.State!=ProjectRunState.Running){tx.Rollback();return false;}
        bool expired=current.ExpiresAt<=now;
        if(expired){decision=null;failure=ProjectRunState.Cancelled;}
        string? artifact=current.ArtifactId;var state=failure;
        if(decision?.Draft is {} draft)
        {
            artifact="project-"+current.Id+"-"+current.Revision+"-"+current.Requests;
            var note=new UserNote(artifact,draft.Title,ProjectWork.Render(draft,current.Sources),["项目草稿"],false,false,"ai",now,now);ValidateUserNote(note);
            Exec("INSERT INTO user_notes VALUES($0,$1,$2,$3,$4,$5,$6,$7,$8)",tx,note.Id,note.Title,note.Content,Tags(note.Tags),0,0,note.Source,Date(now),Date(now));state=ProjectRunState.DraftReady;
        }
        else if(decision?.Capability=="user.ask")state=ProjectRunState.WaitingUser;
        var next=current with{State=state,ArtifactId=artifact,TaskSuggestions=decision?.Draft?.Tasks??current.TaskSuggestions,PromotedTaskIndexes=decision?.Draft is null?current.PromotedTaskIndexes:[],Updated=now,Notice=decision?.Capability=="user.ask"?string.Join("；",decision.Questions)[..Math.Min(200,string.Join("；",decision.Questions).Length)]:null};
        Exec("UPDATE settings SET v=$1 WHERE k=$0",tx,ProjectPrefix+current.Id,JsonSerializer.Serialize(next));tx.Commit();return !expired;
    }
    public bool PromoteProjectTask(string projectId,int revision,string artifactId,int suggestionIndex,DateTimeOffset now)
    {
        if(!CapabilityCatalog.RequiresUserConfirmation(CapabilityDomain.Project,"task.promote"))throw new InvalidOperationException("项目待办投影必须由用户明确确认。");
        using var tx=_db.BeginTransaction();var run=ReadProject(projectId,tx);
        if(run is null||run.Revision!=revision||run.State!=ProjectRunState.DraftReady||run.ArtifactId!=artifactId||suggestionIndex<0||suggestionIndex>=run.TaskSuggestions.Count||run.PromotedTaskIndexes.Contains(suggestionIndex)){tx.Rollback();return false;}
        var suggestion=run.TaskSuggestions[suggestionIndex];string taskId=ProjectWork.SuggestedTaskId(artifactId,suggestionIndex);
        var task=new UserTask(taskId,suggestion.Title,suggestion.Description,"todo","normal",null,null,now,now,null,"ai","项目："+run.Goal[..Math.Min(80,run.Goal.Length)],["项目建议"],0);ValidateUserTask(task);
        Exec("INSERT OR IGNORE INTO user_tasks VALUES($0,$1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13)",tx,task.Id,task.Title,task.Description,task.Status,task.Priority,null,null,Date(now),Date(now),null,task.Source,task.Project,Tags(task.Tags),task.SortOrder);
        using(var changed=Command("SELECT changes()",tx))if(Convert.ToInt64(changed.ExecuteScalar())!=1){tx.Rollback();return false;}
        var next=run with{PromotedTaskIndexes=run.PromotedTaskIndexes.Append(suggestionIndex).Order().ToArray(),Updated=now};
        Exec("UPDATE settings SET v=$1 WHERE k=$0",tx,ProjectPrefix+run.Id,JsonSerializer.Serialize(next));tx.Commit();RemoveBackup();return true;
    }
    public void RecoverProjects()
    {
        foreach(var run in Projects().Where(x=>x.State is ProjectRunState.Running or ProjectRunState.Ready or ProjectRunState.DraftReady or ProjectRunState.WaitingUser))
            SaveProject(run with{State=ProjectRunState.WaitingForSources,Updated=DateTimeOffset.Now,Notice="资料未保留；重新选择并核对来源后才能继续。不会自动重发或重放写入。"});
    }
}
