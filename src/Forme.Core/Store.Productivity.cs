using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Forme.Core;

public sealed partial class Store
{
    public void MarkLocalProductivityTurn(string userMessageId,string assistantMessageId)=>Exec("UPDATE messages SET status='local' WHERE id IN($0,$1) AND role IN('user','assistant')",null,userMessageId,assistantMessageId);
    private static string Tags(IReadOnlyList<string> tags)=>JsonSerializer.Serialize(tags);
    private static IReadOnlyList<string> ReadTags(string value)=>JsonSerializer.Deserialize<string[]>(value)??[];
    private static DateTimeOffset? ReadOptionalDate(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:ReadDate(r,i);

    public static void ValidateUserNote(UserNote note)
    {
        if(string.IsNullOrWhiteSpace(note.Id)||note.Id.Length>64||string.IsNullOrWhiteSpace(note.Title)||note.Title.Length>120||note.Content is null||note.Content.Length>10000||note.Source is not ("manual" or "ai" or "chat")||note.Tags.Count>20||note.Tags.Any(x=>string.IsNullOrWhiteSpace(x)||x.Length>32))
            throw new InvalidDataException("记事标题最多120字、内容最多10000字，每条最多20个有效标签。 ");
    }
    public List<UserNote> UserNotes(bool includeArchived=false,string? search=null,int offset=0,int limit=50)
    {
        string term="%"+(search??"").Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")+"%";
        return Query("SELECT * FROM user_notes WHERE ($0=1 OR archived=0) AND ($1='' OR title LIKE $2 ESCAPE '\\' OR content LIKE $2 ESCAPE '\\' OR tags LIKE $2 ESCAPE '\\') ORDER BY pinned DESC,updated DESC LIMIT $3 OFFSET $4",r=>new UserNote(r.GetString(0),r.GetString(1),r.GetString(2),ReadTags(r.GetString(3)),r.GetInt32(4)==1,r.GetInt32(5)==1,r.GetString(6),ReadDate(r,7),ReadDate(r,8)),includeArchived?1:0,search??"",term,Math.Clamp(limit,1,100_000),Math.Max(0,offset));
    }
    public void SaveUserNote(UserNote note)
    {
        ValidateUserNote(note);
        Exec("INSERT INTO user_notes VALUES($0,$1,$2,$3,$4,$5,$6,$7,$8) ON CONFLICT(id) DO UPDATE SET title=excluded.title,content=excluded.content,tags=excluded.tags,pinned=excluded.pinned,archived=excluded.archived,source=excluded.source,updated=excluded.updated",null,note.Id,note.Title,note.Content,Tags(note.Tags),note.Pinned?1:0,note.Archived?1:0,note.Source,Date(note.Created),Date(note.Updated));
    }
    public void DeleteUserNote(string id){Exec("DELETE FROM user_notes WHERE id=$0",null,id);RemoveBackup();}

    public static void ValidateUserTask(UserTask task)
    {
        if(string.IsNullOrWhiteSpace(task.Id)||task.Id.Length>64||string.IsNullOrWhiteSpace(task.Title)||task.Title.Length>200||task.Description is null||task.Description.Length>2000||task.Status is not ("todo" or "doing" or "done" or "cancelled")||task.Priority is not ("normal" or "important" or "urgent")||task.Source is not ("manual" or "ai" or "chat")||task.Project.Length>100||task.Tags.Count>20||task.Tags.Any(x=>string.IsNullOrWhiteSpace(x)||x.Length>32)||task.SortOrder<0)
            throw new InvalidDataException("待办标题或字段无效。 ");
        if(task.RemindAt is not null&&task.DueAt is not null&&task.RemindAt>task.DueAt)throw new InvalidDataException("提醒时间不能晚于截止时间。 ");
    }
    public List<UserTask> UserTasks(string? status=null,DateOnly? dueDay=null,int offset=0,int limit=100)
    {
        string? day=dueDay?.ToString("yyyy-MM-dd");
        return Query("SELECT * FROM user_tasks WHERE ($0 IS NULL OR status=$0) AND ($1 IS NULL OR substr(due_at,1,10)=$1) ORDER BY CASE status WHEN 'doing' THEN 0 WHEN 'todo' THEN 1 ELSE 2 END,sort_order,COALESCE(due_at,'9999'),created DESC LIMIT $2 OFFSET $3",r=>new UserTask(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),ReadOptionalDate(r,5),ReadOptionalDate(r,6),ReadDate(r,7),ReadDate(r,8),ReadOptionalDate(r,9),r.GetString(10),r.GetString(11),ReadTags(r.GetString(12)),r.GetInt32(13)),status,day,Math.Clamp(limit,1,100_000),Math.Max(0,offset));
    }
    public void SaveUserTask(UserTask task)
    {
        ValidateUserTask(task);
        using var tx=_db.BeginTransaction();
        Exec("INSERT INTO user_tasks VALUES($0,$1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13) ON CONFLICT(id) DO UPDATE SET title=excluded.title,description=excluded.description,status=excluded.status,priority=excluded.priority,due_at=excluded.due_at,remind_at=excluded.remind_at,updated=excluded.updated,completed_at=excluded.completed_at,source=excluded.source,project=excluded.project,tags=excluded.tags,sort_order=excluded.sort_order",tx,task.Id,task.Title,task.Description,task.Status,task.Priority,task.DueAt is null?null:Date(task.DueAt.Value),task.RemindAt is null?null:Date(task.RemindAt.Value),Date(task.Created),Date(task.Updated),task.CompletedAt is null?null:Date(task.CompletedAt.Value),task.Source,task.Project,Tags(task.Tags),task.SortOrder);
        using var existing=Command("SELECT id,created FROM user_reminders WHERE task_id=$0 ORDER BY created LIMIT 1",tx,task.Id);using var reader=existing.ExecuteReader();
        string? reminderId=null;DateTimeOffset reminderCreated=task.Created;if(reader.Read()){reminderId=reader.GetString(0);reminderCreated=ReadDate(reader,1);}reader.Close();
        if(task.RemindAt is {} remindAt)
        {
            reminderId??=Guid.NewGuid().ToString("N");var reminder=new UserReminder(reminderId,task.Title,remindAt,task.Id,task.Status is "done" or "cancelled"?"dismissed":"pending","",reminderCreated,task.Updated,null);
            Exec("INSERT INTO user_reminders VALUES($0,$1,$2,$3,$4,$5,$6,$7,$8) ON CONFLICT(id) DO UPDATE SET title=excluded.title,due_at=excluded.due_at,status=excluded.status,updated=excluded.updated,snooze_until=NULL",tx,reminder.Id,reminder.Title,Date(reminder.DueAt),reminder.TaskId,reminder.Status,reminder.RepeatRule,Date(reminder.Created),Date(reminder.Updated),null);
            Exec("DELETE FROM user_reminders WHERE task_id=$0 AND id<>$1",tx,task.Id,reminderId);
        }
        else Exec("DELETE FROM user_reminders WHERE task_id=$0",tx,task.Id);
        tx.Commit();
    }
    public void SetUserTaskStatus(string id,string status,DateTimeOffset now)
    {
        if(status is not ("todo" or "doing" or "done" or "cancelled"))throw new InvalidDataException("待办状态无效。 ");
        using var tx=_db.BeginTransaction();Exec("UPDATE user_tasks SET status=$1,updated=$2,completed_at=$3 WHERE id=$0",tx,id,status,Date(now),status=="done"?Date(now):null);if(status is "done" or "cancelled")Exec("UPDATE user_reminders SET status='dismissed',snooze_until=NULL,updated=$1 WHERE task_id=$0 AND status='pending'",tx,id,Date(now));tx.Commit();RemoveBackup();
    }
    public void DeleteUserTask(string id){Exec("DELETE FROM user_reminders WHERE task_id=$0; DELETE FROM user_tasks WHERE id=$0",null,id);RemoveBackup();}

