using Forme.Core;
using System.Windows.Threading;

namespace Forme.App;

internal sealed partial class Controller : IDisposable
{
    public Store Store { get; }
    public Preferences Preferences { get; private set; }
    public FocusClock Clock { get; } = new();
    public Secrets Secrets { get; }
    public AiClient Ai { get; }
    public event Action? Changed;
    public event Action? Tick;
    public event Action<string>? Notice;
    public event Action<string>? Finished;
    public ChatSession? Session { get; private set; }
    public Func<CompanionCommand,string>? CommandHandler {get;set;}
    public Func<CompanionCommand,CancellationToken,Task<bool>>? CommandWaiter {get;set;}
    public Action? StopCommands {get;set;}
    public Func<string>? SceneContext {get;set;}
    public ChatMessage? PendingChatSave {get;private set;}
    public string MemoryDraft {get;set;}="";
    public void RetryChatSave(){if(PendingChatSave is not {} message)return;if(!Store.HasSession(message.SessionId)){PendingChatSave=null;Refresh();return;}Store.SaveMessage(message);PendingChatSave=null;Refresh();}
    public void DiscardPendingSave(){PendingChatSave=null;Refresh();}
    public string Draft { get; set; } = "";
    // Unstarted focus inputs remain local, in memory for this app session only.
    public string FocusDraft { get; set; } = "";
    public string? FocusDurationDraft { get; set; }
    public string? RestDurationDraft { get; set; }
    public string LiveReply { get; private set; } = "";
    public string ChatStatus { get; private set; } = "";
    public bool Busy => _request is not null;
    public bool AnimationSuspended { get; set; }
    public bool Waiting => Busy && LiveReply.Length==0 && DateTimeOffset.UtcNow-_requestedAt>TimeSpan.FromSeconds(5);
    private CancellationTokenSource? _request;
    private int _epoch;
    private DateTimeOffset _requestedAt;
    private readonly DispatcherTimer _timer;
    private int _saveTicks;

