using Forme.Core;

namespace Forme.App;

internal sealed partial class Controller
{
    private bool _goalExecuting;
    internal bool CanPrepareGoalReplan(GoalRun run)=>!Busy&&Preferences.AllowGoalPlans&&Session?.Id==run.SessionId&&GoalProtocol.CanReplan(run);
    internal bool CanReconcileGoalBoat(GoalRun run)=>!Busy&&Store.CanReconcileGoalBoat(run);
    internal void ReconcileGoalBoat(string id)
    {
        if(Busy||!Store.ReconcileGoalBoat(id,DateTimeOffset.Now))throw new OperationFailureException("没有找到与该目标局次一致的到岸回执，请刷新后核对。");
        if(Store.Goal(id) is {} run){ChatStatus=GoalSummary(run);CooperativeBoatResolved(run);}
        Changed?.Invoke();
    }
    internal void PrepareGoalReplan(string id)
    {
        if(Busy||!Preferences.AllowGoalPlans)throw new OperationFailureException("请先停止当前任务，并在设置中开启跨组件目标计划。");
        var run=Store.Goal(id);if(run is null||!CanPrepareGoalReplan(run))throw new OperationFailureException("该目标状态不支持重规划；只有没有步骤执行、且首个移动步骤明确遇到路径阻挡或目标不可用时，才可请求一次调整。");
        string source=Store.GoalSource(run)??throw new OperationFailureException("找不到原目标消息，无法安全重规划。");
        if(!Store.BeginGoalReplan(id,DateTimeOffset.Now))throw new OperationFailureException("该目标的重规划额度已用完或状态已改变。");
        if(source.Length>2000)source=source[..2000];
        string failure=run.Steps[0].Evidence?.EndsWith("TargetUnavailable",StringComparison.Ordinal)==true?"目标当前不可用":"路径被阻挡";
        Draft=$"请为以下未完成目标重新规划一次。之前没有步骤执行；首个移动步骤收到本机回执“{failure}”，其余步骤均未执行。请结合这条信息重新判断可行方案，只给出最多三步的新计划；不要声称已经执行或完成。\n原目标：{source}";
        ChatStatus="AI重规划草稿已准备；请检查后手动发送。不会自动请求或执行。";Changed?.Invoke();
    }
    private async Task<string> ExecuteGoalPlan(GoalPlan plan,ChatMessage user,ChatCapabilitySnapshot issued,CancellationToken cancellation)
    {
        if(!issued.GoalPlans||!Preferences.AllowGoalPlans)throw new OperationFailureException("请先在角色与权限设置开启跨组件目标计划；本次没有执行。");
        if(_goalPlay is not null||_boatProposalGoalId is not null)throw new OperationFailureException("请先完成或停止正在等待结果或确认的玩法目标；本次没有执行。");
        var commands=new ActionPlan(plan.Steps.Where(x=>x.Command is not null).Select(x=>x.Command!).ToArray());
        var denied=ChatCapabilitySnapshot.Validate(issued,CaptureChatCapabilities(),commands);
        if(denied is null)denied=GoalProtocol.Preflight(plan,Preferences,ActionPreflight);
        var game=plan.Steps.Last().Command;
        if(denied is null&&game is not null&&GoalPlay.Kind(game) is not null)
            denied=GoalPlayWorld is {} play?play.PreflightGoalPlay(game):ActionResult.Failure(ActionResultCode.SceneUnavailable,"当前宿主不支持玩法结果核对，整组未执行。");
        Store.BeginGoal(user.Id,user.SessionId,plan,DateTimeOffset.Now,issued.Endpoint,issued.Model);
        FocusTaskCard("goal");
        _goalExecuting=true;using var limit=CancellationTokenSource.CreateLinkedTokenSource(cancellation);limit.CancelAfter(TimeSpan.FromSeconds(30));
        var expected=issued;var allActions=new List<ActionResult>();
        try
        {
            if(denied is not null)
            {
                Store.StartGoalStep(user.Id,0,DateTimeOffset.Now);Store.FinishGoalStep(user.Id,0,GoalStepState.Failed,"preflight:"+denied.Code,DateTimeOffset.Now);
                return "目标整体预检未通过，未执行任何步骤。\n"+denied.UserMessage;
            }
            for(int i=0;i<plan.Steps.Count;i++)
            {
                limit.Token.ThrowIfCancellationRequested();
                if(!Preferences.AllowGoalPlans)throw new OperationCanceledException(limit.Token);
                if(ChatCapabilitySnapshot.Validate(expected,CaptureChatCapabilities(),new([])) is {} changed)throw new ActionFailureException(changed.Code,changed.UserMessage);
                var step=plan.Steps[i];Store.StartGoalStep(user.Id,i,DateTimeOffset.Now);
                ChatStatus=$"正在执行目标 {i+1}/{plan.Steps.Count}："+GoalProtocol.Label(step.Capability)+GoalView(step.Command?.View);LiveReply=ChatStatus;Changed?.Invoke();
                limit.Token.ThrowIfCancellationRequested();
                if(step.Work is {} work)
                {
                    string evidence=Store.ApplyGoalWork(user.Id,i,work,DateTimeOffset.Now);
                    Store.FinishGoalStep(user.Id,i,GoalStepState.Completed,evidence,DateTimeOffset.Now);
                }
                else
                {
                    if(GoalPlay.Supported(step.Command!))EndProject();
                    var results=await ExecuteIntents([ActionPlanner.FromCommand(step.Command!)],limit.Token,expected);allActions.AddRange(results);ActionResults=allActions.ToArray();
                    var final=results.LastOrDefault();bool complete=final?.Code==ActionResultCode.Completed;
                    if(complete&&GoalPlay.Supported(step.Command!))
                    {
                        AwaitGoalPlay(user.Id,i,step.Command!,limit.Token);return GoalSummary(Store.Goal(user.Id)!);
                    }
                    Store.FinishGoalStep(user.Id,i,complete?GoalStepState.Completed:final?.Code==ActionResultCode.Cancelled?GoalStepState.Cancelled:GoalStepState.Failed,final?.Code.ToString()??"missing-receipt",DateTimeOffset.Now);
                    if(!complete)return GoalSummary(Store.Goal(user.Id)!)+"\n"+(final?.UserMessage??"没有取得动作完成回执。");
                    var current=CaptureChatCapabilities();var command=step.Command!;
                    expected=expected with{
                        InitialScene=command.Action=="scene"?(command.Value=="outdoor"?SceneKind.Outdoor:SceneKind.Indoor):expected.InitialScene,
                        Actors=command.Action=="scene"?current.Actors:expected.Actors.Select(actor=>actor.View==command.View?current.Actors.Single(x=>x.View==actor.View):actor).ToArray(),
                        Games=command.View==CompanionView.Room?current.Games:expected.Games
                    };
                }
                Changed?.Invoke();await Task.Yield();
            }
            return GoalSummary(Store.Goal(user.Id)!);
        }
        catch(OperationCanceledException)
        {
            return StopGoalSummary(user.Id,cancellation.IsCancellationRequested)+"\n已停止；已保存的产物保留，未自动重试。";
        }
        catch(Exception ex) when(OperationErrors.Expected(ex))
        {
            return StopGoalSummary(user.Id,false)+"\n"+OperationErrors.Message(ex);
        }
        finally{_goalExecuting=false;Changed?.Invoke();}
    }
    private string StopGoalSummary(string id,bool cancelled)
    {
        try
        {
            Store.StopGoal(id,cancelled,DateTimeOffset.Now);
            return Store.Goal(id) is {} run?GoalSummary(run):"会话或执行记录已删除，本次停止；已生成的本机产物保留。";
        }
        catch(Exception ex) when(OperationErrors.Expected(ex))
        {return "执行记录未能写回，已产生的本机结果需核对；本次不会自动重放。";}
    }
    internal static string GoalStepLabel(GoalStepState state)=>state switch{
        GoalStepState.Pending=>"待执行",GoalStepState.Running=>"执行中",GoalStepState.Completed=>"已完成",GoalStepState.Failed=>"失败",
        GoalStepState.Cancelled=>"已停止",GoalStepState.Skipped=>"未执行",GoalStepState.WaitingForPlay=>"玩法已进入，等待本局结果",GoalStepState.WaitingForUser=>"等待你确认，尚未执行",_=>"中断，需核对"};
    internal static string GoalSummary(GoalRun run)
    {
        string heading=run.State switch{GoalRunState.Completed=>"目标已完成。",GoalRunState.Ready or GoalRunState.Running=>"目标执行中。",GoalRunState.WaitingForPlay=>"已进入玩法，等待你参与及本局真实结果。",GoalRunState.WaitingForUser=>"等待你确认纸船建议，尚未放船。",GoalRunState.Cancelled=>"目标已停止。",GoalRunState.Interrupted=>"目标曾中断，未自动恢复。",_=>"目标未全部完成。"};
        return heading+"\n"+string.Join("\n",run.Steps.Select(x=>$"{x.Index+1}. {GoalProtocol.Label(x.Capability)}{GoalView(x.View)} · {GoalStepLabel(x.State)}"+GoalEvidence(x.Evidence)));
    }
    private static string GoalView(CompanionView? view)=>view switch{CompanionView.Room=>"（小屋/户外）",CompanionView.Desktop=>"（桌面）",_=>""};
    private static string GoalEvidence(string? evidence)
    {
        if(evidence=="plan-ready")return " · 搜索顺序已生成";
        if(evidence=="not-found")return " · 本次未找到";
        if(evidence=="no-action")return " · AI选择不行动";
        if(evidence=="no-proposal")return " · 没有可执行建议";
        if(evidence?.StartsWith("proposed:",StringComparison.Ordinal)==true)return " · "+(evidence[9..].Contains(':')?"目标"+PaperBoat.DockName(evidence[9..].Split(':',2)[0])+"，":"")+"预计抵达"+PaperBoat.DockName(evidence[9..].Split(':',2)[^1])+"，尚未采用";
        if(evidence=="play:hide:found")return " · 本局找到小球";
        if(evidence?.StartsWith("recovered-play:boat:",StringComparison.Ordinal)==true)return " · 已核对本局到岸："+PaperBoat.DockName(evidence[20..]);
        if(evidence?.StartsWith("play:boat:",StringComparison.Ordinal)==true&&evidence!="play:boat:waiting")return " · 实际抵达"+PaperBoat.DockName(evidence[10..]);
        if(evidence?.StartsWith("found:",StringComparison.Ordinal)==true&&int.TryParse(evidence[6..],out int count))return $" · 找到 {count} 条";
        if(evidence?.StartsWith("today:",StringComparison.Ordinal)==true&&int.TryParse(evidence[6..],out int today))return $" · 今日 {today} 条";
        return evidence=="saved"?" · 已在本机保存":evidence=="recovered-artifact"?" · 本机产物已核对，未重放":evidence?.StartsWith("preflight:",StringComparison.Ordinal)==true?" · 整体预检未通过":"";
    }
}
