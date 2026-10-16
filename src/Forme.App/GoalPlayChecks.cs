#if FORME_DEVELOPMENT_TOOLS
using Forme.Core;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Forme.App;

internal static class GoalPlayChecks
{
    private static string Reply(string kind,string mode)=>GoalProtocol.Marker+"\n[{\"intent\":\"CreateNote\",\"title\":\"工作记录\",\"content\":\"private-goal-work-body\"},{\"type\":\"StartPlay\",\"target\":\""+kind+"\",\"mode\":\""+mode+"\"}]\n"+new string((char)96,3);
    public static async Task Run()
    {
        foreach(string scenario in new[]{"hide-found","hide-stop","hide-replaced","house-close","hide-minimize","boat-open","boat-start","boat-pause","boat-stop-ready","revoked","denied","stale-layout","stale-hide","missing-receipt","old-boat-result","cancel-launch","floating-hide","project-stop"})
        {
            string root=Path.GetFullPath(Path.Combine("artifacts","goal-play-checks",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
            string kind=scenario.StartsWith("boat",StringComparison.Ordinal)||scenario is "stale-layout" or "old-boat-result"?"boat":"hide";
            var handler=new Handler{Reply=Reply(kind,kind=="hide"?"pet-hides":scenario=="boat-start"?"start":"open")};
            using var c=new Controller(Path.Combine(root,"data"),new AiClient(handler));
            c.SavePreferences(new Preferences{Onboarded=true,Endpoint="https://goal-play.invalid/v1",Model="fake-play",AllowGoalPlans=true,AllowPlayControl=scenario!="denied",AllowPetControl=true,AllowSceneControl=true,ReducedMotion=scenario!="boat-pause",ContextBudget=8192});c.Secrets.Save("isolated-test-key");
            using var host=new DesktopHost(Application.Current,c,"goal-play-"+Guid.NewGuid().ToString("N"),true);host.ShowHouse("chat");await Task.Delay(80);
            var room=Descendants(host.House!).OfType<Room3DView>().Single();
            if(scenario=="hide-found")
            {
                string source=Path.Combine(root,"本目标资料.txt");File.WriteAllText(source,"private-project-source-body");
                await c.LoadProjectSources([source]);c.AllowProjectSharing(true);
            }
            if(scenario is "stale-layout" or "old-boat-result")
            {
                host.House!.Navigate("boat");
                if(scenario=="old-boat-result"){room.StartBoat();if(room.LastBoat is null)throw new Exception("Native old boat did not finish");}
            }
            handler.Block=scenario is "stale-layout" or "stale-hide";
            if(scenario=="missing-receipt")c.ActionHandler=command=>new(ActionResultCode.Completed,command.Action,command.Value,"模拟虚假启动回执",TimeSpan.Zero);
            if(scenario=="cancel-launch")c.ActionHandler=command=>{var receipt=host.ExecuteAction(command);c.StopReply();return receipt;};
            if(scenario=="floating-hide"){host.House!.Close();host.ShowFloatingChat();}
            c.Draft=kind=="hide"?"记下工作记录，然后你藏我找，玩一局藏物":"记下工作记录，然后玩一局纸船";
            var pending=c.Send();
            if(handler.Block)
            {
                await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if(scenario=="stale-layout")room.ConfigureBoat(room.BoatSettings with{Wind=1});else room.StartHide(true,null);
                handler.Release.TrySetResult(true);
            }
            await pending;var run=c.Store.GoalRuns().Single();
            if(scenario=="floating-hide")room=Descendants(host.House!).OfType<Room3DView>().Single();
            if(scenario=="cancel-launch")
            {if(run.State!=GoalRunState.Cancelled||room.HideActive||c.Store.UserNotes().Count!=1)throw new Exception($"Cancellation at launch retained an unowned game: {run.State}; active={room.HideActive}; notes={c.Store.UserNotes().Count}; {c.ChatStatus}");continue;}
            if(scenario is "denied" or "stale-layout" or "stale-hide" or "old-boat-result")
            {
                if(run.State!=GoalRunState.Failed||c.Store.UserNotes().Count!=0)throw new Exception("Whole-plan validation wrote data: "+scenario+" "+c.ChatStatus);
                if(scenario=="stale-hide"&&!room.HideActive)throw new Exception("Stale goal stopped a newer player round");
                continue;
            }
            if(scenario=="missing-receipt")
            {
                if(run.State!=GoalRunState.Failed||run.Steps[^1].State!=GoalStepState.Uncertain||room.HideActive)throw new Exception("Launch acknowledgement fabricated a game outcome");
                continue;
            }
            if(scenario=="boat-start")
            {if(run.State!=GoalRunState.Completed||room.LastBoat is null||run.Steps[^1].Evidence!="play:boat:"+room.LastBoat.Dock)throw new Exception("Immediate actual boat result was not confirmed");continue;}
            if(run.State!=GoalRunState.WaitingForPlay||run.Steps[^1].State!=GoalStepState.WaitingForPlay||c.Busy||c.TaskCard()?.CanStop!=true)
                throw new Exception("Game launch was confused with completion: "+scenario+" "+c.ChatStatus);
            var observed=((IGoalPlayWorld)host).ObserveGoalPlay(kind);
            if(handler.Bodies.Any(x=>x.Contains(observed.Round,StringComparison.Ordinal)||x.Contains(room.ActivitySubject,StringComparison.Ordinal)||x.Contains("private-project-source-body",StringComparison.Ordinal)))throw new Exception("Local game identity or project source leaked");
            if(scenario=="hide-found")
            {
                if(c.ProjectSources.Count!=0||c.ProjectSharingAllowed)throw new Exception("Goal game inherited project data");
                host.House!.UpdateLayout();var details=Descendants(host.House).OfType<Border>().Where(x=>AutomationProperties.GetName(x)=="当前任务卡").SelectMany(Descendants).OfType<Expander>().Single();details.IsExpanded=true;
                host.House.Width=1200;host.House.Height=850;host.House.UpdateLayout();Capture(host.House,"goal-play-waiting.png");
                var selection=Descendants(host.House).OfType<ComboBox>().Single(x=>AutomationProperties.GetName(x)=="藏物家具");
                for(int i=0;i<selection.Items.Count&&room.HideActive;i++){selection.SelectedIndex=i;Descendants(host.House).OfType<Button>().Single(x=>Equals(x.Content,"检查选中家具")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
                if(c.Store.Goal(run.Id)?.State!=GoalRunState.Completed||c.Store.Goal(run.Id)?.Steps[^1].Evidence!="play:hide:found"||c.TaskCard()?.CanStop==true)throw new Exception("Actual found result was not recorded");
                handler.Reply="你好";c.Draft="你好";await c.Send();if(handler.Bodies[^1].Contains("private-goal-work-body",StringComparison.Ordinal)||handler.Bodies[^1].Contains("private-project-source-body",StringComparison.Ordinal))throw new Exception("Work data leaked into later chat");
            }
            else if(scenario=="hide-replaced")
            {
                room.StartHide(true,null);if(c.Store.Goal(run.Id)?.State!=GoalRunState.Cancelled||!room.HideActive)throw new Exception("Replaced round completed older goal");
                try{c.StopTaskCard("goal:"+run.Id);throw new Exception("Old card stopped replacement game");}catch(OperationFailureException){}
                if(!room.HideActive)throw new Exception("Replacement game was stopped");
            }
            else if(scenario=="house-close")
            {host.House!.Close();if(c.Store.Goal(run.Id)?.State is not (GoalRunState.Interrupted or GoalRunState.Cancelled))throw new Exception("Closing scene falsely completed goal");}
            else if(scenario=="hide-minimize")
            {host.House!.WindowState=WindowState.Minimized;await Task.Delay(60);if(room.HideActive||c.Store.Goal(run.Id)?.State!=GoalRunState.Cancelled)throw new Exception("Minimized hide left an unresolved goal");}
            else if(scenario=="boat-open")
            {
                room.ConfigureBoat(room.BoatSettings with{Wind=1});if(c.Store.Goal(run.Id)?.State!=GoalRunState.WaitingForPlay)throw new Exception("Preparation edits ended the same game");
                room.StartBoat();if(room.LastBoat is null||c.Store.Goal(run.Id)?.State!=GoalRunState.Completed||c.Store.Goal(run.Id)?.Steps[^1].Evidence!="play:boat:"+room.LastBoat.Dock)throw new Exception("Native boat docking was not confirmed");
            }
            else if(scenario=="boat-pause")
            {
                room.StartBoat();room.PauseBoat();if(c.Store.Goal(run.Id)?.State!=GoalRunState.WaitingForPlay||c.TaskCard()?.Phase!="玩法已暂停，等待继续")throw new Exception("Pause was reported as completion");
                c.StopTaskCard("goal:"+run.Id);if(room.BoatRunning||c.Store.Goal(run.Id)?.State!=GoalRunState.Cancelled)throw new Exception("Paused boat did not stop");
            }
            else if(scenario=="boat-stop-ready")
            {c.StopCommands?.Invoke();if(c.Store.Goal(run.Id)?.State!=GoalRunState.Cancelled)throw new Exception("Stop left a prepared boat goal pending");}
            else if(scenario=="revoked")
            {c.SavePreferences(c.Preferences with{AllowPlayControl=false});if(room.HideActive||c.Store.Goal(run.Id)?.State!=GoalRunState.Cancelled)throw new Exception("Revoked play grant did not stop owned round");}
            else if(scenario=="project-stop")
            {
                string source=Path.Combine(root,"后续项目.txt");File.WriteAllText(source,"独立项目资料");await c.LoadProjectSources([source]);c.AllowProjectSharing(true);handler.Block=true;
                var project=c.GenerateProjectDraft();await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                c.StopTaskCard(c.TaskCard()!.Key);await project;
                if(!room.HideActive||c.Store.Goal(run.Id)?.State!=GoalRunState.WaitingForPlay||c.ProjectSources.Count!=0)throw new Exception("Stopping separate project stopped pending game");
                c.StopTaskCard("goal:"+run.Id);
            }
            else
            {c.StopTaskCard("goal:"+run.Id);if(room.HideActive||c.Store.Goal(run.Id)?.State!=GoalRunState.Cancelled)throw new Exception("Pending hide did not stop");}
            if(c.Store.UserNotes().Count!=1)throw new Exception("Earlier work artifact lost");
            if(handler.Calls!=(scenario is "hide-found" or "project-stop"?2:1))throw new Exception("Game reconciliation sent another AI request");
        }
        File.WriteAllText("artifacts/goal-play-smoke-result.txt","PASS: 18 isolated native WPF scenarios; mixed work-to-hide/boat waits for real round result; actual player guesses and boat simulator docking resolve ledger; immediate reduced-motion docking; stop, pause, revoke, replacement, close, minimize, missing launch receipt and cancellation during launch; floating-chat goal opens actual house game; preflight rejects denied/stale layout/stale round/previous docking without prior work writes; owned stop does not affect newer game; separate project stop preserves pending game; project data and grant cleared; private round IDs and source/work bodies excluded from requests; no reconciliation model calls. Only in-process mock HTTP; no external AI requests.\n");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {yield return node;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var item in Descendants(VisualTreeHelper.GetChild(node,i)))yield return item;}
    private static void Capture(Window window,string name)
    {
        var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine("artifacts/screenshots",name));png.Save(file);
    }
    private sealed class Handler:HttpMessageHandler
    {
        public string Reply="";public bool Block;public int Calls;public List<string> Bodies {get;}=[];
        public TaskCompletionSource<bool> Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Calls++;Bodies.Add(await request.Content!.ReadAsStringAsync(token));if(Block){Entered.TrySetResult(true);await Release.Task.WaitAsync(token);}
            string json=JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content=Reply},finish_reason="stop"}}});
            return new(HttpStatusCode.OK){Content=new StringContent("data: "+json+"\n\ndata: [DONE]\n\n",Encoding.UTF8,"text/event-stream")};
        }
    }
}
#endif