    public Controller(string directory,AiClient? ai=null)
    {
        Store=new(directory); Preferences=Store.LoadPreferences(); Preferences.Validate(); Secrets=new(directory);Ai=ai??new();
        LoadActionRules();
        if(Store.Get<ActivitySnapshot>("activity") is { } snapshot) {Clock.Restore(snapshot);}
        Session=Store.Sessions().FirstOrDefault();
        _timer=new DispatcherTimer(DispatcherPriority.Background) {Interval=TimeSpan.FromSeconds(1)};
        _timer.Tick+=(_,_)=>OnTick();
    }
    public void Refresh() => Changed?.Invoke();
    public void SavePreferences(Preferences next)
    {
        next.Validate();string previous=Preferences.PetModel;Store.SavePreferences(next);Preferences=next;
        if(previous!=next.PetModel)Store.RecordPetModels(previous,next.PetModel,DateOnly.FromDateTime(DateTime.Now));
        Changed?.Invoke();
    }
    public void SaveAiConfiguration(Preferences next,string? replacementKey)
    {
        if(Busy)throw new OperationFailureException("请先停止并等待当前请求结束。");
        var session=Store.SaveAiConfiguration(next,Session,Secrets,replacementKey);
        Preferences=next;Session=session;LiveReply="";ChatStatus="";Changed?.Invoke();
    }
    public void Start(string kind,int minutes,string title)
    {
        Clock.Start(kind,minutes,title);
        try {Store.SaveActivity(Clock.Snapshot());} catch {Clock.Finish();throw;}
        if(kind=="focus")FocusDraft="";
        _saveTicks=0; SyncTimer(); Changed?.Invoke();
    }
    public void Pause()
    {
        Clock.Pause();Store.SaveActivity(Clock.Snapshot());SyncTimer();Changed?.Invoke();
    }
    public void Resume() {if(Clock.Complete){Finish();return;}Clock.Resume();Store.SaveActivity(Clock.Snapshot());SyncTimer();Changed?.Invoke();}
    public void Finish()
    {
        var before=Clock.Snapshot(); var entry=Clock.Finish();
        try{Store.FinishFocus(entry);}catch{if(before is not null)Clock.Restore(before);SyncTimer();throw;}
        SyncTimer(); Changed?.Invoke();
        if(entry.Result=="completed") Finished?.Invoke(entry.Kind=="focus"?"专注完成了，休息一下吧。":"休息结束了，按自己的节奏继续。");
    }
    private void OnTick()
    {
        try
        {
            if(Clock.Running)
            {
                if(Clock.Complete) {Finish();return;}
                if(++_saveTicks>=15){Store.SaveActivity(Clock.Snapshot());_saveTicks=0;}
            }
            Tick?.Invoke();
        }
        catch(Exception ex) when(OperationErrors.Expected(ex)){Clock.Pause();SyncTimer();Notice?.Invoke("进度保存失败，计时已暂停："+OperationErrors.Message(ex));Changed?.Invoke();}
    }
    private void SyncTimer(){if(Clock.Running || Busy)_timer.Start();else _timer.Stop();}
    public void SelectSession(ChatSession session){if(Busy)throw new OperationFailureException("请先停止当前回复。");Session=session;LiveReply="";ChatStatus="";Changed?.Invoke();}
    public void NewSession(){if(Busy)throw new OperationFailureException("请先停止当前回复。");Session=Store.NewSession(Preferences.Endpoint);LiveReply="";ChatStatus="";Changed?.Invoke();}
    public void DeleteSession(){Cancel();if(Session is not null){Store.DeleteSession(Session.Id);if(PendingChatSave?.SessionId==Session.Id)PendingChatSave=null;}Session=Store.Sessions().FirstOrDefault();LiveReply="";ChatStatus="";Changed?.Invoke();}
    public void Cancel(){_epoch++;_request?.Cancel();if(_request is not null)StopCommands?.Invoke();}
    public void StopReply(){Cancel();ChatStatus=_localExecuting?"已停止本地动作，没有发起AI请求。":"已停止。服务端已发生的用量可能仍计费。";Changed?.Invoke();}
    public async Task Send(string? retryId=null)
    {
        if(Busy) return;
        if(PendingChatSave is not null)throw new OperationFailureException("请先保存或放弃暂存回复，再继续聊天。");
        var key=Secrets.Read(); if(string.IsNullOrEmpty(key)) throw new OperationFailureException("请先在设置中连接 AI；也可以继续使用本地活动。");
        Session??=Store.NewSession(Preferences.Endpoint);
        if(!AiClient.SameEndpoint(Session.Endpoint,Preferences.Endpoint)) throw new OperationFailureException("此会话属于另一服务，请新建会话，或确认转移上下文后继续。");
        var history=Store.ContextMessages(Session.Id);
        string input=Draft.Trim();
        if(retryId is not null)
        {
            var user=history.LastOrDefault(x=>x.Role=="user" && x.Id==retryId)??throw new OperationFailureException("找不到重试消息。");
            input=user.Content;history=history.TakeWhile(x=>x.Id!=retryId).ToList();
        }
        var turns=AiClient.BuildContext(Preferences,history,input,out bool trimmed,Preferences.MemoryEnabled?Store.Memories():null,Preferences.MemoryEnabled?Store.Note(Session.Id):null,CompanionCommands.HasControl(Preferences)?SceneContext?.Invoke():null);
        var userMessage=new ChatMessage(retryId??Guid.NewGuid().ToString("N"),Session.Id,"user",input,"complete",DateTimeOffset.Now);
        var response=new ChatMessage(Guid.NewGuid().ToString("N"),Session.Id,"assistant","","streaming",DateTimeOffset.Now.AddTicks(1));
        Store.BeginChatTurn(retryId is null?userMessage:null,response);if(retryId is null)Draft="";LiveReply="";ChatStatus=trimmed?"上下文已按预算裁剪。正在回应…":"正在回应…";
        var request=new CancellationTokenSource();_request=request;int epoch=++_epoch;_requestedAt=DateTimeOffset.UtcNow;
        var p=Preferences with {};SyncTimer();Changed?.Invoke();
        DateTimeOffset lastSave=DateTimeOffset.UtcNow;int savedLength=0;bool limited=false;
        string text="",status="error";
        var pending=new ReplyBuffer();
        var paint=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(80)};
        paint.Tick+=(_,_)=>
        {
            if(epoch!=_epoch)return;
            if(pending.ReadChanges() is not {} partial)return;
            text=partial;LiveReply=CompanionCommands.VisibleText(partial);
            try{if(DateTimeOffset.UtcNow-lastSave>TimeSpan.FromSeconds(3)&&partial.Length-savedLength>=64){Store.SaveMessage(response with{Content=LiveReply});lastSave=DateTimeOffset.UtcNow;savedLength=partial.Length;}}
            catch(Exception ex) when(OperationErrors.Expected(ex)){request.Cancel();ChatStatus="本地保存失败，已停止请求："+OperationErrors.Message(ex);}
            Tick?.Invoke();
        };
        paint.Start();
        try
        {
            var result=await Ai.SendAsync(p,key,turns,pending.Append,request.Token,trimmed);
            limited=result.LengthLimited;if(epoch==_epoch){text=result.Content;status="complete";}else status="stopped";
            if(epoch==_epoch)ChatStatus=result.LengthLimited?"已达到单次回复上限，未自动续写，未执行动作。":trimmed?"回复完成；上下文已按预算裁剪。":"回复完成。";
        }
        catch(OperationCanceledException){status="stopped";if(epoch==_epoch)ChatStatus="已停止。";}
        catch(Exception ex) when(OperationErrors.Expected(ex)){if(epoch==_epoch)ChatStatus=OperationErrors.Message(ex);}
        finally
        {
            paint.Stop();text=pending.Snapshot();var parsed=CompanionCommands.Parse(text,p);text=parsed.Text;
            if(status=="complete"&&epoch==_epoch&&!limited&&!request.IsCancellationRequested)
            {
                var reports=await ExecuteCommands(parsed.Commands,epoch,request.Token,text);
                if(parsed.Rejected>0)reports.Add("已忽略无效或未授权动作");
                if(reports.Count>0){string report=string.Join("；",reports);ChatStatus+=" "+report;text+="\n\n"+CompanionCommands.ReportMarker+" "+report;}
            }
            try
            {
                if(Store.HasSession(response.SessionId) && (epoch==_epoch || request.IsCancellationRequested))
                {
                    var final=response with {Content=text,Status=request.IsCancellationRequested?"stopped":status};
                    try{Store.SaveMessage(final);}
                    catch(Exception ex) when(OperationErrors.Expected(ex)){PendingChatSave=final;ChatStatus="回复暂存于内存，可复制或重试保存："+OperationErrors.Message(ex);Notice?.Invoke(ChatStatus);}
                }
            }
            finally{_request=null;request.Dispose();LiveReply="";SyncTimer();Changed?.Invoke();}
        }
    }
    public async Task TestConnection(string endpoint,string model,string key)
    {
        if(Busy) throw new OperationFailureException("已有请求正在处理。");
        var request=new CancellationTokenSource();_request=request;SyncTimer();Changed?.Invoke();
        try{await Ai.SendAsync(Preferences with {Endpoint=endpoint,Model=model,MaxReplyTokens=128},key,[new("user","请只回复：连接成功")],_=>{},request.Token);}
        finally{_request=null;request.Dispose();SyncTimer();Changed?.Invoke();}
    }
    public void AfterImport(){Preferences=Store.LoadPreferences();Session=Store.Sessions().FirstOrDefault();PendingChatSave=null;MemoryDraft="";Draft="";LiveReply="";Changed?.Invoke();}
    public void ClearAll()
    {
        if(Busy)throw new OperationFailureException("请先停止请求并等待结束，再清除数据。");
        if(Clock.Active)Clock.Finish();File.Delete(ActionRulesPath);File.Delete(ActionRulesPath+".tmp");Store.ResetAll();Secrets.Delete();LoadActionRules();Preferences=new(){Onboarded=true};Store.SavePreferences(Preferences);Session=null;PendingChatSave=null;MemoryDraft="";Draft="";FocusDraft="";FocusDurationDraft=null;RestDurationDraft=null;LiveReply="";ChatStatus="";SyncTimer();Changed?.Invoke();
    }
    public void Dispose(){Cancel();_timer.Stop();Clock.Pause();try{Store.SaveActivity(Clock.Snapshot());}finally{Ai.Dispose();Store.Dispose();}}
}
