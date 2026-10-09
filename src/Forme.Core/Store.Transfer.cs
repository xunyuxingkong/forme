using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Forme.Core;

public sealed partial class Store
{
    public static PreparedImport PrepareImport(string path,CancellationToken cancellation=default)=>PreparedImport.Read(path,cancellation);
    private IEnumerable<T> Enumerate<T>(string sql,Func<SqliteDataReader,T> map)
    {using var cmd=Command(sql);using var reader=cmd.ExecuteReader();while(reader.Read())yield return map(reader);}
    public void ExportFile(string path,bool chat,bool moods,bool focus,bool room,CancellationToken cancellation=default)
    {
        if(!(chat||moods||focus||room))throw new InvalidDataException("至少选择一类数据。");
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536))
            using(var bounded=new BoundedOutput(file,cancellation))
            using(var writer=new Utf8JsonWriter(bounded,new(){Indented=true}))
            {
                Exec("BEGIN DEFERRED TRANSACTION");
                try
                {
                    int count=0;
                    void Array<T>(string name,bool selected,IEnumerable<T> records)
                    {
                        writer.WritePropertyName(name);if(!selected){writer.WriteNullValue();return;}
                        writer.WriteStartArray();
                        foreach(var item in records)
                        {
                            cancellation.ThrowIfCancellationRequested();if(++count>PreparedImport.MaxRecords)throw new InvalidDataException("所选数据超过一百万条可恢复上限，请分类别导出。");
                            JsonSerializer.Serialize(writer,item);if(count%128==0)writer.Flush();
                        }
                        writer.WriteEndArray();
                    }
                    writer.WriteStartObject();writer.WriteNumber("SchemaVersion",ExportVersion);writer.WriteString("ExportedAt",DateTimeOffset.Now);
                    Array("Sessions",chat,Enumerate("SELECT * FROM sessions ORDER BY created",r=>new ChatSession(r.GetString(0),r.GetString(1),ReadDate(r,2),r.GetString(3))));
                    Array("Messages",chat,Enumerate("SELECT * FROM messages ORDER BY created,rowid",r=>new ChatMessage(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),ReadDate(r,5))));
                    Array("Moods",moods,Enumerate("SELECT * FROM moods ORDER BY created",r=>new MoodEntry(r.GetString(0),r.GetString(1),r.GetString(2),ReadDate(r,3),ReadDate(r,4))));
                    Array("Focus",focus,Enumerate("SELECT * FROM focus ORDER BY started",r=>new FocusEntry(r.GetString(0),r.GetString(1),r.GetString(2),ReadDate(r,3),r.GetInt32(4),r.GetDouble(5),r.GetString(6))));
                    writer.WritePropertyName("Room");
                    if(room)
                    {
                        var p=LoadPreferences();writer.WriteStartObject();writer.WriteString("PetName",p.PetName);writer.WriteString("Theme",p.Theme);writer.WriteString("Rug",p.Rug);writer.WriteString("Ornament",p.Ornament);
                        Array("Events",true,Enumerate("SELECT * FROM growth",r=>new GrowthEvent(r.GetString(0),r.GetString(1),r.GetString(2))));writer.WriteEndObject();
                    }
                    else writer.WriteNullValue();
                    writer.WriteEndObject();writer.Flush();Exec("COMMIT");
                }
                catch{Exec("ROLLBACK");throw;}
            }
            cancellation.ThrowIfCancellationRequested();File.Move(temp,path,true);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public void ApplyImport(PreparedImport plan)
    {
        plan.EnsureAlive();var prefs=LoadPreferences();
        ExportFile(BackupPath,true,true,true,true);
        Exec("ATTACH DATABASE $0 AS staged",null,plan.DatabasePath);
        try
        {
            using var tx=_db.BeginTransaction();
            if(plan.HasChat)Exec("DELETE FROM messages; DELETE FROM sessions; INSERT INTO sessions SELECT * FROM staged.sessions; INSERT INTO messages SELECT * FROM staged.messages;",tx);
            if(plan.HasMoods)Exec("DELETE FROM moods; INSERT INTO moods SELECT * FROM staged.moods;",tx);
            if(plan.HasFocus)Exec("DELETE FROM focus; INSERT INTO focus SELECT * FROM staged.focus;",tx);
            if(plan.Room is {} room)
            {
                prefs=prefs with{PetName=room.PetName,Theme=room.Theme,Rug=room.Rug,Ornament=room.Ornament};
                Exec("INSERT INTO settings VALUES('preferences',$0) ON CONFLICT(k) DO UPDATE SET v=excluded.v",tx,JsonSerializer.Serialize(prefs));
                Exec("DELETE FROM growth; INSERT INTO growth SELECT * FROM staged.growth;",tx);
            }
            tx.Commit();
        }
        finally{Exec("DETACH DATABASE staged");}
    }
    private sealed class BoundedOutput(Stream output,CancellationToken cancellation):Stream
    {
        private long _written;
        public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;
        public override long Length=>_written;public override long Position{get=>_written;set=>throw new NotSupportedException();}
        public override void Write(byte[] buffer,int offset,int count)=>Write(buffer.AsSpan(offset,count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            cancellation.ThrowIfCancellationRequested();if(_written+buffer.Length>PreparedImport.MaxFileBytes)throw new InvalidDataException("导出超过 512MB 可恢复上限，请分类别导出。目标文件未覆盖。");
            output.Write(buffer);_written+=buffer.Length;
        }
        public override void Flush()=>output.Flush();public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();
    }
}
