using System.Text.Json;

namespace Forme.Core;

public interface ISecretStore
{
    string Read();
    void Save(string key);
}

// Both failures and notifications are separated from the persisted operation.
public sealed partial class Store
{
    public ChatSession? SaveAiConfiguration(Preferences next,ChatSession? current,ISecretStore secrets,string? replacementKey)
    {
        next.Validate();var previous=LoadPreferences();string oldKey=secrets.Read();
        if(oldKey.Length>0&&!AiClient.ProviderIdentity(previous.Endpoint).Equals(AiClient.ProviderIdentity(next.Endpoint),StringComparison.OrdinalIgnoreCase)&&(replacementKey is null||replacementKey.Length==0))
            throw new InvalidDataException("切换 AI 服务商后必须重新填写对应的 API Key；旧密钥已保留，原配置未改变。");
        if(replacementKey is {Length:>0}&&(replacementKey.Length>512||replacementKey.Contains('\r')||replacementKey.Contains('\n')||string.IsNullOrWhiteSpace(replacementKey)))throw new InvalidDataException("密钥格式无效。");
        bool attemptedSecret=false;using var tx=_db.BeginTransaction();
        try
        {
            Exec("INSERT INTO settings VALUES('preferences',$0) ON CONFLICT(k) DO UPDATE SET v=excluded.v",tx,JsonSerializer.Serialize(next));
            ChatSession? session=current;
            if(!AiClient.SameEndpoint(next.Endpoint,previous.Endpoint))
            {
                session=new(Guid.NewGuid().ToString("N"),"聊一会儿",DateTimeOffset.Now,next.Endpoint);
                Exec("INSERT INTO sessions VALUES($0,$1,$2,$3)",tx,session.Id,session.Title,Date(session.Created),session.Endpoint);
            }
            if(replacementKey is {Length:>0}){attemptedSecret=true;secrets.Save(replacementKey);}
            tx.Commit();return session;
        }
        catch
        {
            bool recoveryFailed=false;
            try{tx.Rollback();}catch{recoveryFailed=true;}
            if(attemptedSecret)
            {
                try{secrets.Save(oldKey);}
                catch{recoveryFailed=true;}
            }
            if(recoveryFailed)throw new AiConfigurationRecoveryException();
            throw;
        }
    }
}

public sealed class AiConfigurationRecoveryException : Exception
{
    public AiConfigurationRecoveryException():base("AI 配置未完成，凭据恢复失败。请退出应用并检查本机存储权限后重新配置。"){}
}
