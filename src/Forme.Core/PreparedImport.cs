using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Forme.Core;

public sealed class PreparedImport : IDisposable
{
    public const long MaxFileBytes=512L*1024*1024;
    public const int MaxRecords=1_000_000;
    private readonly string _directory;
    private readonly FileStream _lease;
    private static string TemporaryRoot=>Path.Combine(Path.GetTempPath(),"Forme-transfer");
    internal string DatabasePath=>Path.Combine(_directory,"forme.db");
    internal string DirectoryPath=>_directory;
    internal static PreparedImport Empty()=>new(Path.Combine(TemporaryRoot,Guid.NewGuid().ToString("N")));
    private readonly HashSet<string> _categories=new(StringComparer.Ordinal);
    public bool HasChat=>_categories.Contains("Sessions");
    public bool HasMoods=>_categories.Contains("Moods");
    public bool HasFocus=>_categories.Contains("Focus");
    public RoomExport? Room {get;private set;}
    public int Sessions {get;private set;}
    public int Messages {get;private set;}
    public int Moods {get;private set;}
    public int Focus {get;private set;}
    private int _records;
    private bool _disposed;
    private PreparedImport(string directory)
    {
        _directory=directory;Directory.CreateDirectory(directory);
        _lease=new FileStream(Path.Combine(directory,".in-use"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
    }
    public static void CleanupAbandoned()
    {
        if(!Directory.Exists(TemporaryRoot))return;
        foreach(var directory in Directory.EnumerateDirectories(TemporaryRoot))
        {
            if(!Guid.TryParseExact(Path.GetFileName(directory),"N",out _))continue;
            try
            {
                using(var lease=new FileStream(Path.Combine(directory,".in-use"),FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}
                Directory.Delete(directory,true);
            }
            catch(IOException){} // An active process owns this staging directory.
            catch(UnauthorizedAccessException){}
        }
    }
    internal void EnsureAlive(){ObjectDisposedException.ThrowIf(_disposed,this);}
    public static PreparedImport Read(string path,CancellationToken cancellation=default)
    {
        var plan=new PreparedImport(Path.Combine(TemporaryRoot,Guid.NewGuid().ToString("N")));
        try
        {
            using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,8192);
            if(input.Length>MaxFileBytes)throw new InvalidDataException("导入超过 512MB 安全上限。");
            using var staging=new Store(plan._directory);
            using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=plan.DatabasePath,Pooling=false}.ToString());db.Open();
            using var tx=db.BeginTransaction();
            void Insert(string sql,params object?[] values)
            {
                cancellation.ThrowIfCancellationRequested();if(++plan._records>MaxRecords)throw new InvalidDataException("导入超过一百万条安全上限。");
                using var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;
                for(int n=0;n<values.Length;n++)cmd.Parameters.AddWithValue("$"+n,values[n]??DBNull.Value);
                cmd.ExecuteNonQuery();
            }
            var json=new JsonStreamReader(input,cancellation);json.Read(JsonTokenType.StartObject);var names=new HashSet<string>();int version=0;
            while(json.Peek()!=JsonTokenType.EndObject)
            {
                string name=json.Read(JsonTokenType.PropertyName)!;
                if(!names.Add(name))throw new InvalidDataException("重复的数据类别。");
                if(name=="SchemaVersion"){version=json.Value<int>();continue;}
                if(name=="ExportedAt"){json.Value<DateTimeOffset>();continue;}
                if(name is not ("Sessions" or "Messages" or "Moods" or "Focus" or "Room"))throw new InvalidDataException("未知的数据类别。");
                if(json.Peek()==JsonTokenType.Null){json.Read(JsonTokenType.Null);continue;}
                plan._categories.Add(name);
                if(name=="Room")
                {
                    json.Read(JsonTokenType.StartObject);var fields=new Dictionary<string,string>();bool events=false;
                    while(json.Peek()!=JsonTokenType.EndObject)
                    {
                        string field=json.Read(JsonTokenType.PropertyName)!;
                        if(field=="Events")
                        {
                            if(events)throw new InvalidDataException("重复的成长记录。");events=true;json.Read(JsonTokenType.StartArray);
                            while(json.Peek()!=JsonTokenType.EndArray)
                            {
                                var e=json.Value<GrowthEvent>();Store.ValidateExport(new(){Room=new("pet","day","cream","none",[e])});
                                Insert("INSERT INTO growth VALUES($0,$1,$2)",e.Id,e.Type,e.Day);
                            }
                            json.Read(JsonTokenType.EndArray);
                        }
                        else if(field is "PetName" or "Theme" or "Rug" or "Ornament")
                        {if(!fields.TryAdd(field,json.Value<string>()))throw new InvalidDataException("重复的房间字段。");}
                        else throw new InvalidDataException("未知的房间字段。");
                    }
                    json.Read(JsonTokenType.EndObject);
                    if(!events||fields.Count!=4)throw new InvalidDataException("房间数据不完整。");
                    plan.Room=new(fields["PetName"],fields["Theme"],fields["Rug"],fields["Ornament"],[]);
                    Store.ValidateExport(new(){Room=plan.Room});continue;
                }
                json.Read(JsonTokenType.StartArray);
                while(json.Peek()!=JsonTokenType.EndArray)
                {
                    switch(name)
                    {
                        case "Sessions":
                            var s=json.Value<ChatSession>();Store.ValidateExport(new(){Sessions=[s],Messages=[]});
                            Insert("INSERT INTO sessions VALUES($0,$1,$2,$3)",s.Id,s.Title,s.Created.ToString("O"),s.Endpoint);plan.Sessions++;break;
                        case "Messages":
                            var m=json.Value<ChatMessage>();
                            if(m is null||string.IsNullOrWhiteSpace(m.Id)||string.IsNullOrWhiteSpace(m.SessionId)||m.Content is null||m.Content.Length>32000||m.Role is not ("user" or "assistant")||m.Status is not ("complete" or "stopped" or "error" or "streaming"))throw new InvalidDataException("消息记录无效。");
                            Insert("INSERT INTO messages VALUES($0,$1,$2,$3,$4,$5)",m.Id,m.SessionId,m.Role,m.Content,m.Status=="streaming"?"stopped":m.Status,m.Created.ToString("O"));plan.Messages++;break;
                        case "Moods":
                            var mood=json.Value<MoodEntry>();Store.ValidateExport(new(){Moods=[mood]});
                            Insert("INSERT INTO moods VALUES($0,$1,$2,$3,$4)",mood.Id,mood.Mood,mood.Note,mood.Created.ToString("O"),mood.Updated.ToString("O"));plan.Moods++;break;
                        case "Focus":
                            var f=json.Value<FocusEntry>();Store.ValidateExport(new(){Focus=[f]});
                            Insert("INSERT INTO focus VALUES($0,$1,$2,$3,$4,$5,$6)",f.Id,f.Kind,f.Title,f.Started.ToString("O"),f.TargetSeconds,f.ElapsedSeconds,f.Result);plan.Focus++;break;
                    }
                }
                json.Read(JsonTokenType.EndArray);
            }
            json.Read(JsonTokenType.EndObject);
            if(json.Peek()!=JsonTokenType.None||version!=Store.ExportVersion||plan._categories.Count==0||plan.HasChat!=plan._categories.Contains("Messages"))throw new InvalidDataException("导入文件结构或版本无效。");
            using var orphan=db.CreateCommand();orphan.Transaction=tx;orphan.CommandText="SELECT count(*) FROM messages WHERE session NOT IN (SELECT id FROM sessions)";
            if(Convert.ToInt64(orphan.ExecuteScalar())!=0)throw new InvalidDataException("消息所属会话缺失。");
            tx.Commit();return plan;
        }
        catch(Exception ex) when(ex is JsonException or NullReferenceException or SqliteException)
        {plan.Dispose();throw new InvalidDataException("导入内容无效、字段缺失或记录重复。原数据未修改。",ex);}
        catch{plan.Dispose();throw;}
    }
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;_lease.Dispose();
        try{if(Directory.Exists(_directory))Directory.Delete(_directory,true);}
        catch(IOException){} // Clean up abandoned files on the next startup.
        catch(UnauthorizedAccessException){}
    }
}
