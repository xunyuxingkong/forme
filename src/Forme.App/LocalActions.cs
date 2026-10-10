using Forme.Core;
using System.Text;
namespace Forme.App;

internal sealed partial class Controller
{
    private ActionRules _actionRules=ActionRules.Default();
    private string? _rulesError;
    private bool _localExecuting;
    public string ActionRulesPath=>Path.Combine(Store.DirectoryPath,"interaction-rules.json");
    public string ActionRulesStatus=>_rulesError??$"已加载 {_actionRules.Rules.Length} 条本地规则";
    public void LoadActionRules()
    {
        try
        {
            if(!File.Exists(ActionRulesPath))File.WriteAllText(ActionRulesPath,ActionRules.Default().Serialize(),new UTF8Encoding(false));
            if(new FileInfo(ActionRulesPath).Length>32768)throw new InvalidDataException("规则文件最多32KB。");
            _actionRules=ActionRules.Parse(File.ReadAllText(ActionRulesPath));
            var upgraded=_actionRules.UpgradeDefaults();
            if(!ReferenceEquals(upgraded,_actionRules))SaveRules(upgraded.Serialize());
            _rulesError=null;
        }
        catch(Exception ex) when(OperationErrors.Expected(ex)){_rulesError="规则未加载："+OperationErrors.Message(ex);}
    }
    public string RulesText()=>File.Exists(ActionRulesPath)&&new FileInfo(ActionRulesPath).Length<=32768?File.ReadAllText(ActionRulesPath):ActionRules.Default().Serialize();
    public string? LocalRulePreview()=>Preferences.LocalActionRules&&_rulesError is null&&_actionRules.Match(Draft.Trim()) is {} commands?"将执行本地规则，不调用AI、不发送消息或场景。\n"+System.Text.Json.JsonSerializer.Serialize(commands)+"\n权限仍逐项检查；最多3步、30秒。":null;
    public void SaveRules(string text)
    {
        var rules=ActionRules.Parse(text);string saved=rules.Serialize();if(Encoding.UTF8.GetByteCount(saved)>32768)throw new InvalidDataException("格式化后的规则超过32KB，请减少规则。");string temporary=ActionRulesPath+".tmp";File.WriteAllText(temporary,saved,new UTF8Encoding(false));File.Move(temporary,ActionRulesPath,true);_actionRules=rules;_rulesError=null;Refresh();
    }
    public async Task<bool> TrySendLocal()
    {
        if(!Preferences.LocalActionRules)return false;
        if(_rulesError is not null)throw new OperationFailureException(_rulesError+"，请在动作规则设置中修复或恢复默认。");
        var commands=_actionRules.Match(Draft.Trim());if(commands is null)return false;
        if(Busy)return true;if(PendingChatSave is not null)throw new OperationFailureException("请先处理暂存回复。");
        string input=Draft.Trim();Session??=Store.NewSession(Preferences.Endpoint);
        var user=new ChatMessage(Guid.NewGuid().ToString("N"),Session.Id,"user",input,"local",DateTimeOffset.Now);
        var reply=new ChatMessage(Guid.NewGuid().ToString("N"),Session.Id,"assistant","","streaming",DateTimeOffset.Now.AddTicks(1));
        Store.BeginChatTurn(user,reply);Draft="";var request=new CancellationTokenSource();_request=request;int epoch=++_epoch;
        _localExecuting=true;LiveReply="正在执行本地规则，不发送AI请求。";ChatStatus=LiveReply;SyncTimer();Changed?.Invoke();
        try
        {
            var reports=await ExecuteCommands(commands,epoch,request.Token,"本地动作");
            string text="本地规则匹配 · 未联网\n"+CompanionCommands.ReportMarker+" "+string.Join("；",reports);
            if(Store.HasSession(reply.SessionId))
            {
                var final=reply with{Content=text,Status="local"};
                try{Store.SaveMessage(final);}catch(Exception ex) when(OperationErrors.Expected(ex)){PendingChatSave=final;Notice?.Invoke("本地动作反馈暂存，可重试保存。");}
            }
            ChatStatus=text;
        }
        finally{_localExecuting=false;_request=null;request.Dispose();LiveReply="";SyncTimer();Changed?.Invoke();}
        return true;
    }
    private async Task<List<string>> ExecuteCommands(IReadOnlyList<CompanionCommand> commands,int epoch,CancellationToken token,string text)
    {
        var reports=new List<string>();using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,limit.Token);
        foreach(var command in commands)
        {
            if(epoch!=_epoch||linked.IsCancellationRequested){reports.Add("动作组已停止");break;}
            if(!CompanionCommands.Allowed(command,Preferences)){reports.Add("对应动作权限未开启，请在设置中开启；剩余动作未执行");break;}
            try
            {
                if(CommandHandler is null){reports.Add("当前没有可控制的场景");break;}
                reports.Add(CommandHandler(command));LiveReply=text+"\n"+CompanionCommands.ReportMarker+" "+string.Join("；",reports);Tick?.Invoke();
                if(command.Action=="stop")break;
                if(CommandWaiter is {} wait&&!await wait(command,linked.Token)){reports.Add("活动被打断，剩余动作未执行");break;}
            }
            catch(OperationCanceledException){StopCommands?.Invoke();reports.Add(limit.IsCancellationRequested?"动作组超过30秒，已停止":"动作组已取消");break;}
            catch(Exception ex) when(OperationErrors.Expected(ex)){reports.Add("未执行："+OperationErrors.Message(ex));break;}
        }
        return reports;
    }
}
