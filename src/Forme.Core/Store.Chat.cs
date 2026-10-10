namespace Forme.Core;

public sealed partial class Store
{
    public List<ChatMessage> ContextMessages(string session,int limit=60)=>Query("SELECT * FROM (SELECT *,rowid AS seq FROM messages WHERE session=$0 AND status!='local' ORDER BY created DESC,rowid DESC LIMIT $1) ORDER BY created,seq",r=>new ChatMessage(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),ReadDate(r,5)),session,Math.Clamp(limit,1,60));
    public List<ChatMemory> Memories()=>Query("SELECT * FROM chat_memories ORDER BY updated DESC,id",r=>new ChatMemory(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetInt32(4)==1,ReadDate(r,5),ReadDate(r,6)));
    public static void ValidateMemory(ChatMemory memory)
    {
        if(string.IsNullOrWhiteSpace(memory.Id)||memory.Id.Length>64||string.IsNullOrWhiteSpace(memory.Title)||memory.Title.Length>60||string.IsNullOrWhiteSpace(memory.Content)||memory.Content.Length>400||!ValidProvider(memory.Provider))
            throw new InvalidDataException("记忆需要标题和内容：标题最多60字、内容最多400字，且必须绑定有效服务商。");
    }
    private static bool ValidProvider(string provider)
    {
        if(string.IsNullOrEmpty(provider))return false;
        if(provider is "deepseek" or "siliconflow")return true;
        if(!provider.StartsWith("custom:",StringComparison.Ordinal))return false;
        try{return AiClient.ProviderIdentity(provider[7..])==provider;}catch(InvalidDataException){return false;}
    }
    public void SaveMemory(ChatMemory memory)
    {
        ValidateMemory(memory);
        if(Convert.ToInt64(Scalar("SELECT count(*) FROM chat_memories WHERE id<>$0",memory.Id))>=30)throw new InvalidDataException("最多保留30条记忆，请先整理已有内容。");
        Exec("INSERT INTO chat_memories VALUES($0,$1,$2,$3,$4,$5,$6) ON CONFLICT(id) DO UPDATE SET title=excluded.title,content=excluded.content,provider=excluded.provider,enabled=excluded.enabled,updated=excluded.updated",null,memory.Id,memory.Title,memory.Content,memory.Provider,memory.Enabled?1:0,Date(memory.Created),Date(memory.Updated));
    }
    public void DeleteMemory(string id){Exec("DELETE FROM chat_memories WHERE id=$0",null,id);RemoveBackup();}
    public ChatNote? Note(string session)=>Query("SELECT * FROM chat_notes WHERE session=$0",r=>new ChatNote(r.GetString(0),r.GetString(1),r.GetString(2)),session).FirstOrDefault();
    public static void ValidateNote(ChatNote note)
    {
        if(string.IsNullOrWhiteSpace(note.SessionId)||note.Content is null||note.Content.Length>800||!ValidProvider(note.Provider))throw new InvalidDataException("会话摘要最多800字，且必须绑定有效服务商。");
    }
    public void SaveNote(ChatNote note)
    {
        ValidateNote(note);if(!HasSession(note.SessionId))throw new InvalidDataException("会话已不存在。");
        if(string.IsNullOrWhiteSpace(note.Content)){Exec("DELETE FROM chat_notes WHERE session=$0",null,note.SessionId);RemoveBackup();return;}
        Exec("INSERT INTO chat_notes VALUES($0,$1,$2) ON CONFLICT(session) DO UPDATE SET content=excluded.content,provider=excluded.provider",null,note.SessionId,note.Content,note.Provider);
    }
    public List<ChatNote> Notes()=>Query("SELECT * FROM chat_notes",r=>new ChatNote(r.GetString(0),r.GetString(1),r.GetString(2)));
    public List<ChatMessage> SearchMessages(string session,string search,int offset=0,int limit=20)
    {
        string query=search.Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_");
        return Query("SELECT * FROM (SELECT *,rowid AS seq FROM messages WHERE session=$0 AND content LIKE $1 ESCAPE '\\' ORDER BY created DESC,rowid DESC LIMIT $2 OFFSET $3) ORDER BY created,seq",r=>new ChatMessage(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),ReadDate(r,5)),session,"%"+query+"%",Math.Clamp(limit,1,100),Math.Max(0,offset));
    }
    // A user turn and its recoverable response placeholder are saved together.
    public void BeginChatTurn(ChatMessage? user,ChatMessage response)
    {
        if(!HasSession(response.SessionId))throw new InvalidDataException("会话已不存在。");
        using var tx=_db.BeginTransaction();
        if(user is not null)
        {
            using var count=Command("SELECT count(*) FROM messages WHERE session=$0 AND role='user'",tx,user.SessionId);
            bool first=Convert.ToInt64(count.ExecuteScalar())==0;
            Exec("INSERT INTO messages VALUES($0,$1,$2,$3,$4,$5)",tx,user.Id,user.SessionId,user.Role,user.Content,user.Status,Date(user.Created));
            if(first)Exec("UPDATE sessions SET title=$1 WHERE id=$0",tx,user.SessionId,user.Content.Length>24?user.Content[..24]:user.Content);
        }
        Exec("INSERT INTO messages VALUES($0,$1,$2,$3,$4,$5)",tx,response.Id,response.SessionId,response.Role,response.Content,response.Status,Date(response.Created));tx.Commit();
    }
}
