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
    public Func<CompanionCommand,ActionResult>? ActionHandler {get;set;}
    public Func<ActionPlan,ActionResult?>? ActionPreflight {get;set;}
    public Func<SceneKind>? CurrentScene {get;set;}
    public IReadOnlyList<ActionResult> ActionResults {get;private set;} = [];
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
    public bool Busy => _request is not null || _attachmentRead is not null || _projectRequest is not null;
    public bool AnimationSuspended { get; set; }
    public bool Waiting => _request is not null && LiveReply.Length==0 && DateTimeOffset.UtcNow-_requestedAt>TimeSpan.FromSeconds(5);
    private CancellationTokenSource? _request;
    private int _epoch;
    private DateTimeOffset _requestedAt;
    private readonly DispatcherTimer _timer;
    private int _saveTicks;

    public Controller(string directory,AiClient? ai=null)
    {
        Store=new(directory);Store.RecoverProjects();Store.RecoverGoals(); Preferences=Store.LoadPreferences(); Preferences.Validate(); Secrets=new(directory);Ai=ai??new();
        LoadActionRules();
        if(Store.Get<ActivitySnapshot>("activity") is { } snapshot) {Clock.Restore(snapshot);}
        Session=Store.Sessions().FirstOrDefault();
        _timer=new DispatcherTimer(DispatcherPriority.Background) {Interval=TimeSpan.FromSeconds(1)};
        _timer.Tick+=(_,_)=>OnTick();
    }
    public void Refresh() => Changed?.Invoke();
    public void SavePreferences(Preferences next)
    {
        if(!AiClient.SameEndpoint(next.Endpoint,Preferences.Endpoint))next=next with{ShareChatState=false,ChatStateEndpoint=""};
        if(_chatStateRequest&&!ChatCapabilitySnapshot.SharingAllowed(next)){Cancel();ChatStatus="聊天状态分享已关闭，本次请求及后续动作已停止。";}
        if(_goalExecuting&&!next.AllowGoalPlans||_goalPlay is not null&&(!next.AllowGoalPlans&&Store.Goal(_goalPlay.Id)?.Origin!=GoalOrigin.Cooperative||!next.AllowPlayControl||next.PetModel!=Preferences.PetModel)){Cancel();ChatStatus="目标许可或伙伴已变化，后续步骤已停止。";}
        if(_boatProposalGoalId is not null&&!ProposalBindingValid(next))CancelBoatProposal();
        if(_cooperativeRequest&&!CooperativeGrant(next))Cancel();
        bool providerChanged=next.Endpoint!=Preferences.Endpoint||next.Model!=Preferences.Model;
        if(providerChanged){if(CurrentProject is not null)EndProject();next=next with{AutonomyEnabled=false};}
        next.Validate();if(_selectionView is not null&&(providerChanged||next.PetModel!=Preferences.PetModel||next.AllowPetControl!=Preferences.AllowPetControl||next.AllowSceneControl!=Preferences.AllowSceneControl||next.ShareGameState!=Preferences.ShareGameState||next.GameStateEndpoint!=Preferences.GameStateEndpoint||next.SharePlayState!=Preferences.SharePlayState))NotifyActivityInteraction();string previous=Preferences.PetModel;Store.SavePreferences(next);Preferences=next;SyncCompanionLife();
        if(previous!=next.PetModel)Store.RecordPetModels(previous,next.PetModel,DateOnly.FromDateTime(DateTime.Now));
        Changed?.Invoke();
    }
    public void SaveAiConfiguration(Preferences next,string? replacementKey)
    {
        if(Busy)throw new OperationFailureException("请先停止并等待当前请求结束。");
        if(!AiClient.SameEndpoint(next.Endpoint,Preferences.Endpoint))next=next with{ShareChatState=false,ChatStateEndpoint=""};
        if(next.Endpoint!=Preferences.Endpoint||next.Model!=Preferences.Model)next=next with{AutonomyEnabled=false};
        var session=Store.SaveAiConfiguration(next,Session,Secrets,replacementKey);
        Preferences=next;Session=session;LiveReply="";ChatStatus="";if(_boatProposalGoalId is not null&&!ProposalBindingValid(next))CancelBoatProposal();SyncCompanionLife();Changed?.Invoke();
    }
    public void Start(string kind,int minutes,string title)
    {
        NotifyActivityInteraction();Clock.Start(kind,minutes,title);
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
        if(entry.Result=="completed"){Finished?.Invoke(entry.Kind=="focus"?"专注完成了，休息一下吧。":"休息结束了，按自己的节奏继续。");if(entry.Kind=="focus")CompletedFocusInvitation();}
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
    public void SelectSession(ChatSession session){if(Busy)throw new OperationFailureException("请先停止当前回复。");if(Session?.Id!=session.Id)ClearAttachments();Session=session;LiveReply="";ChatStatus="";Changed?.Invoke();}
    public void NewSession(){if(Busy)throw new OperationFailureException("请先停止当前回复。");ClearAttachments();Session=Store.NewSession(Preferences.Endpoint);LiveReply="";ChatStatus="";Changed?.Invoke();}
    public void DeleteSession(){Cancel();ClearAttachments();if(Session is not null){Store.DeleteSession(Session.Id);if(PendingChatSave?.SessionId==Session.Id)PendingChatSave=null;}Session=Store.Sessions().FirstOrDefault();LiveReply="";ChatStatus="";Changed?.Invoke();}
    public void Cancel(){_epoch++;_attachmentRead?.Cancel();_request?.Cancel();CancelBoatProposal();StopWaitingGoalPlay();if(_projectRequest is not null)EndProject();if(_request is not null)StopSelectedActivity();}
    public void StopReply(){Cancel();ChatStatus=_localExecuting?"已停止本地动作，没有发起AI请求。":"已停止。服务端已发生的用量可能仍计费。";Changed?.Invoke();}
    public async Task Send(string? retryId=null)
    {
        await TakeForegroundAsync();
        if(Busy) return;
        if(PendingChatSave is not null)throw new OperationFailureException("请先保存或放弃暂存回复，再继续聊天。");
        var key=Secrets.Read(); if(string.IsNullOrEmpty(key)) throw new OperationFailureException("请先在设置中连接 AI；也可以继续使用本地活动。");
        Session??=Store.NewSession(Preferences.Endpoint);
        if(!AiClient.SameEndpoint(Session.Endpoint,Preferences.Endpoint)) throw new OperationFailureException("此会话属于另一服务，请新建会话，或确认转移上下文后继续。");
        var history=Store.ContextMessages(Session.Id);
        string input=Draft.Trim();
        IReadOnlyList<AiAttachment> attachments=_attachments.ToArray();
        if(retryId is not null)
        {
            if(Store.Goal(retryId) is not null)throw new OperationFailureException("这条消息已有目标执行记录，不会自动重放；请查看执行记录后明确发送新目标。");
            var user=history.LastOrDefault(x=>x.Role=="user" && x.Id==retryId)??throw new OperationFailureException("找不到重试消息。");
            input=user.Content;history=history.TakeWhile(x=>x.Id!=retryId).ToList();
            if(_attachments.Count>0)throw new OperationFailureException("已选择新附件，请用发送按钮发起新消息。");
            attachments=[];
            if(user.Status=="attachment")
            {
                if(_attachmentRetry is not {} saved||saved.Id!=retryId||!AiClient.SameEndpoint(saved.Endpoint,Preferences.Endpoint))throw new OperationFailureException("原附件未保存，请重新添加附件后发送。不会只重试文字。");
                input=saved.Input;attachments=saved.Items;
            }
        }
        if(input.Length==0&&attachments.Count>0)input="请阅读我本次上传的附件。";
        bool aiRouting=attachments.Count==0;
        var capabilitySnapshot=aiRouting?CaptureChatCapabilities():null;
        bool trimmed=false;
        var turns=AiClient.BuildContext(Preferences,history,input,out trimmed,Preferences.MemoryEnabled?Store.Memories():null,Preferences.MemoryEnabled?Store.Note(Session.Id):null,attachments:attachments,additionalSystemInstruction:capabilitySnapshot is null?null:ChatRoutingInstructions(capabilitySnapshot));
        ActionResults=[];
        string storedInput=attachments.Count==0?input:input+"\n\n[本次附件："+string.Join("、",attachments.Select(x=>x.Name))+"；原内容未保存，后续消息不自动重传]";
        var userMessage=new ChatMessage(retryId??Guid.NewGuid().ToString("N"),Session.Id,"user",storedInput,attachments.Count==0?"complete":"attachment",DateTimeOffset.Now);
        var response=new ChatMessage(Guid.NewGuid().ToString("N"),Session.Id,"assistant","","streaming",DateTimeOffset.Now.AddTicks(1));
        Store.BeginChatTurn(retryId is null?userMessage:null,response);
        if(retryId is null){Draft="";_attachments.Clear();_attachmentRetry=attachments.Count>0?new(userMessage.Id,input,Preferences.Endpoint,attachments):null;}
        LiveReply="";ChatStatus=trimmed?"上下文已按预算裁剪。正在回应…":"正在回应…";
        var request=new CancellationTokenSource();_request=request;_chatStateRequest=capabilitySnapshot?.Shared==true;int epoch=++_epoch;_requestedAt=DateTimeOffset.UtcNow;
        var p=Preferences with {};SyncTimer();Changed?.Invoke();
        DateTimeOffset lastSave=DateTimeOffset.UtcNow;int savedLength=0;bool limited=false;
        string text="",status="error";bool productivityTurn=false;
        var pending=new ReplyBuffer();
        int pendingStage=(int)AiRequestStage.WaitingForResponse,paintedStage=-1;
        var paint=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(80)};
        paint.Tick+=(_,_)=>
        {
            if(epoch!=_epoch)return;
            int stage=Volatile.Read(ref pendingStage);
            if(stage!=paintedStage)
            {
                paintedStage=stage;ChatStatus=(AiRequestStage)stage switch{AiRequestStage.WaitingForResponse=>"正在等待服务响应…",AiRequestStage.WaitingForContent=>"服务已响应，等待回复…",AiRequestStage.Thinking=>"模型正在思考，等待回复…",_=>"正在回应…"};Tick?.Invoke();
            }
            if(pending.ReadChanges() is not {} partial)return;
            text=partial;LiveReply=ConversationText.Visible(partial);
            try{if(DateTimeOffset.UtcNow-lastSave>TimeSpan.FromSeconds(3)&&partial.Length-savedLength>=64){Store.SaveMessage(response with{Content=LiveReply});lastSave=DateTimeOffset.UtcNow;savedLength=partial.Length;}}
            catch(Exception ex) when(OperationErrors.Expected(ex)){request.Cancel();ChatStatus="本地保存失败，已停止请求："+OperationErrors.Message(ex);}
            Tick?.Invoke();
        };
        paint.Start();
        try
        {
            var result=await Ai.SendAsync(p,key,turns,pending.Append,request.Token,trimmed,stage=>Volatile.Write(ref pendingStage,(int)stage));
            limited=result.LengthLimited;if(epoch==_epoch){text=result.Content;status="complete";}else status="stopped";
            if(epoch==_epoch)ChatStatus=result.LengthLimited?"已达到单次回复上限，未自动续写，未执行动作。":trimmed?"回复完成；上下文已按预算裁剪。":"回复完成。";
        }
        catch(OperationCanceledException){status="stopped";if(epoch==_epoch)ChatStatus="已停止。";}
        catch(Exception ex) when(OperationErrors.Expected(ex)){if(epoch==_epoch)ChatStatus=OperationErrors.Message(ex);}
        finally
        {
            paint.Stop();text=ConversationText.Visible(pending.Snapshot());
            if(aiRouting&&status=="complete"&&epoch==_epoch&&!limited&&!request.IsCancellationRequested)
            {
                try
                {
                    string completedReply=pending.Snapshot();
                    int protocolCount=new[]{CompanionCommands.Marker,ProductivityIntentProtocol.Marker,GoalProtocol.Marker}.Count(marker=>completedReply.Contains(marker,StringComparison.Ordinal));
                    if(CompanionCommands.TryExtractLegacyActionPlan(completedReply,out _))protocolCount++;
                    if(protocolCount>1)throw new InvalidDataException("AI返回了多种操作协议，整组没有执行。");
                    if(GoalProtocol.TryExtract(completedReply,out string goalJson))
                    {
                        var plan=GoalProtocol.Parse(goalJson,input,capabilitySnapshot!.InitialScene);
                        text=await ExecuteGoalPlan(plan,userMessage,capabilitySnapshot,request.Token);productivityTurn=true;ChatStatus=text;
                    }
                    else if(AiIntentProtocol.TryExtractActions(completedReply,out string actionJson))
                    {
                        var intents=AiIntentProtocol.Parse(actionJson,input);
                        ActionResults=intents.Count==0?[ActionResult.Failure(ActionResultCode.NotRecognized,"没有识别到明确动作，没有执行；请换一种说法。")]:await ExecuteIntents(intents,request.Token,capabilitySnapshot);
                        text=ResultText(ActionResults);ChatStatus=ActionResults.LastOrDefault()?.UserMessage??text;
                    }
                    else if(ProductivityIntentProtocol.TryExtract(completedReply,out string productivityJson))
                    {
                        if(capabilitySnapshot is not null&&ChatCapabilitySnapshot.Validate(capabilitySnapshot,CaptureChatCapabilities(),new([])) is {} stale)throw new ActionFailureException(stale.Code,stale.UserMessage);
                        var intents=ProductivityIntentProtocol.Parse(productivityJson,input);
                        text=ExecuteProductivityIntents(intents);ChatStatus=text;productivityTurn=true;
                    }
                }
                catch(ActionFailureException ex){ActionResults=[ActionResult.Failure(ex.Code,ex.Message)];text=ResultText(ActionResults);ChatStatus=ActionResults[0].UserMessage;}
                catch(InvalidDataException ex){text=ex.Message;ChatStatus=text;}
                catch(Exception ex) when(OperationErrors.Expected(ex)){text=OperationErrors.Message(ex);ChatStatus=text;}
            }
            try
            {
                if(Store.HasSession(response.SessionId) && (epoch==_epoch || request.IsCancellationRequested))
                {
                    var final=response with {Content=text,Status=request.IsCancellationRequested?"stopped":productivityTurn?"local":status};
                    try{Store.SaveMessage(final);if(productivityTurn)Store.MarkLocalProductivityTurn(userMessage.Id,response.Id);}
                    catch(Exception ex) when(OperationErrors.Expected(ex)){if(productivityTurn)Store.MarkLocalProductivityTurn(userMessage.Id,response.Id);PendingChatSave=final;ChatStatus="回复暂存于内存，可复制或重试保存："+OperationErrors.Message(ex);Notice?.Invoke(ChatStatus);}
                }
            }
            finally{if(status=="complete"&&!request.IsCancellationRequested)_attachmentRetry=null;_request=null;_chatStateRequest=false;request.Dispose();LiveReply="";SyncTimer();Changed?.Invoke();}
        }
    }
    private string ExecuteProductivityIntents(IReadOnlyList<ProductivityIntent> intents)
    {
        if(intents.Count==0)return "没有识别到明确的记事或待办操作，没有保存任何内容。";
        var output=new List<string>();var now=DateTimeOffset.Now;
        foreach(var intent in intents)
        {
            switch(intent.Type)
            {
                case ProductivityIntentType.CreateNote:
                    Store.SaveUserNote(new(Guid.NewGuid().ToString("N"),intent.Title!,intent.Content!,intent.Tags??[],false,false,"ai",now,now));output.Add("已在本机保存 1 条记事。标题和正文未写入聊天记录，请到“记事”页面查看。");break;
                case ProductivityIntentType.SearchNote:
                    var notes=Store.UserNotes(search:intent.Query,limit:50);output.Add(notes.Count==0?"没有找到匹配的本机记事。":$"找到 {notes.Count} 条本机记事。请到“记事”页面查看内容；记事正文未复制到聊天记录，也未发送给 AI。");break;
                case ProductivityIntentType.CreateTask:
                    DateTimeOffset? due=intent.DueAt is null?null:DateTimeOffset.Parse(intent.DueAt,System.Globalization.CultureInfo.InvariantCulture);
                    DateTimeOffset? remind=intent.RemindAt is null?null:DateTimeOffset.Parse(intent.RemindAt,System.Globalization.CultureInfo.InvariantCulture);
                    var taskId=Guid.NewGuid().ToString("N");Store.SaveUserTask(new(taskId,intent.Title!,intent.Description??"","todo",intent.Priority??"normal",due,remind,now,now,null,"ai","",[],0));
                    output.Add("已在本机保存 1 条待办。标题和说明未写入聊天记录，请到“今日”页面查看。"+(due is {} dueAt?$"截止时间：{dueAt.LocalDateTime:MM-dd HH:mm}。":""));break;
                case ProductivityIntentType.CreateReminder:
                    var reminderAt=DateTimeOffset.Parse(intent.DueAt!,System.Globalization.CultureInfo.InvariantCulture);Store.SaveUserReminder(new(Guid.NewGuid().ToString("N"),intent.Title!,reminderAt,null,"pending","",now,now,null));output.Add($"已在本机保存 1 条提醒（{reminderAt.LocalDateTime:MM-dd HH:mm}）。提醒内容未写入聊天记录，请到“今日”页面查看。");break;
                case ProductivityIntentType.ListTodayTasks:
                    var today=DateOnly.FromDateTime(now.LocalDateTime);var todayTasks=Store.UserTasks(status:"todo",limit:100).Where(x=>x.DueAt is null||DateOnly.FromDateTime(x.DueAt.Value.LocalDateTime)==today).ToList();
                    output.Add(todayTasks.Count==0?"今天没有待办。":$"今天有 {todayTasks.Count} 条待办。请到“今日”页面查看；待办标题未复制到聊天记录，也未发送给 AI。");break;
            }
        }
        return string.Join("\n",output);
    }
    public async Task TestConnection(string endpoint,string model,string key,string? reasoningEffort=null)
    {
        if(Busy) throw new OperationFailureException("已有请求正在处理。");
        var request=new CancellationTokenSource();_request=request;SyncTimer();Changed?.Invoke();
        try{await Ai.SendAsync(Preferences with {Endpoint=endpoint,Model=model,MaxReplyTokens=AiClient.IsZhipuEndpoint(endpoint)||AiClient.IsSiliconFlowGlm(endpoint,model)?Preferences.MaxReplyTokens:128,ZhipuReasoningEffort=reasoningEffort??Preferences.ZhipuReasoningEffort},key,[new("user","请只回复：连接成功")],_=>{},request.Token);}
        finally{_request=null;request.Dispose();SyncTimer();Changed?.Invoke();}
    }
    public void AfterImport(){CancelBoatProposal();StopWaitingGoalPlay();EndProject();ClearAttachments();Preferences=Store.LoadPreferences();Session=Store.Sessions().FirstOrDefault();PendingChatSave=null;MemoryDraft="";Draft="";LiveReply="";Changed?.Invoke();}
    public void ClearAll()
    {
        if(Busy)throw new OperationFailureException("请先停止请求并等待结束，再清除数据。");
        CancelBoatProposal();StopWaitingGoalPlay();EndProject();ClearAttachments();if(Clock.Active)Clock.Finish();File.Delete(ActionRulesPath);File.Delete(ActionRulesPath+".tmp");Store.ResetAll();Secrets.Delete();LoadActionRules();Preferences=new(){Onboarded=true};Store.SavePreferences(Preferences);Session=null;PendingChatSave=null;MemoryDraft="";Draft="";FocusDraft="";FocusDurationDraft=null;RestDurationDraft=null;LiveReply="";ChatStatus="";SyncTimer();Changed?.Invoke();
    }
    public void Dispose(){DisposeCompanionLife();DisposeProject();Cancel();ClearAttachments();_timer.Stop();Clock.Pause();try{Store.SaveActivity(Clock.Snapshot());}finally{Ai.Dispose();Store.Dispose();}}
}
