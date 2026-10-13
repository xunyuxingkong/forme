using Forme.Core;
namespace Forme.App;
internal interface ICooperativePlayWorld
{
    void BeginHide(string hiddenId);
    HideObservation ObserveHide(string hint);
    Task<HideCheckResult> CheckHide(string id,CancellationToken token);
    BoatLayout CurrentBoat();
    void ConfirmBoat(BoatLayout expected,BoatLayout proposed);
}
internal sealed partial class DesktopHost : ICooperativePlayWorld
{
    void ICooperativePlayWorld.BeginHide(string id)
    {
        if(House is null||House.CurrentPage!="hide"||!House.ActivityVisible)throw new OperationFailureException("请在显示中的小屋藏物页开始。");
        House.BeginAiHide(id);
    }
    HideObservation ICooperativePlayWorld.ObserveHide(string hint)=>House?.ObserveAiHide(hint)??throw new OperationFailureException("小屋已关闭。");
    Task<HideCheckResult> ICooperativePlayWorld.CheckHide(string id,CancellationToken token)=>House?.CheckAiHide(id,token)??Task.FromResult(HideCheckResult.Cancelled);
    BoatLayout ICooperativePlayWorld.CurrentBoat()=>House?.CurrentCooperativeBoat()??throw new OperationFailureException("请打开纸船页。");
    void ICooperativePlayWorld.ConfirmBoat(BoatLayout expected,BoatLayout proposed)=>House?.ConfirmCooperativeBoat(expected,proposed);
}
internal sealed partial class MainWindow
{
    internal void BeginAiHide(string id)=>_room.StartHide(false,id,true);
    internal HideObservation ObserveAiHide(string hint)=>_room.ObserveAiHide(hint);
    internal Task<HideCheckResult> CheckAiHide(string id,CancellationToken token)=>_room.CheckAiHide(id,token);
    internal BoatLayout CurrentCooperativeBoat(){if(_page!="boat"||!_room.ActivityVisible)throw new OperationFailureException("请打开显示中的纸船页。");return _room.BoatSettings;}
    internal void ConfirmCooperativeBoat(BoatLayout expected,BoatLayout proposed)
    {
        if(_page!="boat"||!_room.ActivityVisible||!BoatPartner.Same(expected,_room.BoatSettings))throw new OperationFailureException("纸船布置已变化，请重新合作。");
        _room.ConfigureBoat(proposed);_room.StartBoat();
    }
}
internal sealed partial class Room3DView
{
    private bool _aiHide,_aiHideFound;
    internal HideObservation ObserveAiHide(string hint)
    {
        if(!ActivityVisible||!_aiHide||(_hideMode!="pet"&&!_aiHideFound))throw new OperationFailureException("这局AI藏物已结束。");
        return new(ActivitySubject,_activityGeneration,CurrentWorld.Items.Select(x=>new HideCandidate(x.Id,x.Kind,
            (x.Z<0?"后半边":"前半边")+(x.X<0?"偏左":"偏右"),LivingWorld.Approach(x,_travel) is not null)).ToArray(),_hideChecked.ToArray(),hint);
    }
    internal async Task<HideCheckResult> CheckAiHide(string id,CancellationToken token)
    {
        if(_aiHideFound)return HideCheckResult.Found;
        if(!ActivityVisible||!_aiHide||_hideMode!="pet")return HideCheckResult.Cancelled;
        var item=CurrentWorld.Items.FirstOrDefault(x=>x.Id==id);if(item is null||_hideChecked.Contains(id))return HideCheckResult.Unreachable;
        var point=LivingWorld.Approach(item,_travel);if(point is null)return HideCheckResult.Unreachable;
        int generation=_activityGeneration;
        _hideChecked.Add(id);_pendingInteraction="search:"+id;_moveTarget=point;
        _animator.MoveTo(point.Value);_hint.Text="伙伴去检查"+LivingWorld.Kind(item.Kind).Name;PaintWorld();
        if(!_travel.Moving){_pendingInteraction=null;FinishSearch(id);}
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(20));
        while(_travel.Moving||_pendingInteraction is not null||_animator.Reacting)
        {
            deadline.Token.ThrowIfCancellationRequested();
            if(!ActivityVisible||generation!=_activityGeneration||!_c.Preferences.AllowPlayControl)return HideCheckResult.Cancelled;
            await Task.Delay(80,deadline.Token);
        }
        return _aiHideFound?HideCheckResult.Found:_hideMode=="pet"?HideCheckResult.Missing:HideCheckResult.Cancelled;
    }
}
internal sealed partial class Controller
{
    private string _hidePublicHint="";
    private int _hideHintRevision;
    private bool _hideHintUsed;
    internal bool AiHideRunning {get;private set;}
    internal bool CanReviseHideHint=>AiHideRunning&&!_hideHintUsed;
    internal BoatLayout? CooperativeBoatBefore {get;private set;}
    internal BoatLayout? CooperativeBoatProposal {get;private set;}
    internal string CooperativeBoatSubject {get;private set;}="";
    internal void GiveHideHint(string hint)
    {
        if(!AiHideRunning||_hideHintUsed)throw new OperationFailureException("本局只能调整一次提示；可结束后重新开始。");
        if(string.IsNullOrWhiteSpace(hint)||hint.Length>300)throw new InvalidDataException("提示需为1–300字。");
        _hidePublicHint=hint.Trim();_hideHintRevision++;_hideHintUsed=true;CompanionLifeStatus="收到提示，检查完当前位置后调整剩余搜索路线。";Tick?.Invoke();
    }
    private void RequirePlaySharing()
    {
        if(!Preferences.SharePlayState||!Preferences.ShareGameState||!AiClient.SameEndpoint(Preferences.GameStateEndpoint,Preferences.Endpoint))throw new OperationFailureException("请在伙伴生活设置开启游戏状态和合作玩法状态分享。");
        if(!Preferences.AllowPlayControl)throw new OperationFailureException("请开启玩法权限。");
        if(!Secrets.Exists)throw new OperationFailureException("请先连接AI。");
    }
    internal async Task RunAiHide(string hiddenId,string hint)
    {
        await TakeForegroundAsync();if(Busy)throw new OperationFailureException("请先结束当前操作。");
        RequirePlaySharing();if(hint.Length>300)throw new InvalidDataException("提示最多300字。");
        var world=ActivityWorld as ICooperativePlayWorld??throw new OperationFailureException("当前场景不支持合作藏物。");
        using var request=new CancellationTokenSource(TimeSpan.FromSeconds(120));
        _request=request;_selectionView=CompanionView.Room;SelectedActivityId=Guid.NewGuid().ToString("N");SelectedActivityState=CompanionActivityState.Planning;
        AiHideRunning=true;_hidePublicHint=hint.Trim();_hideHintRevision=0;_hideHintUsed=false;int epoch=++_epoch;SyncTimer();Changed?.Invoke();
        bool found=false;
        try
        {
            _applyingSelection=true;try{world.BeginHide(hiddenId);}finally{_applyingSelection=false;}
            var initial=world.ObserveHide(_hidePublicHint);
            int requests=0;
            while(requests<2)
            {
                request.Token.ThrowIfCancellationRequested();RequirePlaySharing();
                int hintVersion=_hideHintRevision;
                var observation=world.ObserveHide(_hidePublicHint);
                if(observation.Subject!=initial.Subject||observation.Revision!=initial.Revision)throw new OperationFailureException("藏物场景已变化，本局停止。");
                using var planning=CancellationTokenSource.CreateLinkedTokenSource(request.Token);planning.CancelAfter(TimeSpan.FromSeconds(25));
                CompanionLifeStatus=requests==0?"AI正在安排搜索顺序…":"AI正在根据提示调整路线…";Tick?.Invoke();
                requests++;
                var reply=await Ai.SendAsync(Preferences with{},Secrets.Read()!,HideStrategy.Build(Preferences,observation),_=>{},planning.Token);
                request.Token.ThrowIfCancellationRequested();if(epoch!=_epoch)throw new OperationCanceledException(request.Token);
                if(reply.LengthLimited)throw new InvalidDataException("搜索策略被截断，没有执行。");
                var strategy=HideStrategy.Parse(reply.Content,observation);
                if(hintVersion!=_hideHintRevision)continue;
                SelectedActivityState=CompanionActivityState.Executing;
                foreach(string id in strategy)
                {
                    request.Token.ThrowIfCancellationRequested();RequirePlaySharing();
                    if(hintVersion!=_hideHintRevision)break;
                    Task<HideCheckResult> checking;
                    _applyingSelection=true;try{checking=world.CheckHide(id,request.Token);}finally{_applyingSelection=false;}
                    var result=await checking;
                    if(result==HideCheckResult.Found){found=true;break;}
                    if(result==HideCheckResult.Cancelled)throw new OperationCanceledException(request.Token);
                    CompanionLifeStatus=result==HideCheckResult.Unreachable?"该位置不可达，继续检查其它候选。":"这里没有，继续按AI路线寻找。";Tick?.Invoke();
                }
                if(found||hintVersion==_hideHintRevision)break;
            }
            SelectedActivityState=found?CompanionActivityState.Completed:CompanionActivityState.NoAction;
            CompanionLifeStatus=found?"伙伴找到小球了，AI合作藏物完成。":"本次策略检查完毕，尚未找到；可以结束并换提示重玩。";
            RecordExperience("hide","hide",found?"found":"not-found",SceneKind.Indoor);
        }
        catch(Exception ex) when(OperationErrors.Expected(ex))
        {
            SelectedActivityState=ex is OperationCanceledException?CompanionActivityState.Cancelled:CompanionActivityState.Failed;
            CompanionLifeStatus=ex is OperationCanceledException?"合作藏物已停止。":OperationErrors.Message(ex);
        }
        finally
        {
            if(epoch==_epoch&&ReferenceEquals(_request,request)){request.Cancel();_applyingSelection=true;try{ActivityWorld?.Stop(CompanionView.Room);}finally{_applyingSelection=false;}}
            AiHideRunning=false;_selectionView=null;_request=null;SyncTimer();Changed?.Invoke();
        }
    }
    internal async Task ProposeBoat(string dock)
    {
        await TakeForegroundAsync();if(Busy)throw new OperationFailureException("请先结束当前操作。");RequirePlaySharing();
        var world=ActivityWorld as ICooperativePlayWorld??throw new OperationFailureException("当前场景不可用。");
        var before=world.CurrentBoat();var observed=ActivityWorld!.Observe(CompanionView.Room);
        using var request=new CancellationTokenSource(TimeSpan.FromSeconds(25));_request=request;_selectionView=CompanionView.Room;
        int epoch=++_epoch;SelectedActivityId=Guid.NewGuid().ToString("N");SelectedActivityState=CompanionActivityState.Planning;SyncTimer();
        CompanionLifeStatus="AI在比较第二片叶子的导流方案…";Tick?.Invoke();
        try
        {
            var options=BoatPartner.Options(before);
            var fake=observed with{Options=options.Select(x=>new ActivityOption(x.Id,PaperBoat.DockName(x.Dock),"模拟结果",[])).ToArray()};
            var turns=BoatPartner.Build(Preferences,before,dock);if(turns.Sum(AiClient.Estimate)>Preferences.ContextBudget)throw new InvalidDataException("共同纸船方案超过文字预算。");
            var reply=await Ai.SendAsync(Preferences with{},Secrets.Read()!,turns,_=>{},request.Token);
            request.Token.ThrowIfCancellationRequested();if(epoch!=_epoch)throw new OperationCanceledException(request.Token);
            if(reply.LengthLimited)throw new InvalidDataException("共同纸船方案被截断。");
            var choice=ActivitySelection.Parse(reply.Content,fake);
            if(choice.Id is null){SelectedActivityState=CompanionActivityState.NoAction;CompanionLifeStatus=choice.Reason;}
            else
            {
                if(!BoatPartner.Same(before,world.CurrentBoat())||ActivityWorld.Observe(CompanionView.Room).SubjectId!=observed.SubjectId)throw new OperationFailureException("纸船布置已改变，建议作废。");
                CooperativeBoatBefore=before;CooperativeBoatProposal=options.Single(x=>x.Id==choice.Id).Layout;CooperativeBoatSubject=observed.SubjectId;
                SelectedActivityState=CompanionActivityState.WaitingForUser;_boatProposalAt=DateTimeOffset.UtcNow;
                CompanionLifeStatus="AI建议已生成，预估抵达"+PaperBoat.DockName(options.Single(x=>x.Id==choice.Id).Dock)+"。"+choice.Reason+" 点击确认后才采用并放船。";
            }
        }
        catch(Exception ex) when(OperationErrors.Expected(ex)){CooperativeBoatProposal=null;SelectedActivityState=CompanionActivityState.Failed;CompanionLifeStatus=OperationErrors.Message(ex);}
        finally{_selectionView=null;_request=null;SyncTimer();Changed?.Invoke();}
    }
    private DateTimeOffset _boatProposalAt;
    internal void ConfirmBoatProposal()
    {
        RequirePlaySharing();
        if(CooperativeBoatBefore is null||CooperativeBoatProposal is null||DateTimeOffset.UtcNow-_boatProposalAt>TimeSpan.FromMinutes(10)||
            ActivityWorld?.Observe(CompanionView.Room).SubjectId!=CooperativeBoatSubject)throw new OperationFailureException("共同纸船建议已失效。");
        (ActivityWorld as ICooperativePlayWorld)!.ConfirmBoat(CooperativeBoatBefore,CooperativeBoatProposal);
        CooperativeBoatProposal=null;SelectedActivityState=CompanionActivityState.Executing;CompanionLifeStatus="共同纸船已出发，等待真实到达结果。";Changed?.Invoke();
    }
    internal void CompleteCooperativeBoat(string dock)
    {
        if(SelectedActivityState!=CompanionActivityState.Executing)return;
        SelectedActivityState=CompanionActivityState.Completed;CompanionLifeStatus="共同纸船实际抵达"+PaperBoat.DockName(dock)+"。";RecordExperience("cooperative-boat","boat",dock,SceneKind.Indoor);Changed?.Invoke();
    }
}
