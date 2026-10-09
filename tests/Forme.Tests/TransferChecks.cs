using Forme.Core;
using Microsoft.Data.Sqlite;
using System.Text;

internal static class TransferChecks
{
    public static void Run(string root,Action<bool,string> check,Action<Action,string> throws)
    {
        string abandoned=Path.Combine(Path.GetTempPath(),"Forme-transfer",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(abandoned);File.WriteAllText(Path.Combine(abandoned,".in-use"),"");
        PreparedImport.CleanupAbandoned();check(!Directory.Exists(abandoned),"abandoned staging is removed");
        string path=Path.Combine(root,"stream.json"),dir=Path.Combine(root,"transfer");
        using var store=new Store(dir);store.SaveMood(new("original","平静","原内容",DateTimeOffset.Now,DateTimeOffset.Now));
        var session=store.NewSession("https://api.deepseek.com");store.SaveMessage(new("unicode",session.Id,"assistant",new string('中',9000)+"🙂","complete",DateTimeOffset.Now));
        store.ExportFile(path,true,true,true,true);
        using(var plan=Store.PrepareImport(path))
        {PreparedImport.CleanupAbandoned();check(plan.Sessions==1&&plan.Messages==1&&plan.Moods==1,"streaming preview reports selected counts and active staging survives cleanup");store.ApplyImport(plan);}
        check(store.Messages(session.Id).Single().Content.EndsWith("🙂"),"chunked UTF8 import preserves Unicode");
        using(var backup=Store.PrepareImport(store.BackupPath))check(backup.Messages==1&&backup.Moods==1,"recovery uses same streaming parser");
        File.WriteAllText(path,"{\"SchemaVersion\":1,\"Moods\":[{\"Id\":\"same\",\"Mood\":\"ok\",\"Note\":\"\",\"Created\":\"2026-10-09T00:00:00Z\",\"Updated\":\"2026-10-09T00:00:00Z\"},{\"Id\":\"same\",\"Mood\":\"ok\",\"Note\":\"\",\"Created\":\"2026-10-09T00:00:00Z\",\"Updated\":\"2026-10-09T00:00:00Z\"}]}");
        throws(()=>{using var bad=Store.PrepareImport(path);},"duplicate streaming records rejected");
        check(store.Moods().Single().Id=="original","rejected staged import does not mutate user data");
        File.WriteAllText(path,"{\"SchemaVersion\":1,\"Moods\":[null]}");throws(()=>{using var bad=Store.PrepareImport(path);},"null records fail as input errors");
        File.WriteAllText(path,"{\"SchemaVersion\":1,\"Messages\":[],\"Sessions\":[]}");
        using(var plan=Store.PrepareImport(path)){store.ApplyImport(plan);check(store.Sessions().Count==0&&store.Moods().Count==1,"streaming partial import retains unrelated category");}
        File.WriteAllText(path,"{\"SchemaVersion\":1,\"Moods\":[],\"Moods\":[]}");throws(()=>{using var bad=Store.PrepareImport(path);},"duplicate category rejected");
        File.WriteAllText(path,"{\"SchemaVersion\":1,\"Moods\":[]}");using var cancel=new CancellationTokenSource();cancel.Cancel();
        throws(()=>{using var bad=Store.PrepareImport(path,cancel.Token);},"streaming import cancellation releases staging");
        File.WriteAllText(path,"keep target");throws(()=>store.ExportFile(path,true,true,true,true,cancel.Token),"cancelled export rejected");check(File.ReadAllText(path)=="keep target","cancelled export preserves target");
    }
    public static void Large(string root,Action<bool,string> check)
    {
        string dir=Path.Combine(root,"large-source"),path=Path.Combine(root,"large.json");
        using(var store=new Store(dir))
        {
            var session=store.NewSession("https://api.deepseek.com");
            using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(dir,"forme.db"),Pooling=false}.ToString());db.Open();using var tx=db.BeginTransaction();
            using var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT INTO messages VALUES($id,$session,'assistant',$content,'complete',$date)";
            cmd.Parameters.AddWithValue("$id","");cmd.Parameters.AddWithValue("$session",session.Id);cmd.Parameters.AddWithValue("$content",new string('x',31997)+"中🙂");cmd.Parameters.AddWithValue("$date",DateTimeOffset.Now.ToString("O"));
            for(int i=0;i<3500;i++){cmd.Parameters["$id"].Value="large-"+i;cmd.ExecuteNonQuery();}tx.Commit();
            store.ExportFile(path,true,false,false,false);
        }
        check(new FileInfo(path).Length>=100L*1024*1024,"100MB export created without materializing document");
        long peak=GC.GetTotalMemory(false);using var monitor=new Timer(_=>{long current=GC.GetTotalMemory(false);InterlockedExtensions.Max(ref peak,current);},null,0,10);
        using(var plan=Store.PrepareImport(path))
        {using var target=new Store(Path.Combine(root,"large-target"));target.ApplyImport(plan);using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(target.DirectoryPath,"forme.db"),Pooling=false}.ToString());db.Open();using var count=db.CreateCommand();count.CommandText="SELECT count(*) FROM messages";check(Convert.ToInt32(count.ExecuteScalar())==3500,"100MB export imports all records");}
        check(peak<100L*1024*1024,"large-file managed heap stays below 100MB during parse and import");
        File.WriteAllText(Path.Combine(root,"large-transfer-memory.txt"),$"Observed managed heap peak: {peak/1048576d:F1} MB\n");
    }
    public static async Task Concurrent(string root,Action<bool,string> check)
    {
        string dir=Path.Combine(root,"concurrent"),path=Path.Combine(root,"concurrent.json");
        using(var initial=new Store(dir))
        {
            var session=initial.NewSession("https://api.deepseek.com");
            for(int i=0;i<2000;i++)initial.SaveMessage(new("row-"+i,session.Id,"assistant",new string('x',4000),"complete",DateTimeOffset.Now));
        }
        using var stop=new CancellationTokenSource();int writes=0;
        var writer=Task.Run(()=>
        {
            using var store=new Store(dir);var session=store.Sessions().Single();
            while(!stop.IsCancellationRequested)
            {
                int n=Interlocked.Increment(ref writes);store.SaveMessage(new("new-"+n,session.Id,"user","concurrent","complete",DateTimeOffset.Now));Thread.Sleep(1);
            }
        });
        try
        {
            await Task.Run(()=>{using var source=new Store(dir,true);source.ExportFile(path,true,false,false,false);});
            using var plan=Store.PrepareImport(path);check(plan.Sessions==1&&plan.Messages>=2000,"snapshot remains consistent during concurrent writes");
        }
        finally{stop.Cancel();await writer;}
        check(writes>0,"normal writes succeed while export runs");
    }
    private static class InterlockedExtensions
    {
        public static void Max(ref long target,long value){long old;do{old=Volatile.Read(ref target);if(value<=old)return;}while(Interlocked.CompareExchange(ref target,value,old)!=old);}
    }
}
