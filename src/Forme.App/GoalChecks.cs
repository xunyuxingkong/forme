#if FORME_DEVELOPMENT_TOOLS
using Forme.Core;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Forme.App;

internal static class GoalChecks
{
    private const string Plan="[{\"intent\":\"CreateNote\",\"title\":\"会议准备\",\"content\":\"private-goal-body\"},{\"type\":\"Stroll\",\"mode\":\"walk\",\"view\":\"desktop\"},{\"intent\":\"CreateTask\",\"title\":\"确认会议\",\"description\":\"检查资料\"}]";
    private static string Envelope(string plan)=>GoalProtocol.Marker+"\n"+plan+"\n"+new string((char)96,3);
    public static async Task Run()
    {
        foreach(string scenario in new[]{"success","off","invalid-late","denied","failed-motion","replan","target-unavailable","preflight-target-unavailable","cancel","revoke","stale","between-steps","delete-session","truncated","mixed-markers","attachments","same-view","query"})
        {
            var transport=new Handler{Reply=Envelope(Plan),Truncated=scenario=="truncated"};
            string root=Path.GetFullPath(Path.Combine("artifacts","goal-checks",Guid.NewGuid().ToString("N")));
            using var c=new Controller(root,new AiClient(transport));
            c.SavePreferences(new Preferences{Onboarded=true,Endpoint="https://goals.invalid/v1",Model="fake-goals",ContextBudget=8192,AllowPetControl=scenario!="denied",AllowGoalPlans=scenario!="off"});
            c.Secrets.Save("isolated-test-key");var world=new World();c.ActivityWorld=world;c.CurrentScene=()=>SceneKind.Desktop;
            c.ActionPreflight=_=>scenario=="preflight-target-unavailable"?ActionResult.Failure(ActionResultCode.TargetUnavailable,"模拟目标当前不可用"):null;c.StopCommands=()=>{};
            int starts=0;var enteredMotion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            c.ActionHandler=command=>{
                starts++;if(scenario=="failed-motion"||scenario=="replan"&&starts==1)return ActionResult.Failure(ActionResultCode.PathBlocked,"模拟路径被阻挡",command);
                if(scenario=="target-unavailable"&&starts==1)return ActionResult.Failure(ActionResultCode.TargetUnavailable,"模拟目标当前不可用",command);
                world.Desktop=world.Desktop with{Revision=world.Desktop.Revision+1};enteredMotion.TrySetResult(true);
                return new(ActionResultCode.Started,command.Action,command.Value,"模拟动作开始",TimeSpan.Zero);
            };
            c.CommandWaiter=(_,token)=>scenario is "cancel" or "revoke" or "delete-session"?WaitForCancel(token):Task.FromResult(true);
            if(scenario=="invalid-late")transport.Reply=Envelope(Plan.Replace("\"mode\":\"walk\"","\"mode\":\"bad\""));
            if(scenario=="mixed-markers")transport.Reply+=CompanionCommands.Marker+"\n[]\n"+new string((char)96,3);
            if(scenario=="same-view")transport.Reply=Envelope("[{\"type\":\"Stroll\",\"mode\":\"walk\",\"view\":\"desktop\"},{\"intent\":\"CreateNote\",\"title\":\"会议\",\"content\":\"资料\"},{\"type\":\"Stroll\",\"mode\":\"walk\",\"view\":\"desktop\"}]");
            if(scenario=="replan")transport.Reply=Envelope("[{\"type\":\"Stroll\",\"mode\":\"walk\",\"view\":\"desktop\"},{\"intent\":\"CreateTask\",\"title\":\"确认会议\"}]");
            if(scenario is "target-unavailable" or "preflight-target-unavailable")transport.Reply=Envelope("[{\"type\":\"Stroll\",\"mode\":\"walk\",\"view\":\"desktop\"},{\"intent\":\"CreateTask\",\"title\":\"确认会议\"}]");
            if(scenario=="query")transport.Reply=Envelope("[{\"intent\":\"CreateNote\",\"title\":\"会议准备\",\"content\":\"private-goal-body\"},{\"intent\":\"SearchNote\",\"query\":\"会议准备\"},{\"type\":\"Stroll\",\"mode\":\"walk\",\"view\":\"desktop\"}]");
            if(scenario=="attachments")c.AddAttachments([AiAttachment.FromBytes("资料.txt",Encoding.UTF8.GetBytes("附件资料"))]);
            bool changed=false;c.Changed+=()=>
            {
                if(scenario=="between-steps"&&!changed&&c.Store.GoalRuns().FirstOrDefault()?.Steps[0].State==GoalStepState.Completed)
                {changed=true;world.Desktop=world.Desktop with{Revision=world.Desktop.Revision+1};}
            };
            c.Draft="记下准备开会，在桌面走几步，再创建确认会议待办";var pending=c.Send();
            await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if(scenario=="stale")world.Desktop=world.Desktop with{Revision=2};
            transport.Release.TrySetResult(true);
            if(scenario is "cancel" or "revoke" or "delete-session")
            {
                await enteredMotion.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if(scenario=="cancel")c.StopReply();else if(scenario=="revoke")c.SavePreferences(c.Preferences with{AllowGoalPlans=false});else c.DeleteSession();
            }
            await pending;
            var run=c.Store.GoalRuns().SingleOrDefault();int notes=c.Store.UserNotes().Count,tasks=c.Store.UserTasks().Count;
            if(scenario is "off" or "invalid-late" or "truncated" or "mixed-markers" or "attachments")
            {if(run is not null||notes!=0||tasks!=0||starts!=0)throw new Exception("Invalid plan had effects: "+scenario);}
            else if(scenario=="delete-session")
            {if(run is not null||notes!=1||tasks!=0)throw new Exception("Deleted session replayed or lost artifact");}
            else if(scenario is "denied" or "stale")
            {if(run?.State!=GoalRunState.Failed||notes!=0||tasks!=0||starts!=0)throw new Exception("Whole-plan preflight wrote data: "+scenario+" "+c.ChatStatus);}
            else if(scenario is "failed-motion" or "between-steps")
            {
                if(run?.State!=GoalRunState.Failed||notes!=1||tasks!=0||run.Steps[2].State!=GoalStepState.Skipped)throw new Exception("Partial failure not preserved: "+scenario+" "+c.ChatStatus);
                if(scenario=="failed-motion"&&c.TaskCard()?.Steps.Any(x=>x.Contains("路径被阻挡"))!=true)throw new Exception("Task card hid failure reason");
            }
            else if(scenario=="replan")
            {
                if(run?.State!=GoalRunState.Failed||run.Steps[0].Capability!="Stroll"||c.Store.UserTasks().Count!=0||!c.CanPrepareGoalReplan(run))throw new Exception("Recoverable first move was not eligible for manual replanning");
                int calls=transport.Calls;c.PrepareGoalReplan(run.Id);
                if(transport.Calls!=calls||c.Store.Goal(run.Id)!.Replans!=1||!c.Draft.Contains("路径被阻挡")||!c.Draft.Contains("桌面走几步"))throw new Exception("Replan draft did not preserve consent, quota or failure context");
                try{c.PrepareGoalReplan(run.Id);throw new Exception("Replan draft quota was reused");}catch(OperationFailureException){}
                transport.Reply=Envelope("[{\"intent\":\"CreateTask\",\"title\":\"确认会议\"}]");await c.Send();
                var continuation=c.Store.GoalRuns().Single(x=>x.Id!=run.Id);
                if(transport.Calls!=calls+1||continuation.State!=GoalRunState.Completed||c.Store.UserTasks().Count!=1||starts!=1)throw new Exception("User-sent replan did not complete only the unexecuted work");
            }
            else if(scenario is "target-unavailable" or "preflight-target-unavailable")
            {
                if(run?.State!=GoalRunState.Failed||run.Steps[0].Capability!="Stroll"||run.Steps[0].State!=GoalStepState.Failed||run.Steps[1].State!=GoalStepState.Skipped||notes!=0||tasks!=0||starts!=(scenario=="target-unavailable"?1:0)||!c.CanPrepareGoalReplan(run))
                    throw new Exception("No-effect unavailable target was not eligible for manual replanning: "+scenario);
                int calls=transport.Calls;c.PrepareGoalReplan(run.Id);
                if(transport.Calls!=calls||c.Store.Goal(run.Id)!.Replans!=1||!c.Draft.Contains("目标当前不可用")||!c.Draft.Contains("其余步骤均未执行"))
                    throw new Exception("Unavailable-target replan draft spent a request or lost the failure context");
            }
            else if(scenario is "cancel" or "revoke")
            {if(run?.State!=GoalRunState.Cancelled||notes!=1||tasks!=0||run.Steps[2].State!=GoalStepState.Skipped)throw new Exception("Cancellation ran remaining steps: "+scenario);}
            else if(scenario=="same-view")
            {if(run?.State!=GoalRunState.Completed||notes!=1||starts!=2)throw new Exception("Own earlier action invalidated later action: "+c.ChatStatus);}
            else if(scenario=="query")
            {if(run?.State!=GoalRunState.Completed||notes!=1||tasks!=0||!c.ChatStatus.Contains("找到 1 条"))throw new Exception("Query receipt not shown");}
            else
            {
                if(run?.State!=GoalRunState.Completed||notes!=1||tasks!=1||starts!=1)throw new Exception("Mixed plan did not finish: "+c.ChatStatus);
                if(!c.ChatStatus.Contains("走动（桌面）"))throw new Exception("Goal receipt hid actor view");
                int calls=transport.Calls;try{await c.Send(run.Id);throw new Exception("Replayed original goal");}catch(OperationFailureException){}
                if(transport.Calls!=calls)throw new Exception("Replay spent another model request");
                transport.Reply="你好";c.Draft="你好";await c.Send();
                if(transport.LastRequest.Contains("private-goal-body"))throw new Exception("Goal work data leaked to later context");
            }
            if(c.Session is {} session&&c.Store.Messages(session.Id).Any(x=>x.Role=="assistant"&&(x.Content.Contains(GoalProtocol.Marker)||x.Content.Contains("private-goal-body"))))
                throw new Exception("Protocol or work body appeared in assistant transcript: "+scenario);
        }
        await Native();
        File.WriteAllText("artifacts/goal-smoke-result.txt","PASS: 18 isolated fake-service scenarios; mixed note/motion/task, whole-plan rejection, partial failure/cancel/revoke, user-triggered first-move replan for blocked paths and unavailable targets including whole-plan preflight failure, stale and between-step actor changes, deleted session, truncation, mixed protocols, attachments, own-view continuity, local query receipts, no replay request or later work-data leakage. Native HWND movement plus real note/task persistence; aggregate WPF goal history present and opt-in defaults off. No external AI requests.\n");
    }
    private static async Task<bool> WaitForCancel(CancellationToken token){await Task.Delay(Timeout.Infinite,token);return false;}
    private static async Task Native()
    {
        var transport=new Handler{Reply=Envelope(Plan)};transport.Release.SetResult(true);
        using var c=new Controller(Path.GetFullPath(Path.Combine("artifacts","goal-native",Guid.NewGuid().ToString("N"))),new AiClient(transport));
        c.SavePreferences(new Preferences{Onboarded=true,Endpoint="https://goals.invalid/v1",Model="fake-goals",ContextBudget=8192,AllowPetControl=true});
        c.Secrets.Save("isolated-test-key");using var host=new DesktopHost(Application.Current,c,"goal-native-"+Guid.NewGuid().ToString("N"),true);
        host.ShowPet();host.ShowHouse("settings");host.House!.UpdateLayout();
        var check=Descendants(host.House).OfType<CheckBox>().Single(x=>x.Content is TextBlock text&&text.Text.StartsWith("允许AI按明确目标串联"));
        if(check.IsChecked==true)throw new Exception("Goal plan opt-in defaulted on");
        c.SavePreferences(c.Preferences with{AllowGoalPlans=true});host.ShowHouse("chat");await Task.Delay(150);
        var pet=host.Pet!;var before=Position(pet);c.Draft="记下准备开会，在桌面走几步，再创建确认会议待办";await c.Send();
        if(c.Store.GoalRuns().Single().State!=GoalRunState.Completed||c.Store.UserNotes().Count!=1||c.Store.UserTasks().Count!=1||Position(pet)==before)
            throw new Exception("Real motion and productivity did not complete: "+c.ChatStatus);
        var session=c.Session??throw new Exception("Chat session missing");var now=DateTimeOffset.Now;string recoveryId=Guid.NewGuid().ToString("N");
        c.Store.SaveMessage(new(recoveryId,session.Id,"user","创建恢复测试记事","complete",now));
        var recoveryPlan=GoalProtocol.Parse("[{\"intent\":\"CreateNote\",\"title\":\"恢复核对\",\"content\":\"saved before crash\"}]","创建恢复测试记事",SceneKind.Indoor);
        c.Store.BeginGoal(recoveryId,session.Id,recoveryPlan,now);c.Store.StartGoalStep(recoveryId,0,now);c.Store.ApplyGoalWork(recoveryId,0,recoveryPlan.Steps[0].Work!,now);c.Store.RecoverGoals();c.Refresh();
        host.House!.UpdateLayout();
        var management=Descendants(host.House).OfType<Expander>().Single(x=>Equals(x.Header,"聊天记录管理 · 会话、记忆与搜索"));
        management.IsExpanded=true;host.House.UpdateLayout();
        if(!Descendants(host.House).OfType<Expander>().Any(x=>Equals(x.Header,"目标执行记录 · 本机")))throw new Exception("Goal history not available");
        var history=Descendants(host.House).OfType<Expander>().Single(x=>Equals(x.Header,"目标执行记录 · 本机"));history.IsExpanded=true;
        host.House.UpdateLayout();var reconcile=Descendants(host.House).OfType<Button>().SingleOrDefault(x=>Equals(x.Content,"核对并确认此步已保存"));
        if(reconcile is null)throw new Exception("Interrupted persisted artifact had no manual reconciliation action");
        reconcile.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if(c.Store.Goal(recoveryId)?.State!=GoalRunState.Completed||c.Store.Goal(recoveryId)?.Steps[0].Evidence!="recovered-artifact"||c.Store.UserNotes().Count!=2)
            throw new Exception("Manual artifact reconciliation did not confirm the saved local result");
        host.House.Width=1200;host.House.Height=850;host.House.UpdateLayout();history.BringIntoView();await Task.Delay(100);
        var bitmap=new RenderTargetBitmap((int)host.House.ActualWidth,(int)host.House.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(host.House);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));Directory.CreateDirectory("artifacts/screenshots");
        using var screenshot=File.Create("artifacts/screenshots/goal-ledger.png");encoder.Save(screenshot);
    }
    private static System.Drawing.Point Position(PetWindow pet)
    {if(!ScreenCoordinateService.TryGetWindowRect(new WindowInteropHelper(pet).Handle,out var rect))throw new Exception("Pet HWND unavailable");return rect.Location;}
    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {yield return node;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Descendants(VisualTreeHelper.GetChild(node,i)))yield return child;}
    private sealed class Handler:HttpMessageHandler
    {
        public string Reply="",LastRequest="";public bool Truncated;public int Calls;
        public TaskCompletionSource<bool> Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Calls++;LastRequest=await request.Content!.ReadAsStringAsync(token);Entered.TrySetResult(true);await Release.Task.WaitAsync(token);
            string json=JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content=Reply},finish_reason=Truncated?"length":"stop"}}});
            return new(HttpStatusCode.OK){Content=new StringContent("data: "+json+"\n\ndata: [DONE]\n\n",Encoding.UTF8,"text/event-stream")};
        }
    }
    private sealed class World:ICompanionActivityWorld
    {
        public ActivityObservation Desktop=new ActivityObservation(CompanionView.Desktop,"fake-desktop",1,SceneKind.Desktop,"sprout","clear",true,true,"idle",[]){Visible=true};
        public ActivityObservation Observe(CompanionView view)=>Desktop with{View=view,SubjectId=view==CompanionView.Room?"fake-room":Desktop.SubjectId,Scene=view==CompanionView.Room?SceneKind.Indoor:SceneKind.Desktop};
        public ActionResult? Preflight(CompanionView view,ActionPlan plan)=>null;
        public ActionResult Start(CompanionView view,CompanionCommand command)=>throw new NotSupportedException();
        public Task<bool> Wait(CompanionView view,CompanionCommand command,CancellationToken cancellation)=>Task.FromResult(true);
        public void Stop(CompanionView view){}
    }
}
#endif
