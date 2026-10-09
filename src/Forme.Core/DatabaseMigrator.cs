using Microsoft.Data.Sqlite;

namespace Forme.Core;

internal static class DatabaseMigrator
{
    public const int CurrentVersion=3;
    internal static void Upgrade(SqliteConnection db,string directory,Action<int>? beforeStep=null)
    {
        using var versionCommand=db.CreateCommand();versionCommand.CommandText="PRAGMA user_version";
        int version=Convert.ToInt32(versionCommand.ExecuteScalar());
        if(version>CurrentVersion)throw new InvalidDataException("数据来自较新版本，请使用相应版本打开。原数据未修改。");
        if(version==CurrentVersion)return;
        if(version>0)
        {
            var path=Path.Combine(directory,$"migration-v{version}.db");var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                using(var backup=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=temp,Pooling=false}.ToString()))
                {backup.Open();db.BackupDatabase(backup);}
                File.Move(temp,path,true);
            }
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
        using var tx=db.BeginTransaction();
        while(version<CurrentVersion)
        {
            beforeStep?.Invoke(version);
            using var step=db.CreateCommand();step.Transaction=tx;
            step.CommandText=version switch
            {
                0=>"CREATE TABLE settings(k TEXT PRIMARY KEY,v TEXT NOT NULL); CREATE TABLE moods(id TEXT PRIMARY KEY,mood TEXT,note TEXT,created TEXT,updated TEXT); CREATE TABLE sessions(id TEXT PRIMARY KEY,title TEXT,created TEXT,endpoint TEXT); CREATE TABLE messages(id TEXT PRIMARY KEY,session TEXT,role TEXT,content TEXT,status TEXT,created TEXT); CREATE INDEX messages_session ON messages(session,created); CREATE TABLE focus(id TEXT PRIMARY KEY,kind TEXT,title TEXT,started TEXT,target INTEGER,elapsed REAL,result TEXT); CREATE TABLE growth(id TEXT PRIMARY KEY,type TEXT,day TEXT);",
                // No column meanings change. These indexes support existing paged history queries.
                1=>"CREATE INDEX moods_created ON moods(created DESC); CREATE INDEX focus_started ON focus(started DESC);",
                2=>"CREATE TABLE game_progress(id INTEGER PRIMARY KEY CHECK(id=1),xp INTEGER NOT NULL DEFAULT 0,stars INTEGER NOT NULL DEFAULT 0); INSERT INTO game_progress(id) VALUES(1); CREATE TABLE game_inventory(item_id TEXT PRIMARY KEY,quantity INTEGER NOT NULL CHECK(quantity>=0)); INSERT INTO game_inventory VALUES('ball-yellow',1); CREATE TABLE game_tasks(day TEXT NOT NULL,task_id TEXT NOT NULL,completed INTEGER NOT NULL CHECK(completed IN(0,1)),PRIMARY KEY(day,task_id)); CREATE TABLE game_daily(day TEXT PRIMARY KEY,xp INTEGER NOT NULL DEFAULT 0); CREATE TABLE game_discoveries(item_id TEXT PRIMARY KEY,day TEXT NOT NULL); CREATE TABLE game_slots(slot_id TEXT PRIMARY KEY,item_id TEXT NOT NULL); CREATE TABLE game_achievements(id TEXT PRIMARY KEY,day TEXT NOT NULL);",
                _=>throw new InvalidDataException("缺少对应的数据升级步骤。")
            };
            step.ExecuteNonQuery();step.CommandText=$"PRAGMA user_version={++version}";step.ExecuteNonQuery();
        }
        tx.Commit();
    }
}
