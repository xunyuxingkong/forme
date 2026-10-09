using Forme.Core;
using Microsoft.Data.Sqlite;

internal static class AiConfigurationChecks
{
    private sealed class Secret : ISecretStore
    {
        public string Key="old-key";public bool FailNext;
        public string Read()=>Key;
        public void Save(string key){Key=key;if(FailNext){FailNext=false;throw new IOException("injected secret failure");}}
    }
    public static void Run(string root,Action<bool,string> check,Action<Action,string> throws)
    {
        using var store=new Store(Path.Combine(root,"ai-config"));var old=store.LoadPreferences();store.SavePreferences(old);var current=store.NewSession(old.Endpoint);var secret=new Secret();
        var next=old with{Endpoint="https://example.com/v1",Model="new-model"};
        using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(store.DirectoryPath,"forme.db"),Pooling=false}.ToString());db.Open();
        void Sql(string sql){using var command=db.CreateCommand();command.CommandText=sql;command.ExecuteNonQuery();}
        Sql("CREATE TRIGGER fail_settings BEFORE INSERT ON settings BEGIN SELECT RAISE(ABORT,'injected'); END;");
        throws(()=>store.SaveAiConfiguration(next,current,secret,"new-key"),"AI preferences failure rejected");
        check(store.LoadPreferences()==old&&secret.Key=="old-key"&&store.Sessions().Count==1,"preferences failure preserves full configuration");Sql("DROP TRIGGER fail_settings;");
        Sql("CREATE TRIGGER fail_session BEFORE INSERT ON sessions BEGIN SELECT RAISE(ABORT,'injected'); END;");
        throws(()=>store.SaveAiConfiguration(next,current,secret,"new-key"),"AI session failure rejected");
        check(store.LoadPreferences()==old&&secret.Key=="old-key"&&store.Sessions().Count==1,"session failure rolls back preferences");Sql("DROP TRIGGER fail_session;");
        secret.FailNext=true;throws(()=>store.SaveAiConfiguration(next,current,secret,"new-key"),"AI secret failure rejected");
        check(store.LoadPreferences()==old&&secret.Key=="old-key"&&store.Sessions().Count==1,"secret failure compensates key and database transaction");
        var saved=store.SaveAiConfiguration(next,current,secret,"new-key");
        check(store.LoadPreferences()==next&&saved!.Endpoint==next.Endpoint&&secret.Key=="new-key"&&store.Sessions().Count==2,"AI configuration commits settings session and key");
        var retained=store.SaveAiConfiguration(next with{Model="second-model"},saved,secret,"");
        check(retained==saved&&secret.Key=="new-key"&&store.Sessions().Count==2,"model-only save retains session and blank key");
    }
}
