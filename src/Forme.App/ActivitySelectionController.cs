using Forme.Core;
using System.Diagnostics;
namespace Forme.App;

internal sealed partial class Controller
{
    internal ICompanionActivityWorld? ActivityWorld {get;set;}
    internal CompanionActivityState? SelectedActivityState {get;private set;}
    internal string? SelectedActivityId {get;private set;}
    private CompanionView? _selectionView;
    private bool _applyingSelection;
    internal void NotifyActivityInteraction()
    {
        if(!_applyingSelection)_lastManualInteraction=DateTimeOffset.UtcNow;
        if(_selectionView is not null&&!_applyingSelection&&_request is {IsCancellationRequested:false})StopReply();
    }
    internal void StopSelectedActivity()
    {
        if(_selectionView is {} view)ActivityWorld?.Stop(view);else StopCommands?.Invoke();
    }
    internal List<AiTurn> PreviewActivity(CompanionView view,string goal)
    {
        if(_attachments.Count>0)throw new OperationFailureException("自主选择不使用附件，请先移除附件。");
        var world=ActivityWorld??throw new OperationFailureException("当前没有可用场景。");
        return ActivitySelection.BuildTurns(Preferences,goal,EnrichActivityObservation(world.Observe(view)));
    }
    internal async Task ChooseActivity(CompanionView view,string goal,bool automatic=false)
    {
        if(!automatic)await TakeForegroundAsync();
        if(Busy)throw new OperationFailureException("请先停止并等待当前操作结束。");
        if(PendingChatSave is not null)throw new OperationFailureException("请先处理暂存回复。");
        if(_attachments.Count>0)throw new OperationFailureException("自主选择不使用附件。");
        var world=ActivityWorld??throw new OperationFailureException("场景不可用。");
        var observation=EnrichActivityObservation(world.Observe(view));
        var turns=ActivitySelection.BuildTurns(Preferences,goal,observation);
        if(!observation.Available||observation.Options.Count==0)throw new OperationFailureException("当前没有可用活动，请显示伙伴并开启所需权限。");
        var key=Secrets.Read();if(string.IsNullOrEmpty(key))throw new OperationFailureException("请先连接AI。");
        ChatMessage? reply=null;
        if(!automatic)
        {
            Session??=Store.NewSession(Preferences.Endpoint);
            if(!AiClient.SameEndpoint(Session.Endpoint,Preferences.Endpoint))throw new OperationFailureException("会话属于另一服务，请新建会话。");
            var now=DateTimeOffset.Now;
            var user=new ChatMessage(Guid.NewGuid().ToString("N"),Session.Id,"user",goal,"local",now);
            reply=new(Guid.NewGuid().ToString("N"),Session.Id,"assistant","","streaming",now.AddTicks(1));Store.BeginChatTurn(user,reply);
            Draft="";
        }
        _automaticActivity=automatic;ActionResults=[];LiveReply="";SelectedActivityId=Guid.NewGuid().ToString("N");SelectedActivityState=CompanionActivityState.Planning;
        using var request=new CancellationTokenSource(TimeSpan.FromSeconds(120));
        _request=request;_selectionView=view;int epoch=++_epoch;_requestedAt=DateTimeOffset.UtcNow;SyncTimer();
        if(automatic){CompanionLifeStatus="伙伴正在自主选择活动…";Tick?.Invoke();}
        else{ChatStatus="正在根据当前场景选择活动…";Changed?.Invoke();}
        string text="";string status="local";var watch=Stopwatch.StartNew();
        try
        {
            using var planning=CancellationTokenSource.CreateLinkedTokenSource(request.Token);planning.CancelAfter(TimeSpan.FromSeconds(automatic?15:40));
            var result=await Ai.SendAsync(Preferences with{},key,turns,_=>{},planning.Token);
            request.Token.ThrowIfCancellationRequested();if(epoch!=_epoch)throw new OperationCanceledException(request.Token);
            if(result.LengthLimited)throw new InvalidDataException("活动选择被截断，没有执行。");
            var choice=ActivitySelection.Parse(result.Content,observation);
            if(!Preferences.ShareGameState||!AiClient.SameEndpoint(Preferences.GameStateEndpoint,Preferences.Endpoint)||
                automatic&&(!Preferences.AutonomyEnabled||Preferences.Quiet||Clock.Active||AnimationSuspended||BlockAutonomy?.Invoke()==true))
                throw new ActionFailureException(ActionResultCode.Unauthorized,"自主许可已改变，没有执行。");
            if(!automatic&&watch.Elapsed<=TimeSpan.FromSeconds(15))Store.Set("autonomy-validation",new AutonomyValidation(Preferences.Endpoint,Preferences.Model,DateTimeOffset.UtcNow));
            if(choice.Id is null){SelectedActivityState=CompanionActivityState.NoAction;text=choice.Reason;}
            else
            {
                var option=ActivitySelection.Validate(observation,EnrichActivityObservation(world.Observe(view)),choice);
                SelectedActivityState=CompanionActivityState.Executing;CompanionLifeStatus="正在执行："+option.Title;
                if(!automatic)ChatStatus=CompanionLifeStatus;Tick?.Invoke();
                ActionResults=await ExecuteChoice(world,view,observation,option,request.Token,epoch);
                var final=ActionResults.LastOrDefault();
                SelectedActivityState=final?.Code==ActionResultCode.Completed?CompanionActivityState.Completed:
                    final?.Code is ActionResultCode.Cancelled or ActionResultCode.Interrupted?CompanionActivityState.Cancelled:CompanionActivityState.Failed;
                text="选择了："+option.Title+"。\n理由："+choice.Reason+"\n"+ResultText(ActionResults);
                if(SelectedActivityState==CompanionActivityState.Completed)
                {
                    RecordExperience(option.Id,option.Id.StartsWith("chain-")?"life":"activity","completed",observation.Scene);
                    if(automatic)InviteCompanion(option.Id,"伙伴刚完成了"+option.Title+"，要不要一起玩？",view);
                }
            }
        }
        catch(OperationCanceledException)
        {
            SelectedActivityState=epoch!=_epoch||request.IsCancellationRequested?CompanionActivityState.Cancelled:CompanionActivityState.Failed;
            text=SelectedActivityState==CompanionActivityState.Cancelled?"活动已停止，未执行剩余动作。":"等待活动选择超时，没有执行，也未自动重试。";
            status=SelectedActivityState==CompanionActivityState.Cancelled?"stopped":"error";
        }
        catch(Exception ex) when(OperationErrors.Expected(ex)){SelectedActivityState=CompanionActivityState.Failed;text=OperationErrors.Message(ex);status="error";}
        finally
        {
            if(automatic)CompanionLifeStatus=text;
            else
            {
                ChatStatus=text;
                var saved=reply! with{Content=text,Status=status};
                try{if(Store.HasSession(saved.SessionId))Store.SaveMessage(saved);}
                catch(Exception ex) when(OperationErrors.Expected(ex)){PendingChatSave=saved;ChatStatus="活动结果暂存于内存："+OperationErrors.Message(ex);}
            }
            _selectionView=null;_request=null;_automaticActivity=false;LiveReply="";SyncTimer();Changed?.Invoke();
        }
    }

    private Task<IReadOnlyList<ActionResult>> ExecuteChoice(ICompanionActivityWorld world,CompanionView view,ActivityObservation observation,
        ActivityOption option,CancellationToken token,int epoch)=>ActionExecutor.Execute(new(option.Steps),()=>Preferences,
        plan=>world.Preflight(view,plan),command=>{
            token.ThrowIfCancellationRequested();if(epoch!=_epoch)throw new OperationCanceledException(token);
            var current=EnrichActivityObservation(world.Observe(view));
            if(current.SubjectId!=observation.SubjectId||current.Scene!=observation.Scene||current.Model!=observation.Model)
                throw new ActionFailureException(ActionResultCode.Interrupted,"伙伴或场景已变化，剩余动作已停止。");
            if(world.Preflight(view,new([command])) is {} denied)return denied;
            _applyingSelection=true;try{return world.Start(view,command);}finally{_applyingSelection=false;}
        },(command,cancellation)=>world.Wait(view,command,cancellation),()=>{if(epoch==_epoch)world.Stop(view);},token,
        result=>{if(!_automaticActivity)ChatStatus=result.UserMessage;CompanionLifeStatus=result.UserMessage;Tick?.Invoke();});
}
