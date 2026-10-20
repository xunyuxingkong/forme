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
                            string message = SuccessMessage(command);
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
    public static string SuccessMessage(CompanionCommand command)=>command.Action switch
    {
        "go"=>$"我到{TargetCatalog.Find(command.Value)?.Name??"那里"}啦，来看看～",
        "stroll" when command.Value=="lap1"=>"好呀～我在屏幕上绕了一圈，回来啦！",
        "stroll" when command.Value=="lap2"=>"好呀～我在屏幕上绕了两圈，回来啦！",
        "stroll" when command.Value=="run"=>"我跑了一会儿，感觉真畅快～",
        "stroll"=>"我散步回来啦，感觉真不错～",
        "dance" when command.Value=="spin"=>"转完一圈啦～还想看我跳什么吗？",
        "dance" when command.Value=="hop"=>"蹦蹦跳跳完成啦～好开心！",
        "dance"=>"我摇摆了一会儿，怎么样～",
        "pat" or "rub"=>"好舒服呀，谢谢你摸摸我～",
        "recall"=>"我回来啦～",
        "stop"=>"我停下来啦～",
        "scene" when command.Value=="indoor"=>"我回到小屋啦～",
        "scene"=>"我到户外啦，一起看看吧～",
        "weather"=>"天气换好啦，陪我看看现在的景色吧～",
        "light" when command.Value=="on"=>"灯亮起来啦，小屋暖和多啦～",
        "light"=>"灯关好啦～",
        "fireplace" when command.Value=="on"=>"壁炉点起来啦，好暖和～",
        "fireplace"=>"壁炉关好啦～",
        "interact"=>$"我和{TargetCatalog.Find(command.Value)?.Name??"它"}玩了一下，真有意思～",
        "idle" when command.Value=="sleep"=>"我睡一会儿啦，记得叫醒我哦～",
        "idle" when command.Value=="walk"=>"我开始自由走动啦，陪你待一会儿～",
        "idle" when command.Value=="run"=>"我开始自由跑动啦，活力满满～",
        "idle"=>"我乖乖待在这里陪你～",
        "model"=>"新样子换好啦，快看看我～",
        "toy" or "throw"=>"玩具准备好啦，来陪我玩吧～",
        "layout"=>"布置调整好啦，看看小屋是不是更舒服了～",
        "furniture"=>"家具预览好啦，看看这个位置合不合适～",
        "life"=>"好呀，我们开始这个小活动吧～",
        "hide"=>"好呀，藏物游戏开始啦～",
        "boat" when command.Value=="pause"=>"纸船先停一下，需要时我们再继续～",
        "boat" when command.Value=="retrieve"=>"纸船回来啦～",
        "boat"=>"纸船准备好啦，一起出发吧～",
        "boat-shape" or "boat-color" or "boat-wind" or "boat-leaf" or "boat-name"=>"纸船调整好啦，看看这次的样子～",
        "move"=>"我挪到新位置啦～",
        _=>"好呀，我已经帮你做好啦～"
    };
}
