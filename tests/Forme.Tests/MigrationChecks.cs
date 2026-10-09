using Forme.Core;
using Microsoft.Data.Sqlite;

internal static class MigrationChecks
{
    private static void Sql(string path,string sql)
    {using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString());db.Open();using var command=db.CreateCommand();command.CommandText=sql;command.ExecuteNonQuery();}
    private static long Scalar(string path,string sql)
    {using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString());db.Open();using var command=db.CreateCommand();command.CommandText=sql;return Convert.ToInt64(command.ExecuteScalar());}
    public static void Run(string root,Action<bool,string> check,Action<Action,string> throws)
    {
        string dir=Path.Combine(root,"migration");
        using(var seed=new Store(dir))
        {seed.SavePreferences(new(){PetName="历史伙伴"});seed.SaveMood(new("legacy","平静","旧数据",DateTimeOffset.Now,DateTimeOffset.Now));}
        string path=Path.Combine(dir,"forme.db");
        Sql(path,"DROP INDEX moods_created; DROP INDEX focus_started; DROP TABLE game_achievements; DROP TABLE game_slots; DROP TABLE game_discoveries; DROP TABLE game_daily; DROP TABLE game_tasks; DROP TABLE game_inventory; DROP TABLE game_progress; PRAGMA user_version=1;");
        using(var upgraded=new Store(dir))
        {
            check(upgraded.LoadPreferences().PetName=="历史伙伴"&&upgraded.Moods().Single().Note=="旧数据","v1 migration preserves records");
            check(Scalar(path,"PRAGMA user_version")==3,"v1 migrates through current schema");
            string backup=Path.Combine(dir,"migration-v1.db");
            check(File.Exists(backup)&&Scalar(backup,"PRAGMA user_version")==1,"upgrade backup remains readable v1");
            check(upgraded.Export(false,true,false,false).SchemaVersion==Store.ExportVersion,"database migration keeps JSON compatibility");
            upgraded.DeleteMood("legacy");check(!File.Exists(backup),"deletion clears migration recovery data");
        }
        using(var repeated=new Store(dir))check(Scalar(path,"PRAGMA user_version")==3,"migration is not repeated on current database");
        string failed=Path.Combine(root,"migration-failure");
        using(var seed=new Store(failed)){seed.SaveMood(new("safe","平静","kept",DateTimeOffset.Now,DateTimeOffset.Now));}
        path=Path.Combine(failed,"forme.db");Sql(path,"DROP INDEX moods_created; PRAGMA user_version=1;");
        throws(()=>{using var rejected=new Store(failed);},"migration SQL failure rejected");
        check(Scalar(path,"PRAGMA user_version")==1&&Scalar(path,"SELECT count(*) FROM moods")==1&&Scalar(path,"SELECT count(*) FROM sqlite_master WHERE name='moods_created'")==0,"failed migration rolls back DDL version and records");
        check(Scalar(Path.Combine(failed,"migration-v1.db"),"SELECT count(*) FROM moods")==1,"failed migration retains usable backup");
        string interrupted=Path.Combine(root,"migration-interrupted");Directory.CreateDirectory(interrupted);
        using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(interrupted,"forme.db"),Pooling=false}.ToString()))
        {
            db.Open();throws(()=>DatabaseMigrator.Upgrade(db,interrupted,version=>{if(version==1)throw new IOException("injected failure");}),"migration interruption rejected");
            check(Scalar(Path.Combine(interrupted,"forme.db"),"PRAGMA user_version")==0&&Scalar(Path.Combine(interrupted,"forme.db"),"SELECT count(*) FROM sqlite_master WHERE type='table'")==0,"entire migration chain rolls back");
        }
    }
}