    public List<UserReminder> DueReminders(DateTimeOffset now,int limit=50)=>Query("SELECT * FROM user_reminders WHERE status='pending' AND COALESCE(snooze_until,due_at)<=$0 ORDER BY COALESCE(snooze_until,due_at) LIMIT $1",r=>new UserReminder(r.GetString(0),r.GetString(1),ReadDate(r,2),r.IsDBNull(3)?null:r.GetString(3),r.GetString(4),r.GetString(5),ReadDate(r,6),ReadDate(r,7),ReadOptionalDate(r,8)),Date(now),Math.Clamp(limit,1,100));
    public DateTimeOffset? NextReminderTime()=>Scalar("SELECT min(COALESCE(snooze_until,due_at)) FROM user_reminders WHERE status='pending'") is string value?DateTimeOffset.Parse(value):null;
    public void SaveUserReminder(UserReminder reminder)
    {
        ValidateUserReminder(reminder);InsertReminder(reminder);
    }
    public static void ValidateUserReminder(UserReminder reminder)
    {if(string.IsNullOrWhiteSpace(reminder.Id)||reminder.Id.Length>64||string.IsNullOrWhiteSpace(reminder.Title)||reminder.Title.Length>200||reminder.Status is not ("pending" or "dismissed" or "completed")||reminder.RepeatRule.Length>100)throw new InvalidDataException("提醒内容无效。 ");}
    private void InsertReminder(UserReminder r,Microsoft.Data.Sqlite.SqliteTransaction? tx=null)=>Exec("INSERT INTO user_reminders VALUES($0,$1,$2,$3,$4,$5,$6,$7,$8)",tx,r.Id,r.Title,Date(r.DueAt),r.TaskId,r.Status,r.RepeatRule,Date(r.Created),Date(r.Updated),r.SnoozeUntil is null?null:Date(r.SnoozeUntil.Value));
    private List<UserReminder> AllUserReminders()=>Query("SELECT * FROM user_reminders ORDER BY due_at",r=>new UserReminder(r.GetString(0),r.GetString(1),ReadDate(r,2),r.IsDBNull(3)?null:r.GetString(3),r.GetString(4),r.GetString(5),ReadDate(r,6),ReadDate(r,7),ReadOptionalDate(r,8)));
    public void SnoozeUserReminder(string id,DateTimeOffset until,DateTimeOffset now)=>Exec("UPDATE user_reminders SET snooze_until=$1,updated=$2 WHERE id=$0 AND status='pending'",null,id,Date(until),Date(now));
    public void DismissUserReminder(string id,DateTimeOffset now)=>Exec("UPDATE user_reminders SET status='dismissed',snooze_until=NULL,updated=$1 WHERE id=$0",null,id,Date(now));
    public void DeleteUserReminder(string id){Exec("DELETE FROM user_reminders WHERE id=$0",null,id);RemoveBackup();}
}
