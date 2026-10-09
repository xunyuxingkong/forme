using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Forme.Core;

public sealed partial class Store : IDisposable
{
    public const int Version = DatabaseMigrator.CurrentVersion;
    public const int ExportVersion = 2; // JSON compatibility evolves independently of SQLite indexes.
    private readonly SqliteConnection _db;
    public string DirectoryPath { get; }
    public string BackupPath => Path.Combine(DirectoryPath, "recovery.json");
    public static JsonSerializerOptions JsonOptions { get; } = new() { WriteIndented = true };

    public Store(string directory, bool readOnly=false)
    {
        DirectoryPath = Path.GetFullPath(directory);
        if(!readOnly)Directory.CreateDirectory(DirectoryPath);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(DirectoryPath, "forme.db"), Pooling = false, DefaultTimeout=3, Mode=readOnly?SqliteOpenMode.ReadOnly:SqliteOpenMode.ReadWriteCreate }.ToString());
        try
        {
            _db.Open();
            long version = Convert.ToInt64(Scalar("PRAGMA user_version"));
            if (version > Version) throw new InvalidDataException("数据来自较新版本，请使用相应版本打开。原数据未修改。");
            if(readOnly){if(version!=Version)throw new InvalidDataException("数据格式不支持只读导出。");return;}
            DatabaseMigrator.Upgrade(_db,DirectoryPath);
            Exec("PRAGMA journal_mode=DELETE; PRAGMA secure_delete=ON; PRAGMA busy_timeout=3000;");
            // Interrupted responses are not silently resumed or included as successful history.
            Exec("UPDATE messages SET status='stopped' WHERE status='streaming'");
        }
        catch { _db.Dispose(); throw; }
    }
    private SqliteCommand Command(string sql, SqliteTransaction? tx = null, params object?[] values)
    {
        var cmd = _db.CreateCommand(); cmd.CommandText = sql; cmd.Transaction = tx;
        for (int i = 0; i < values.Length; i++) cmd.Parameters.AddWithValue("$" + i, values[i] ?? DBNull.Value);
        return cmd;
    }
    private void Exec(string sql, SqliteTransaction? tx = null, params object?[] values) { using var c = Command(sql, tx, values); c.ExecuteNonQuery(); }
    private object? Scalar(string sql, params object?[] values) { using var c = Command(sql, null, values); return c.ExecuteScalar(); }
    private List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params object?[] values)
    {
        using var c = Command(sql, null, values); using var r = c.ExecuteReader(); var list = new List<T>();
        while (r.Read()) list.Add(map(r)); return list;
    }
    private static string Date(DateTimeOffset v) => v.ToString("O");
    private static DateTimeOffset ReadDate(SqliteDataReader r, int i) => DateTimeOffset.Parse(r.GetString(i));
    public Preferences LoadPreferences() => Get<Preferences>("preferences") ?? new();
    public T? Get<T>(string key) { var s = Scalar("SELECT v FROM settings WHERE k=$0", key) as string; return s is null ? default : JsonSerializer.Deserialize<T>(s); }
    public void Set<T>(string key, T value) => Exec("INSERT INTO settings VALUES($0,$1) ON CONFLICT(k) DO UPDATE SET v=excluded.v", null, key, JsonSerializer.Serialize(value));
    public void SavePreferences(Preferences p) { p.Validate(); Set("preferences", p); }
    public void SaveActivity(ActivitySnapshot? snapshot) { if (snapshot is null) Exec("DELETE FROM settings WHERE k='activity'"); else Set("activity", snapshot); }
    public List<MoodEntry> Moods(int offset = 0, int limit = 20) => Query("SELECT * FROM moods ORDER BY created DESC LIMIT $0 OFFSET $1", r => new MoodEntry(r.GetString(0),r.GetString(1),r.GetString(2),ReadDate(r,3),ReadDate(r,4)), limit, offset);
    public void SaveMood(MoodEntry m) { ValidateMood(m); Exec("INSERT INTO moods VALUES($0,$1,$2,$3,$4) ON CONFLICT(id) DO UPDATE SET mood=excluded.mood,note=excluded.note,updated=excluded.updated", null, m.Id,m.Mood,m.Note,Date(m.Created),Date(m.Updated)); }
    public void DeleteMood(string id) { Exec("DELETE FROM moods WHERE id=$0", null, id); RemoveBackup(); }
    public List<ChatSession> Sessions(int offset = 0, int limit = 20) => Query("SELECT * FROM sessions ORDER BY created DESC LIMIT $0 OFFSET $1", r => new ChatSession(r.GetString(0),r.GetString(1),ReadDate(r,2),r.GetString(3)),limit,offset);
    public ChatSession NewSession(string endpoint) { var s = new ChatSession(Guid.NewGuid().ToString("N"), "聊一会儿", DateTimeOffset.Now, endpoint); Exec("INSERT INTO sessions VALUES($0,$1,$2,$3)",null,s.Id,s.Title,Date(s.Created),s.Endpoint); return s; }
    public bool HasSession(string id) => Convert.ToInt64(Scalar("SELECT count(*) FROM sessions WHERE id=$0",id)) > 0;
    public void RenameSession(string id, string title) => Exec("UPDATE sessions SET title=$1 WHERE id=$0",null,id,title.Length > 24 ? title[..24] : title);
    public void RebindSession(string id, string endpoint) => Exec("UPDATE sessions SET endpoint=$1 WHERE id=$0",null,id,endpoint);
    public List<ChatMessage> Messages(string session, int offset = 0, int limit = 40) => Query("SELECT * FROM (SELECT *,rowid AS seq FROM messages WHERE session=$0 ORDER BY created DESC,rowid DESC LIMIT $1 OFFSET $2) ORDER BY created,seq", r => new ChatMessage(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),ReadDate(r,5)),session,limit,offset);
    public void SaveMessage(ChatMessage m) { if (!HasSession(m.SessionId)) return; Exec("INSERT INTO messages VALUES($0,$1,$2,$3,$4,$5) ON CONFLICT(id) DO UPDATE SET content=excluded.content,status=excluded.status",null,m.Id,m.SessionId,m.Role,m.Content,m.Status,Date(m.Created)); }
    public void DeleteSession(string id)
    {
        using var tx = _db.BeginTransaction(); Exec("DELETE FROM messages WHERE session=$0",tx,id); Exec("DELETE FROM sessions WHERE id=$0",tx,id); tx.Commit(); RemoveBackup();
    }
    public List<FocusEntry> Focus(int offset = 0, int limit = 20) => Query("SELECT * FROM focus ORDER BY started DESC LIMIT $0 OFFSET $1",r => new FocusEntry(r.GetString(0),r.GetString(1),r.GetString(2),ReadDate(r,3),r.GetInt32(4),r.GetDouble(5),r.GetString(6)),limit,offset);
    public void FinishFocus(FocusEntry f)
    {
        using var tx = _db.BeginTransaction();
        Exec("INSERT OR IGNORE INTO focus VALUES($0,$1,$2,$3,$4,$5,$6)",tx,f.Id,f.Kind,f.Title,Date(f.Started),f.TargetSeconds,f.ElapsedSeconds,f.Result);
        if (f.Kind == "focus" && f.ElapsedSeconds >= 300)
        {
            if(f.Result=="completed")Grant("unlock:star","unlock", "",tx);
            CompleteGameTask(tx,DateOnly.FromDateTime(DateTime.Now),"focus");
        }
        Exec("DELETE FROM settings WHERE k='activity'",tx); tx.Commit();
    }
    private void Grant(string id, string type, string day, SqliteTransaction? tx = null) => Exec("INSERT OR IGNORE INTO growth VALUES($0,$1,$2)",tx,id,type,day);
    public List<GrowthEvent> Growth() => Query("SELECT * FROM growth", r => new GrowthEvent(r.GetString(0),r.GetString(1),r.GetString(2)));
    public bool Unlocked(string name) => name == "none" || Convert.ToInt64(Scalar("SELECT count(*) FROM growth WHERE id=$0","unlock:"+name)) > 0;
    public void CompleteRelaxation()
    {
        using var tx=_db.BeginTransaction();Grant("unlock:cloud","unlock", "",tx);CompleteGameTask(tx,DateOnly.FromDateTime(DateTime.Now),"relax");tx.Commit();
    }
    public bool Water(DateOnly day)
    {
        using var tx = _db.BeginTransaction(); var id = "water:" + day.ToString("yyyy-MM-dd");
        using var c = Command("SELECT count(*) FROM growth WHERE id=$0",tx,id); if (Convert.ToInt64(c.ExecuteScalar()) != 0) return false;
        Grant(id,"water",day.ToString("yyyy-MM-dd"),tx);
        using var count = Command("SELECT count(*) FROM growth WHERE type='water'",tx);
        if (Convert.ToInt64(count.ExecuteScalar()) >= 5) Grant("unlock:flower","unlock","",tx);
        tx.Commit(); return true;
    }
    public int PlantPoints => Convert.ToInt32(Scalar("SELECT count(*) FROM growth WHERE type='water'"));
    public ExportDocument Export(bool chat, bool moods, bool focus, bool room)
    {
        // Keep all selected categories in one snapshot while other connections save.
        Exec("BEGIN DEFERRED TRANSACTION");
        try
        {
            var p = LoadPreferences();var sessions=chat?Sessions(0,int.MaxValue):null;
            var document=new ExportDocument
            {
                Sessions = sessions,
                Messages = sessions?.SelectMany(s=>Messages(s.Id,0,int.MaxValue)).ToList(),
                Moods = moods ? Moods(0,int.MaxValue) : null, Focus = focus ? Focus(0,int.MaxValue) : null,
                Room = room ? new RoomExport(p.PetName,p.Theme,p.Rug,p.Ornament,Growth()){Game=GameExportData()} : null
            };
            Exec("COMMIT");return document;
        }
        catch{Exec("ROLLBACK");throw;}
    }
    private static void ValidateMood(MoodEntry m) { if (string.IsNullOrWhiteSpace(m.Id) || m.Mood.Length > 30 || m.Note.Length > 1000) throw new InvalidDataException("心情记录格式错误。"); }
    public static void ValidateExport(ExportDocument d)
    {
        if (d.SchemaVersion is not (1 or ExportVersion)) throw new InvalidDataException("不支持的导出版本，原数据未修改。");
        if ((d.Sessions is null) != (d.Messages is null)) throw new InvalidDataException("聊天数据不完整。");
        if (d.Sessions is null && d.Moods is null && d.Focus is null && d.Room is null) throw new InvalidDataException("文件没有可导入的数据。");
        void Unique(IEnumerable<string> ids) { var all = ids.ToList(); if (all.Any(string.IsNullOrWhiteSpace) || all.Distinct().Count()!=all.Count) throw new InvalidDataException("记录 ID 无效或重复。"); }
        if (d.Moods is not null) { Unique(d.Moods.Select(x=>x.Id)); foreach(var m in d.Moods) ValidateMood(m); }
        if (d.Sessions is not null)
        {
            Unique(d.Sessions.Select(x=>x.Id)); Unique(d.Messages!.Select(x=>x.Id));
            foreach (var s in d.Sessions) { AiClient.ValidateEndpoint(s.Endpoint); if (s.Title.Length > 120) throw new InvalidDataException("会话标题过长。"); }
            var ids = d.Sessions.Select(x=>x.Id).ToHashSet();
            if (d.Messages!.Any(x=>!ids.Contains(x.SessionId) || x.Role is not ("user" or "assistant") || x.Status is not ("complete" or "stopped" or "error" or "streaming") || x.Content.Length>32000)) throw new InvalidDataException("消息记录无效。");
        }
        if (d.Focus is not null)
        {
            Unique(d.Focus.Select(x=>x.Id));
            if(d.Focus.Any(x=>x.Kind is not ("focus" or "rest") || x.Title.Length>120 || x.TargetSeconds is <60 or >10800 || !double.IsFinite(x.ElapsedSeconds) || x.ElapsedSeconds<0 || x.ElapsedSeconds>x.TargetSeconds || x.Result is not ("completed" or "ended"))) throw new InvalidDataException("专注记录无效。");
        }
        if(d.Room is not null)
        {
            new Preferences {PetName=d.Room.PetName,Theme=d.Room.Theme,Rug=d.Room.Rug,Ornament=d.Room.Ornament}.Validate(); Unique(d.Room.Events.Select(x=>x.Id));if(d.Room.Game is {} game)ValidateGameExport(game);
            if(d.Room.Events.Any(x=>x.Type=="water" ? !DateOnly.TryParseExact(x.Day,"yyyy-MM-dd",out _) || x.Id!="water:"+x.Day : x.Type!="unlock" || !new[]{"unlock:star","unlock:cloud","unlock:flower"}.Contains(x.Id))) throw new InvalidDataException("成长记录无效。");
        }
    }
    public void Import(ExportDocument d)
    {
        ValidateExport(d);
        var existingPreferences=LoadPreferences();
        // Backup is written before any mutation. Transaction prevents a half-import.
        ExportFile(BackupPath,true,true,true,true);
        using var tx = _db.BeginTransaction();
        if(d.Sessions is not null)
        {
            Exec("DELETE FROM messages; DELETE FROM sessions;",tx);
            foreach(var s in d.Sessions) Exec("INSERT INTO sessions VALUES($0,$1,$2,$3)",tx,s.Id,s.Title,Date(s.Created),s.Endpoint);
            foreach(var m in d.Messages!) Exec("INSERT INTO messages VALUES($0,$1,$2,$3,$4,$5)",tx,m.Id,m.SessionId,m.Role,m.Content,m.Status=="streaming"?"stopped":m.Status,Date(m.Created));
        }
        if(d.Moods is not null) { Exec("DELETE FROM moods",tx); foreach(var m in d.Moods) Exec("INSERT INTO moods VALUES($0,$1,$2,$3,$4)",tx,m.Id,m.Mood,m.Note,Date(m.Created),Date(m.Updated)); }
        if(d.Focus is not null) { Exec("DELETE FROM focus",tx); foreach(var f in d.Focus) Exec("INSERT INTO focus VALUES($0,$1,$2,$3,$4,$5,$6)",tx,f.Id,f.Kind,f.Title,Date(f.Started),f.TargetSeconds,f.ElapsedSeconds,f.Result); }
        if(d.Room is not null)
        {
            var p=existingPreferences with {}; p.PetName=d.Room.PetName; p.Theme=d.Room.Theme; p.Rug=d.Room.Rug; p.Ornament=d.Room.Ornament;
            Exec("INSERT INTO settings VALUES('preferences',$0) ON CONFLICT(k) DO UPDATE SET v=excluded.v",tx,JsonSerializer.Serialize(p));
            Exec("DELETE FROM growth",tx); foreach(var e in d.Room.Events) Grant(e.Id,e.Type,e.Day,tx);
            if(d.Room.Game is {} game)ReplaceGame(game,tx);
        }
        tx.Commit();
    }
    public void ClearCategory(string category)
    {
        if(category is not ("chat" or "moods" or "focus")) throw new ArgumentException("类别无效。");
        Exec(category=="chat"?"DELETE FROM messages; DELETE FROM sessions;":$"DELETE FROM {category}"); RemoveBackup();
    }
    public void ResetAll() { Exec("DELETE FROM messages; DELETE FROM sessions; DELETE FROM moods; DELETE FROM focus; DELETE FROM growth; DELETE FROM settings; DELETE FROM game_progress; INSERT INTO game_progress(id,xp,stars) VALUES(1,0,0); DELETE FROM game_inventory; INSERT INTO game_inventory VALUES('ball-yellow',1); DELETE FROM game_tasks; DELETE FROM game_daily; DELETE FROM game_discoveries; DELETE FROM game_slots; DELETE FROM game_achievements;"); RemoveBackup(); Exec("VACUUM"); }
    public void RemoveBackup()
    {
        if(File.Exists(BackupPath))File.Delete(BackupPath);
        if(File.Exists(BackupPath+".tmp"))File.Delete(BackupPath+".tmp");
        foreach(var path in Directory.EnumerateFiles(DirectoryPath,"migration-v*.db*"))File.Delete(path);
    }
    public void Dispose() => _db.Dispose();
}
