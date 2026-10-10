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
    private IntentResolution LocalIntent(string input)=>IntentResolver.Resolve(input,_actionRules,Preferences.PetName);
    public string? LocalRulePreview()
    {
        if(!Preferences.LocalActionRules||_rulesError is not null)return null;
        var resolved=LocalIntent(Draft.Trim());if(resolved.Intents.Count==0)return null;
        try
        {
            var plan=ActionPlanner.Build(resolved.Intents,CurrentScene?.Invoke()??SceneKind.Desktop);
            var denied=ActionPlanner.Preflight(plan,Preferences,ActionPreflight);
            return "本次在本地理解和执行，不调用AI、不发送消息或场景。\n"+(denied?.UserMessage??string.Join(" → ",plan.Steps.Select(x=>x.Action=="go"?"前往"+TargetCatalog.Find(x.Value)?.Name:x.Action=="scene"?(x.Value=="outdoor"?"切换户外":"回小屋"):"伙伴动作")))+"\n整组预检；最多3步、30秒。";
        }
        catch(ActionFailureException ex){return ex.Message;}
    }
    public void SaveRules(string text)
    {
        var rules=ActionRules.Parse(text);string saved=rules.Serialize();if(Encoding.UTF8.GetByteCount(saved)>32768)throw new InvalidDataException("格式化后的规则超过32KB，请减少规则。");string temporary=ActionRulesPath+".tmp";File.WriteAllText(temporary,saved,new UTF8Encoding(false));File.Move(temporary,ActionRulesPath,true);_actionRules=rules;_rulesError=null;Refresh();
    }
    public async Task<bool> TrySendLocal()
    {
        if(!Preferences.LocalActionRules)return false;
        if(_rulesError is not null)throw new OperationFailureException(_rulesError+"，请在动作规则设置中修复或恢复默认。");
        var resolved=LocalIntent(Draft.Trim());if(resolved.Intents.Count==0)return false;
        if(Busy)return true;if(PendingChatSave is not null)throw new OperationFailureException("请先处理暂存回复。");
        string input=Draft.Trim();Session??=Store.NewSession(Preferences.Endpoint);
        var user=new ChatMessage(Guid.NewGuid().ToString("N"),Session.Id,"user",input,"local",DateTimeOffset.Now);
        var reply=new ChatMessage(Guid.NewGuid().ToString("N"),Session.Id,"assistant","","streaming",DateTimeOffset.Now.AddTicks(1));
        Store.BeginChatTurn(user,reply);Draft="";var request=new CancellationTokenSource();_request=request;++_epoch;ActionResults=[];
        _localExecuting=true;LiveReply="正在执行本地规则，不发送AI请求。";ChatStatus=LiveReply;SyncTimer();Changed?.Invoke();
        try
        {
            ActionResults=await ExecuteIntents(resolved.Intents,request.Token);
            string text=ResultText(ActionResults);
            if(Store.HasSession(reply.SessionId))
            {
                var final=reply with{Content=text,Status="local"};
                try{Store.SaveMessage(final);}catch(Exception ex) when(OperationErrors.Expected(ex)){PendingChatSave=final;Notice?.Invoke("本地动作反馈暂存，可重试保存。");}
            }
            ChatStatus=ActionResults.LastOrDefault()?.UserMessage??text;
        }
        finally{_localExecuting=false;_request=null;request.Dispose();LiveReply="";SyncTimer();Changed?.Invoke();}
        return true;
    }
    private static string ResultText(IReadOnlyList<ActionResult> results)=>string.Join("\n",results.Select(x=>x.UserMessage));
    private async Task<IReadOnlyList<ActionResult>> ExecuteIntents(IReadOnlyList<CompanionIntent> intents,CancellationToken token)
    {
        try
        {
            var plan=ActionPlanner.Build(intents,CurrentScene?.Invoke()??SceneKind.Desktop);
            return await ActionExecutor.Execute(plan,()=>Preferences,ActionPreflight,
                command=>ActionHandler?.Invoke(command)??(CommandHandler is {} handler
                    ?new ActionResult(ActionResultCode.Started,command.Action,command.Value,handler(command),TimeSpan.Zero)
                    :ActionResult.Failure(ActionResultCode.SceneUnavailable,"当前没有可控制的场景。",command)),
                CommandWaiter,()=>StopCommands?.Invoke(),token,result=>{LiveReply=result.UserMessage;ChatStatus=result.UserMessage;Tick?.Invoke();});
        }
        catch(ActionFailureException ex){return [ActionResult.Failure(ex.Code,ex.Message)];}
    }
}
