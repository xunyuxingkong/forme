using Forme.Core;
using System.Windows.Threading;

namespace Forme.App;

internal sealed class Controller : IDisposable
{
    public Store Store { get; }
    public Preferences Preferences { get; private set; }
    public FocusClock Clock { get; } = new();
    public Secrets Secrets { get; }
    public AiClient Ai { get; } = new();
    public event Action? Changed;
    public event Action? Tick;
    public event Action<string>? Notice;
    public event Action<string>? Finished;
    public ChatSession? Session { get; private set; }
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

    public Controller(string directory)
    {
        Store=new(directory); Preferences=Store.LoadPreferences(); Preferences.Validate(); Secrets=new(directory);
        if(Store.Get<ActivitySnapshot>("activity") is { } snapshot) {Clock.Restore(snapshot);}
        Session=Store.Sessions().FirstOrDefault();
        _timer=new DispatcherTimer(DispatcherPriority.Background) {Interval=TimeSpan.FromSeconds(1)};
        _timer.Tick+=(_,_)=>OnTick();
    }
    public void Refresh() => Changed?.Invoke();
    public void SavePreferences(Preferences next)
    {
        Store.SavePreferences(next); Preferences=next; Changed?.Invoke();
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
        catch(Exception ex){Clock.Pause();SyncTimer();Notice?.Invoke("进度保存失败，计时已暂停："+ex.Message);Changed?.Invoke();}
    }
    private void SyncTimer(){if(Clock.Running || Busy)_timer.Start();else _timer.Stop();}
    public void SelectSession(ChatSession session){if(Busy)throw new InvalidOperationException("请先停止当前回复。");Session=session;LiveReply="";ChatStatus="";Changed?.Invoke();}
    public void NewSession(){if(Busy)throw new InvalidOperationException("请先停止当前回复。");Session=Store.NewSession(Preferences.Endpoint);LiveReply="";ChatStatus="";Changed?.Invoke();}
    public void DeleteSession(){Cancel();if(Session is not null)Store.DeleteSession(Session.Id);Session=Store.Sessions().FirstOrDefault();LiveReply="";ChatStatus="";Changed?.Invoke();}
    public void Cancel(){_epoch++;_request?.Cancel();}
    public void StopReply(){Cancel();ChatStatus="已停止。服务端已发生的用量可能仍计费。";Changed?.Invoke();}
    public async Task Send(string? retryId=null)
    {
        if(Busy) return;
        var key=Secrets.Read(); if(string.IsNullOrEmpty(key)) throw new InvalidOperationException("请先在设置中连接 AI；也可以继续使用本地活动。");
        Session??=Store.NewSession(Preferences.Endpoint);
        if(Session.Endpoint!=Preferences.Endpoint) throw new InvalidOperationException("此会话属于另一服务，请新建会话，或确认转移上下文后继续。");
        var history=Store.Messages(Session.Id,0,60);
        string input=Draft.Trim();
        if(retryId is not null)
        {
            var user=history.LastOrDefault(x=>x.Role=="user" && x.Id==retryId)??throw new InvalidOperationException("找不到重试消息。");
            input=user.Content;history=history.TakeWhile(x=>x.Id!=retryId).ToList();
        }
        var turns=AiClient.BuildContext(Preferences,history,input,out bool trimmed);
        var userMessage=new ChatMessage(retryId??Guid.NewGuid().ToString("N"),Session.Id,"user",input,"complete",DateTimeOffset.Now);
        if(retryId is null){Store.SaveMessage(userMessage);Store.RenameSession(Session.Id,input);Draft="";}
        var response=new ChatMessage(Guid.NewGuid().ToString("N"),Session.Id,"assistant","","streaming",DateTimeOffset.Now.AddTicks(1));
        Store.SaveMessage(response); LiveReply="";ChatStatus=trimmed?"较早对话未发送。正在回应…":"正在回应…";
        var request=new CancellationTokenSource();_request=request;int epoch=++_epoch;_requestedAt=DateTimeOffset.UtcNow;
        var p=Preferences with {};SyncTimer();Changed?.Invoke();var dispatcher=Dispatcher.CurrentDispatcher;
        DateTimeOffset lastPaint=DateTimeOffset.MinValue,lastSave=DateTimeOffset.MinValue;
        string text="",status="error";
        try
        {
            var result=await Ai.SendAsync(p,key,turns,partial=>
            {
                dispatcher.Invoke(()=>
                {
                    if(epoch!=_epoch || !Store.HasSession(response.SessionId)) return;
                    text=partial;LiveReply=partial;
                    if(DateTimeOffset.UtcNow-lastSave>TimeSpan.FromSeconds(2)){Store.SaveMessage(response with {Content=partial});lastSave=DateTimeOffset.UtcNow;}
                    if(DateTimeOffset.UtcNow-lastPaint>TimeSpan.FromMilliseconds(80)){Tick?.Invoke();lastPaint=DateTimeOffset.UtcNow;}
                });
            },request.Token,trimmed);
            if(epoch==_epoch){text=result.Content;status="complete";}else status="stopped";
            if(epoch==_epoch)ChatStatus=result.LengthLimited?"已达到单次回复上限，未自动续写。":trimmed?"回复完成；较早对话未发送。":"回复完成。";
        }
        catch(OperationCanceledException){status="stopped";if(epoch==_epoch)ChatStatus="已停止。";}
        catch(Exception ex){if(epoch==_epoch)ChatStatus=ex.Message;}
        finally
        {
            try
            {
                if(Store.HasSession(response.SessionId) && (epoch==_epoch || request.IsCancellationRequested))
                    Store.SaveMessage(response with {Content=text,Status=request.IsCancellationRequested?"stopped":status});
            }
            finally{_request=null;request.Dispose();LiveReply="";SyncTimer();Changed?.Invoke();}
        }
    }
    public async Task TestConnection(string endpoint,string model,string key)
    {
        if(Busy) throw new InvalidOperationException("已有请求正在处理。");
        var request=new CancellationTokenSource();_request=request;SyncTimer();Changed?.Invoke();
        try{await Ai.SendAsync(Preferences with {Endpoint=endpoint,Model=model},key,[new("user","请只回复：连接成功")],_=>{},request.Token);}
        finally{_request=null;request.Dispose();SyncTimer();Changed?.Invoke();}
    }
    public void AfterImport(){Preferences=Store.LoadPreferences();Session=Store.Sessions().FirstOrDefault();Draft="";LiveReply="";Changed?.Invoke();}
    public void ClearAll()
    {
        if(Busy)throw new InvalidOperationException("请先停止请求并等待结束，再清除数据。");
        if(Clock.Active)Clock.Finish();Store.ResetAll();Secrets.Delete();Preferences=new(){Onboarded=true};Store.SavePreferences(Preferences);Session=null;Draft="";FocusDraft="";FocusDurationDraft=null;RestDurationDraft=null;LiveReply="";ChatStatus="";SyncTimer();Changed?.Invoke();
    }
    public void Dispose(){Cancel();_timer.Stop();Clock.Pause();Store.SaveActivity(Clock.Snapshot());Ai.Dispose();Store.Dispose();}
}
