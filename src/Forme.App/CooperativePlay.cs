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
        if(_boatProposalGoalId is not null)throw new OperationFailureException("请先确认或停止当前纸船建议。");
        var world=ActivityWorld as ICooperativePlayWorld??throw new OperationFailureException("当前场景不支持合作藏物。");
        if(GoalPlayWorld is null)throw new OperationFailureException("当前场景不支持合作结果核对。");
        var issued=Preferences with{};var goal=BeginCooperative("hide");
        using var request=new CancellationTokenSource(TimeSpan.FromSeconds(120));
        _request=request;_cooperativeRequest=true;_selectionView=CompanionView.Room;SelectedActivityId=goal.Id;SelectedActivityState=CompanionActivityState.Planning;
        AiHideRunning=true;_hidePublicHint=hint.Trim();_hideHintRevision=0;_hideHintUsed=false;int epoch=++_epoch;SyncTimer();Changed?.Invoke();
        bool found=false,declined=false;GoalPlayObservation? owned=null;
        try
        {
            request.Token.ThrowIfCancellationRequested();
            _applyingSelection=true;try{world.BeginHide(hiddenId);}finally{_applyingSelection=false;}
            var initial=world.ObserveHide(_hidePublicHint);
            owned=GoalPlayWorld.ObserveGoalPlay("hide");
            int requests=0;
            while(requests<2)
            {
                request.Token.ThrowIfCancellationRequested();RequirePlaySharing();
                if(Preferences.Endpoint!=issued.Endpoint||Preferences.Model!=issued.Model)throw new OperationCanceledException(request.Token);
                int hintVersion=_hideHintRevision;
                var observation=world.ObserveHide(_hidePublicHint);
                if(observation.Subject!=initial.Subject||observation.Revision!=initial.Revision)throw new OperationFailureException("藏物场景已变化，本局停止。");
                using var planning=CancellationTokenSource.CreateLinkedTokenSource(request.Token);planning.CancelAfter(TimeSpan.FromSeconds(25));
                CooperativeStatus(requests==0?"AI正在安排搜索顺序…":"AI根据已检查项与当前线索再规划一次…");
                var turns=HideStrategy.Build(issued,observation);requests=Store.ReserveCooperativeRequest(goal.Id,issued.Endpoint,issued.Model,DateTimeOffset.Now);Changed?.Invoke();
                var reply=await Ai.SendAsync(issued,Secrets.Read()!,turns,_=>{},planning.Token);
                request.Token.ThrowIfCancellationRequested();if(epoch!=_epoch)throw new OperationCanceledException(request.Token);
                if(reply.LengthLimited)throw new InvalidDataException("搜索策略被截断，没有执行。");
                var current=world.ObserveHide(_hidePublicHint);if(current.Subject!=initial.Subject||current.Revision!=initial.Revision)throw new OperationFailureException("本局已变化，旧搜索方案作废。");
                var strategy=HideStrategy.Parse(reply.Content,observation);
                if(hintVersion!=_hideHintRevision)continue;
                if(Store.Goal(goal.Id)!.Steps[0].State==GoalStepState.Running)
                {Store.FinishGoalStep(goal.Id,0,GoalStepState.Completed,"plan-ready",DateTimeOffset.Now);Store.StartGoalStep(goal.Id,1,DateTimeOffset.Now);}
                if(strategy.Count==0){declined=true;break;}
                SelectedActivityState=CompanionActivityState.Executing;
                CooperativeStatus("伙伴按AI方案实际检查，等待本机结果。");
                foreach(string id in strategy)
                {
                    request.Token.ThrowIfCancellationRequested();RequirePlaySharing();
                    if(hintVersion!=_hideHintRevision)break;
                    Task<HideCheckResult> checking;
                    _applyingSelection=true;try{checking=world.CheckHide(id,request.Token);}finally{_applyingSelection=false;}
                    var result=await checking;
                    if(result==HideCheckResult.Found)
                    {
                        var actual=GoalPlayWorld.ObserveGoalPlay("hide");
                        if(!GoalPlay.SameRound(owned,actual)||actual.Phase!=GoalPlayPhase.Completed||actual.Outcome!="found")throw new OperationFailureException("没有取得本局找到小球的真实结果。");
                        found=true;break;
                    }
                    if(result==HideCheckResult.Cancelled)throw new OperationCanceledException(request.Token);
                    CompanionLifeStatus=result==HideCheckResult.Unreachable?"该位置不可达，继续检查其它候选。":"这里没有，继续按AI路线寻找。";Tick?.Invoke();
                }
                if(found)break;
                var remaining=world.ObserveHide(_hidePublicHint);
                if(!remaining.Candidates.Any(x=>x.Reachable&&!remaining.Checked.Contains(x.Id)))break;
            }
            request.Token.ThrowIfCancellationRequested();
            var active=Store.Goal(goal.Id)!.Steps.First(x=>x.State==GoalStepState.Running);
            Store.FinishGoalStep(goal.Id,active.Index,found?GoalStepState.Completed:GoalStepState.Failed,found?"play:hide:found":declined?"no-action":"not-found",DateTimeOffset.Now);
            SelectedActivityState=found?CompanionActivityState.Completed:CompanionActivityState.NoAction;
            CooperativeStatus(found?"伙伴找到小球了，AI合作藏物完成。":declined?"AI选择不继续检查，本局没有找到。":"本局规划额度已用完或没有可达候选，尚未找到。可结束后换提示重玩。");
            RecordExperience("hide","hide",found?"found":"not-found",SceneKind.Indoor);
        }
        catch(Exception ex) when(OperationErrors.Expected(ex))
        {
            FailCooperative(goal.Id,ex);
            SelectedActivityState=ex is OperationCanceledException?CompanionActivityState.Cancelled:CompanionActivityState.Failed;
            CompanionLifeStatus=ex is OperationCanceledException?"合作藏物已停止。":OperationErrors.Message(ex);
        }
        finally
        {
            if(!found&&owned is not null){request.Cancel();_applyingSelection=true;try{GoalPlayWorld?.StopGoalPlay(owned);}finally{_applyingSelection=false;}}
            AiHideRunning=false;_cooperativeRequest=false;_selectionView=null;_request=null;SyncTimer();Changed?.Invoke();
        }
    }
    internal Task ProposeBoat(string dock)=>ProposeBoat(dock,null);
    private async Task ProposeBoat(string dock,BoatExperimentFeedback? feedback)
    {
        await TakeForegroundAsync();if(Busy)throw new OperationFailureException("请先结束当前操作。");RequirePlaySharing();
        var world=ActivityWorld as ICooperativePlayWorld??throw new OperationFailureException("当前场景不可用。");
        if(GoalPlayWorld is null)throw new OperationFailureException("当前场景不支持本局结果核对。");
        if(_goalPlay is not null)throw new OperationFailureException("请先完成或停止正在等待结果的玩法目标。");
        var before=world.CurrentBoat();var observed=ActivityWorld!.Observe(CompanionView.Room);
        var game=GoalPlayWorld.ObserveGoalPlay("boat");
        if(game.Phase is not (GoalPlayPhase.Ready or GoalPlayPhase.Completed))throw new OperationFailureException("请在显示中的纸船准备页面请求建议。");
        var issued=Preferences with{};var turns=BoatPartner.Build(issued,before,dock,feedback);if(turns.Sum(AiClient.Estimate)>issued.ContextBudget)throw new InvalidDataException("共同纸船方案超过文字预算。");
        CancelBoatProposal();var goal=BeginCooperative("boat");
        using var request=new CancellationTokenSource(TimeSpan.FromSeconds(25));_request=request;_selectionView=CompanionView.Room;
        _cooperativeRequest=true;int epoch=++_epoch;SelectedActivityId=goal.Id;SelectedActivityState=CompanionActivityState.Planning;SyncTimer();
        CooperativeStatus(feedback is null?"AI在比较第二片叶子的导流方案…":"AI正在结合上次实际到岸结果比较新方案…");
        try
        {
            var options=BoatPartner.Options(before);
            var fake=observed with{Options=options.Select(x=>new ActivityOption(x.Id,PaperBoat.DockName(x.Dock),"模拟结果",[])).ToArray()};
            Store.ReserveCooperativeRequest(goal.Id,issued.Endpoint,issued.Model,DateTimeOffset.Now);Changed?.Invoke();
            var reply=await Ai.SendAsync(issued,Secrets.Read()!,turns,_=>{},request.Token);
            request.Token.ThrowIfCancellationRequested();if(epoch!=_epoch)throw new OperationCanceledException(request.Token);
            RequirePlaySharing();if(Preferences.Endpoint!=issued.Endpoint||Preferences.Model!=issued.Model)throw new OperationCanceledException(request.Token);
            if(reply.LengthLimited)throw new InvalidDataException("共同纸船方案被截断。");
            var choice=ActivitySelection.Parse(reply.Content,fake);
            if(choice.Id is null){Store.FinishGoalStep(goal.Id,0,GoalStepState.Failed,"no-proposal",DateTimeOffset.Now);SelectedActivityState=CompanionActivityState.NoAction;CooperativeStatus("AI没有提出可执行建议，未修改路线或放船。");}
            else
            {
                if(!BoatPartner.Same(before,world.CurrentBoat())||ActivityWorld.Observe(CompanionView.Room).SubjectId!=observed.SubjectId||GoalPlayWorld.ObserveGoalPlay("boat")!=game)throw new OperationFailureException("纸船布置或本局已改变，建议作废。");
                var selected=options.Single(x=>x.Id==choice.Id);
                Store.FinishGoalStep(goal.Id,0,GoalStepState.Completed,"proposed:"+dock+":"+selected.Dock,DateTimeOffset.Now);Store.AwaitGoalConfirmation(goal.Id,DateTimeOffset.Now);
                CooperativeBoatBefore=before;CooperativeBoatProposal=options.Single(x=>x.Id==choice.Id).Layout;CooperativeBoatSubject=observed.SubjectId;
                _boatProposalGoalId=goal.Id;_boatProposalPreferences=issued;_boatProposalObservation=game;
                SelectedActivityState=CompanionActivityState.WaitingForUser;_boatProposalAt=DateTimeOffset.UtcNow;
                CooperativeStatus("AI建议已生成，预估抵达"+PaperBoat.DockName(options.Single(x=>x.Id==choice.Id).Dock)+"。"+choice.Reason+" 点击确认后才采用并放船。");
            }
        }
        catch(Exception ex) when(OperationErrors.Expected(ex)){ClearBoatProposal();FailCooperative(goal.Id,ex);SelectedActivityState=ex is OperationCanceledException?CompanionActivityState.Cancelled:CompanionActivityState.Failed;CooperativeStatus(OperationErrors.Message(ex));}
        finally{_cooperativeRequest=false;_selectionView=null;_request=null;SyncTimer();Changed?.Invoke();}
    }
    private DateTimeOffset _boatProposalAt;
    internal void ConfirmBoatProposal()
    {
        if(Busy)throw new OperationFailureException("请先等待当前请求结束。");
        RequirePlaySharing();
        ReconcileCooperativeProposal();
        string id=_boatProposalGoalId??throw new OperationFailureException("共同纸船建议已失效。");
        var before=CooperativeBoatBefore!;var proposed=CooperativeBoatProposal!;
        if(Store.Goal(id)?.State!=GoalRunState.WaitingForUser)throw new OperationFailureException("建议状态已变化，没有重复放船。");
        Store.StartGoalStep(id,1,DateTimeOffset.Now);ClearBoatProposal();
        using var launch=new CancellationTokenSource();_request=launch;_cooperativeRequest=true;
        try
        {
            (ActivityWorld as ICooperativePlayWorld)!.ConfirmBoat(before,proposed);
            SelectedActivityId=id;SelectedActivityState=CompanionActivityState.Executing;
            AwaitGoalPlay(id,1,new("boat","start",View:CompanionView.Room),launch.Token);
            if(Store.Goal(id)?.State==GoalRunState.WaitingForPlay)CooperativeStatus("共同纸船已出发，等待真实到达结果。");
        }
        catch(Exception ex) when(OperationErrors.Expected(ex)){FailCooperative(id,ex);CooperativeStatus("放船结果未能确认，请核对本局；不会自动重放。");throw;}
        finally{_request=null;_cooperativeRequest=false;SyncTimer();Changed?.Invoke();}
    }
}
