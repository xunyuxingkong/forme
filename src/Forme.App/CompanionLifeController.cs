using Forme.Core;
using System.Windows.Threading;
using System.Text.Json;
namespace Forme.App;

internal sealed partial class Controller
{
    internal string CompanionLifeStatus {get;private set;}="伙伴生活等待你的安排。";
    internal string? CompanionInvitation {get;private set;}
    private string _invitationActivity="";
    private CompanionView _invitationView;
    private bool _automaticActivity;
    internal bool IsAutomaticActivity=>_automaticActivity;
    internal bool ForegroundBusy=>Busy&&!_automaticActivity;
    private Task? _automaticTask;
    private DispatcherTimer? _lifeTimer;
    private DateTimeOffset _lastManualInteraction=DateTimeOffset.UtcNow;
    internal Func<bool>? BlockAutonomy {get;set;}
    internal void InitializeCompanionLife()
    {
        _lifeTimer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMinutes(1)};
        _lifeTimer.Tick+=async(_,_)=>{try{await TryAutomaticLife();}catch(Exception ex) when(OperationErrors.Expected(ex)){CompanionLifeStatus=OperationErrors.Message(ex);Tick?.Invoke();}};
        SyncCompanionLife();
    }
    private void SyncCompanionLife()
    {
        if(_lifeTimer is null)return;
        if(Preferences.AutonomyEnabled)_lifeTimer.Start();else _lifeTimer.Stop();
    }
    private void DisposeCompanionLife(){_lifeTimer?.Stop();}
    internal async Task TakeForegroundAsync()
    {
        if(_automaticTask is not {IsCompleted:false})return;
        NotifyActivityInteraction();
        var task=_automaticTask;
        if(await Task.WhenAny(task,Task.Delay(2000))!=task)throw new OperationFailureException("正在停止后台请求，请稍后再试；不会并发追加AI请求。");
        await task;
    }
    internal bool AutonomyVerified()
    {
        var v=Store.Get<AutonomyValidation>("autonomy-validation");
        return v is not null&&v.Endpoint==Preferences.Endpoint&&v.Model==Preferences.Model&&DateTimeOffset.UtcNow>=v.At&&DateTimeOffset.UtcNow-v.At<TimeSpan.FromDays(14);
    }
    internal async Task TryAutomaticLife()
    {
        var p=Preferences;
        if(!p.AutonomyEnabled||!AutonomyVerified()||!p.ShareGameState||!AiClient.SameEndpoint(p.GameStateEndpoint,p.Endpoint)||
            p.Quiet||Clock.Active||AnimationSuspended||Busy||PendingChatSave is not null||_attachments.Count>0||
            BlockAutonomy?.Invoke()==true||DateTimeOffset.UtcNow-_lastManualInteraction<TimeSpan.FromMinutes(2))return;
        var observation=ActivityWorld?.Observe(p.AutonomyView);
        if(observation is not {Available:true}||observation.Options.Count==0)return;
        var ledger=Store.Get<AutonomyLedger>("autonomy-ledger")??new();
        if(!ledger.Reserve(DateTimeOffset.UtcNow,p.AutonomyDailyLimit)){CompanionLifeStatus="自主生活正在冷却、暂停或已用完调用额度。";Tick?.Invoke();return;}
        Store.Set("autonomy-ledger",ledger);
        _automaticTask=RunAutomaticLife(p.AutonomyView);
        await _automaticTask;
    }
    private async Task RunAutomaticLife(CompanionView view)
    {
        try
        {
            await ChooseActivity(view,"根据当前环境和允许的偏好自己选一个短活动；不合适就不行动。",true);
            var ledger=Store.Get<AutonomyLedger>("autonomy-ledger")??new();
            ledger.Result(DateTimeOffset.UtcNow,SelectedActivityState is CompanionActivityState.Completed or CompanionActivityState.NoAction,SelectedActivityState==CompanionActivityState.Cancelled);
            Store.Set("autonomy-ledger",ledger);
        }
        catch(Exception ex) when(OperationErrors.Expected(ex))
        {
            var ledger=Store.Get<AutonomyLedger>("autonomy-ledger")??new();ledger.Result(DateTimeOffset.UtcNow,false);Store.Set("autonomy-ledger",ledger);
            CompanionLifeStatus=OperationErrors.Message(ex);Tick?.Invoke();
        }
    }
    internal ActivityObservation EnrichActivityObservation(ActivityObservation observation)
    {
        var options=observation.Options.ToList();var data=Store.CompanionLife();string facts="";
        if(Preferences.ShareExperiences)
        {
            facts=JsonSerializer.Serialize(new{favorite=data.FavoriteActivity,recent=data.Experiences.Where(x=>x.Enabled).TakeLast(4).Select(x=>new{x.ActivityId,x.Outcome,x.Scene})});
            foreach(var template in data.Templates.Where(x=>x.View==observation.View&&x.Scene==observation.Scene))
            {
                if(observation.Model=="space-marine"&&template.Steps.Any(x=>x.Action is "interact" or "dance"))continue;
                if(ActivityWorld?.Preflight(observation.View,new(template.Steps)) is not null||ActionPlanner.Preflight(new(template.Steps),Preferences) is not null)continue;
                options.Insert(0,new("template-"+template.Id,template.Title,"用户示范并保存的有限动作组合。",template.Steps));
            }
        }
        var ledger=Store.Get<AutonomyLedger>("autonomy-ledger");
        if(ledger is not null&&ledger.RejectUntil>DateTimeOffset.UtcNow)options.RemoveAll(x=>x.Id==ledger.RejectedActivity);
        return observation with{Options=options.Take(12).ToArray(),LifeContext=facts};
    }
    internal void RecordExperience(string activity,string kind,string outcome,SceneKind scene)
    {
        if(Preferences.RememberPlayExperiences)Store.RecordPlay(activity,kind,outcome,scene,DateTimeOffset.UtcNow);
    }
    private void InviteCompanion(string activity,string message,CompanionView view)
    {
        if(!Preferences.AllowAutonomyInvitations||Preferences.Quiet||Clock.Active||AnimationSuspended)return;
        var ledger=Store.Get<AutonomyLedger>("autonomy-ledger")??new();
        if(!ledger.Invite(DateTimeOffset.UtcNow,activity))return;
        Store.Set("autonomy-ledger",ledger);CompanionInvitation=message;_invitationActivity=activity;_invitationView=view;Changed?.Invoke();
    }
    internal void DeclineInvitation(bool today)
    {
        var ledger=Store.Get<AutonomyLedger>("autonomy-ledger")??new();ledger.Reject(DateTimeOffset.UtcNow,_invitationActivity,today);
        Store.Set("autonomy-ledger",ledger);CompanionInvitation=null;Changed?.Invoke();
    }
    internal bool InvitationIsWorkBreak=>_invitationActivity=="work-break";
    internal void ClearInvitation(){CompanionInvitation=null;Changed?.Invoke();}
    internal async Task AcceptInvitation()
    {
        if(InvitationIsWorkBreak){ClearInvitation();return;}
        var view=_invitationView;CompanionInvitation=null;
        await ChooseActivity(view,"继续一起玩一个当前适合的短活动。");
    }
    internal void CompleteUserTask(string id,string status)
    {
        Store.SetUserTaskStatus(id,status,DateTimeOffset.Now);
        if(status=="done"){CompanionLifeStatus="你完成了一项待办，伙伴为你庆祝。";InviteCompanion("work-break","忙完了，要不要一起放松两分钟？",CompanionView.Room);}
    }
    private void CompletedFocusInvitation()=>InviteCompanion("work-break","专注完成了，要不要一起放松两分钟？",CompanionView.Room);

    internal List<CompanionCommand> DemonstrationSteps {get;}=[];
    internal CompanionView DemonstrationView {get;private set;}
    internal SceneKind DemonstrationScene {get;private set;}
    internal async Task Demonstrate(CompanionView view,string optionId)
    {
        await TakeForegroundAsync();if(Busy)throw new OperationFailureException("请先结束当前操作。");
        var world=ActivityWorld??throw new OperationFailureException("场景不可用。");var observation=world.Observe(view);
        var option=observation.Options.FirstOrDefault(x=>x.Id==optionId)??throw new OperationFailureException("动作已不可用。");
        if(option.Steps.Any(x=>x.Action is not ("go" or "interact" or "stroll" or "dance")))throw new OperationFailureException("事件链暂不作为可示范步骤。");
        if(DemonstrationSteps.Count>0&&(DemonstrationView!=view||DemonstrationScene!=observation.Scene))throw new OperationFailureException("请先清空上一段示范。");
        if(DemonstrationSteps.Count+option.Steps.Count>3)throw new OperationFailureException("最多示范3步，请先保存或清空。");
        using var request=new CancellationTokenSource(TimeSpan.FromSeconds(30));_request=request;_selectionView=view;int epoch=++_epoch;SyncTimer();
        try
        {
            var results=await ExecuteChoice(world,view,observation,option,request.Token,epoch);
            if(results.Count!=option.Steps.Count||results.Any(x=>x.Code!=ActionResultCode.Completed))throw new OperationFailureException(ResultText(results));
            DemonstrationView=view;DemonstrationScene=observation.Scene;DemonstrationSteps.AddRange(option.Steps);
            CompanionLifeStatus="已真实完成并记录示范 "+DemonstrationSteps.Count+" 步；尚未保存。";
        }
        finally{_selectionView=null;_request=null;SyncTimer();Changed?.Invoke();}
    }
    internal void SaveDemonstration(string title)
    {
        if(Busy)throw new OperationFailureException("请先结束示范。");var data=Store.CompanionLife();
        data.Templates.Add(new(Guid.NewGuid().ToString("N"),title.Trim(),DemonstrationView,DemonstrationScene,DemonstrationSteps.ToList()));
        Store.SaveCompanionLife(data);DemonstrationSteps.Clear();Changed?.Invoke();
    }
    internal async Task PlayTemplate(string id)
    {
        await TakeForegroundAsync();if(Busy)throw new OperationFailureException("请先结束当前操作。");
        var template=Store.CompanionLife().Templates.FirstOrDefault(x=>x.Id==id)??throw new OperationFailureException("组合已删除。");
        var world=ActivityWorld??throw new OperationFailureException("场景不可用。");var observation=world.Observe(template.View);
        if(!observation.Available||observation.Scene!=template.Scene)throw new OperationFailureException("伙伴或场景已隐藏/变化，不能重放。");
        var option=new ActivityOption("template-local",template.Title,"本地保存的示范组合。",template.Steps);
        using var request=new CancellationTokenSource(TimeSpan.FromSeconds(30));_request=request;_selectionView=template.View;int epoch=++_epoch;SyncTimer();
        try
        {
            var result=await ExecuteChoice(world,template.View,observation,option,request.Token,epoch);
            if(result.Count!=template.Steps.Count||result.Any(x=>x.Code!=ActionResultCode.Completed))throw new OperationFailureException(ResultText(result));
            CompanionLifeStatus="已在本地完成示范组合："+template.Title;RecordExperience(template.Id,"demonstration","completed",template.Scene);
        }
        finally{_selectionView=null;_request=null;SyncTimer();Changed?.Invoke();}
    }
}
