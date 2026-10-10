using System.Diagnostics;

namespace Forme.Core;

public static class ActionExecutor
{
    public static async Task<IReadOnlyList<ActionResult>> Execute(ActionPlan plan, Func<Preferences> preferences,
        Func<ActionPlan,ActionResult?>? preflight, Func<CompanionCommand,ActionResult> start,
        Func<CompanionCommand,CancellationToken,Task<bool>>? wait, Action stop, CancellationToken cancellation,
        Action<ActionResult>? progress = null, TimeSpan? timeout = null)
    {
        var watch = Stopwatch.StartNew();
        if(ActionPlanner.Preflight(plan,preferences(),preflight) is {} denied) return [denied];
        var results = new List<ActionResult>();
        using var limit = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation,limit.Token);
        foreach(var command in plan.Steps)
        {
            ActionResult result;
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                // Permissions can be revoked while an earlier step is waiting.
                if(!CompanionCommands.Allowed(command,preferences()))
                    result = ActionResult.Failure(ActionResultCode.Unauthorized,"动作权限已关闭，剩余步骤未执行。",command);
                else
                {
                    result = start(command);
                    if(result.Code == ActionResultCode.Started)
                    {
                        progress?.Invoke(result);
                        if(wait is null)
                            result = ActionResult.Failure(ActionResultCode.Failed,"无法确认动作是否完成，后续步骤未执行。",command);
                        else if(!await wait(command,linked.Token).WaitAsync(linked.Token))
                            result = ActionResult.Failure(ActionResultCode.Interrupted,"动作被打断，后续步骤已停止。",command);
                        else
                        {
                            linked.Token.ThrowIfCancellationRequested();
                            string message = command.Action == "go" ? $"已经到{TargetCatalog.Find(command.Value)?.Name}啦。" : "伙伴动作已完成。";
                            result = new(ActionResultCode.Completed,command.Action,command.Value,message,watch.Elapsed);
                        }
                    }
                }
            }
            catch(OperationCanceledException)
            {result = ActionResult.Failure(limit.IsCancellationRequested && !cancellation.IsCancellationRequested ? ActionResultCode.Timeout : ActionResultCode.Cancelled,limit.IsCancellationRequested && !cancellation.IsCancellationRequested ? "动作超过30秒时限，已停止。" : "动作已经停止。",command);}
            catch(ActionFailureException ex) {result = ActionResult.Failure(ex.Code,ex.Message,command);}
            catch(Exception ex) when(OperationErrors.Expected(ex)) {result = ActionResult.Failure(ActionResultCode.Failed,"动作没有完成："+OperationErrors.Message(ex),command);}
            result = result with {Elapsed=watch.Elapsed}; results.Add(result); progress?.Invoke(result);
            if(!result.Success) {stop(); break;}
            if(command.Action=="stop") break;
        }
        return results;
    }
}
