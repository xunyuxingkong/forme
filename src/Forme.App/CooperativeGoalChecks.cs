#if FORME_DEVELOPMENT_TOOLS
using Forme.Core;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Forme.App;

internal static class CooperativeGoalChecks
{
    public static async Task Run()
    {
        string[] cases=["hide-feedback","hide-first","hide-decline","hide-exhausted","hide-malformed","hide-truncated","hide-network","hide-hint","hide-cancel","hide-revoke","hide-replace","denied",
            "boat-confirm","boat-feedback","boat-pause","boat-stop","boat-layout","boat-model","boat-service","boat-config","boat-close","boat-manual","boat-replace","boat-decline","boat-network","boat-cancel-launch"];
        foreach(string scenario in cases)
        {
            File.AppendAllText("artifacts/cooperative-goals-progress.txt",scenario+Environment.NewLine);
            string root=Path.GetFullPath(Path.Combine("artifacts","cooperative-goal-checks",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
            bool boat=scenario.StartsWith("boat",StringComparison.Ordinal);var handler=new Handler{Scenario=scenario};
            using var c=new Controller(Path.Combine(root,"data"),new AiClient(handler));
            c.SavePreferences(new Preferences{Onboarded=true,Endpoint="https://cooperative.invalid/v1",Model="fake-cooperative",AllowPlayControl=true,
                SharePlayState=scenario!="denied",ShareGameState=true,GameStateEndpoint="https://cooperative.invalid/v1",AllowGoalPlans=false,
                ReducedMotion=scenario is not ("boat-pause" or "boat-stop" or "boat-cancel-launch"),ContextBudget=8192});c.Secrets.Save("isolated-test-key");
            using var host=new DesktopHost(Application.Current,c,"cooperative-"+Guid.NewGuid().ToString("N"),true);host.ShowHouse(boat?"boat":"hide");
            var room=Descendants(host.House!).OfType<Room3DView>().Single();
            host.House!.UpdateLayout();
            for(int attempt=0;attempt<100&&!room.ActivityVisible;attempt++)await Task.Delay(40);
            if(!room.ActivityVisible)throw new Exception("Native scenario did not display its scene: "+scenario+"; page="+host.House.CurrentPage+"; phase="+((IGoalPlayWorld)host).ObserveGoalPlay(boat?"boat":"hide").Phase);
            if(scenario=="denied")
            {
                try{await c.RunAiHide("lamp","private-local-hint");throw new Exception("Unshared cooperative request was allowed");}catch(OperationFailureException){}
                if(handler.Calls!=0||c.Store.GoalRuns().Count!=0)throw new Exception("Denied cooperation wrote a run or called model");continue;
            }
            handler.Block=scenario is "hide-hint" or "hide-cancel" or "hide-revoke" or "hide-replace";
            var pending=boat?c.ProposeBoat(scenario=="boat-feedback"?"willow":"bridge"):c.RunAiHide(scenario=="hide-first"?"book":"lamp","private-local-hint");
            if(handler.Block)
            {
                await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(6));
                if(c.TaskCard()?.CanStop!=true)throw new Exception("Planning card cannot stop");
                if(scenario=="hide-hint")c.GiveHideHint("revised-public-hint");
                if(scenario=="hide-cancel")c.StopTaskCard(c.TaskCard()!.Key);
                if(scenario=="hide-revoke")c.SavePreferences(c.Preferences with{SharePlayState=false});
                if(scenario=="hide-replace")room.StartHide(true,null);
                handler.Release.TrySetResult(true);
            }
            await pending.WaitAsync(TimeSpan.FromSeconds(35));var run=c.Store.GoalRuns().Single();
            if(run.Origin!=GoalOrigin.Cooperative||run.SessionId!=""||c.Store.Sessions().Count!=0||run.Requests!=handler.Calls)throw new Exception("Request ledger or source provenance is wrong: "+scenario);
            if(!boat)
            {
                bool found=scenario is "hide-first" or "hide-feedback" or "hide-hint";
                if(found&&(run.State!=GoalRunState.Completed||run.Steps[1].Evidence!="play:hide:found"))throw new Exception("Actual hide result missing: "+scenario+"; "+c.CompanionLifeStatus);
                if(!found&&run.State is not (GoalRunState.Failed or GoalRunState.Cancelled))throw new Exception("Unsuccessful hide falsely completed: "+scenario);
                int calls=scenario is "hide-feedback" or "hide-hint" or "hide-exhausted"?2:1;
                if(handler.Calls!=calls||c.Busy||c.AiHideRunning)throw new Exception("Unbounded or stuck hide: "+scenario);
                if(scenario=="hide-feedback"&&!handler.Bodies[1].Contains("book",StringComparison.Ordinal))throw new Exception("Replan missed checked feedback");
                if(scenario=="hide-replace"&&!room.HideActive)throw new Exception("Cancelled older search stopped newer player game");
                if(scenario!="hide-replace"&&room.HideActive)throw new Exception("Owned search remained active");
            }
            else if(scenario is "boat-decline" or "boat-network")
            {
                if(run.State!=GoalRunState.Failed||c.CooperativeBoatProposal is not null||room.BoatRunning)throw new Exception("Failed boat suggestion launched a game");
            }
            else
            {
                if(run.State!=GoalRunState.WaitingForUser||run.Steps[0].State!=GoalStepState.Completed||run.Steps[1].State!=GoalStepState.WaitingForUser||
                    c.TaskCard()?.CanConfirm!=true||c.TaskCard()?.CanStop!=true||room.BoatRunning||room.LastBoat is not null)throw new Exception("Suggestion was treated as launch or completion: "+scenario+" "+c.CompanionLifeStatus);
                if(scenario=="boat-confirm")
                {
                    host.House!.Width=1200;host.House.Height=850;host.House.UpdateLayout();
                    var details=Descendants(host.House).OfType<Expander>().Single(x=>Equals(x.Header,"步骤、来源与产物"));details.IsExpanded=true;
                    Descendants(host.House).OfType<Expander>().Single(x=>Equals(x.Header,"合作玩法执行记录 · 本机")).IsExpanded=true;
                    host.House.UpdateLayout();Capture(host.House,"cooperative-boat-confirm.png");
                    host.House.Width=880;host.House.UpdateLayout();Capture(host.House,"cooperative-boat-confirm-narrow.png");
                    c.ConfirmBoatFromTaskCard("goal:"+run.Id);
                    if(room.LastBoat is null||c.Store.Goal(run.Id)?.State!=GoalRunState.Completed||c.SelectedActivityState!=CompanionActivityState.Completed||
                        c.Store.Goal(run.Id)?.Steps[^1].Evidence!="play:boat:"+room.LastBoat.Dock)throw new Exception("Actual boat docking not reconciled");
                }
                else if(scenario=="boat-feedback")
                {
                    if(run.Steps[0].Evidence is not { } proposal||!proposal.StartsWith("proposed:",StringComparison.Ordinal))throw new Exception("First experiment proposal missing");
                    string target=proposal[9..].Split(':',2)[0];
                    c.ConfirmBoatProposal();var first=c.Store.Goal(run.Id)!;
                    if(first.State!=GoalRunState.Completed||room.LastBoat?.Dock==target||!c.BoatExperimentAvailable)throw new Exception($"Missed voyage did not expose a bounded experiment action: state={first.State}; target={target}; actual={room.LastBoat?.Dock}; status={c.CompanionLifeStatus}; observation={((IGoalPlayWorld)host).ObserveGoalPlay("boat")}");
                    await c.ProposeBoatExperiment().WaitAsync(TimeSpan.FromSeconds(35));
                    var runs=c.Store.GoalRuns().Where(x=>x.Origin==GoalOrigin.Cooperative).OrderBy(x=>x.Created).ToArray();
                    using var requestJson=JsonDocument.Parse(handler.Bodies[1]);string feedback= requestJson.RootElement.GetProperty("messages").EnumerateArray().Last(x=>x.GetProperty("role").GetString()=="user").GetProperty("content").GetString()!;
                    using var feedbackJson=JsonDocument.Parse(feedback);var previous=feedbackJson.RootElement.GetProperty("previous");
                    if(runs.Length!=2||runs[1].Requests!=1||runs[1].State!=GoalRunState.WaitingForUser||previous.GetProperty("target").GetString()!=target||previous.GetProperty("actual").GetString()!=room.LastBoat!.Dock)throw new Exception("Second proposal did not use the actual previous voyage as bounded feedback");
                    if(c.BoatExperimentAvailable)throw new Exception("One actual voyage was reused for another experiment");
                }
                else if(scenario is "boat-pause" or "boat-stop")
                {
                    c.ConfirmBoatProposal();if(c.Store.Goal(run.Id)?.State!=GoalRunState.WaitingForPlay||!room.BoatRunning)throw new Exception("Moving boat not tracked");
                    c.SavePreferences(c.Preferences with{Quiet=true});if(c.Store.Goal(run.Id)?.State!=GoalRunState.WaitingForPlay)throw new Exception("Cooperation wrongly requires chat goal grant");
                    if(scenario=="boat-pause"){room.PauseBoat();if(c.TaskCard()?.Phase!="玩法已暂停，等待继续")throw new Exception("Paused game falsely completed");}
                    c.StopTaskCard("goal:"+run.Id);
                    if(room.BoatRunning||c.Store.Goal(run.Id)?.State!=GoalRunState.Cancelled)throw new Exception("Stopping owned boat failed");
                }
                else if(scenario=="boat-cancel-launch")
                {
                    bool stopped=false;void CancelLaunch(){if(!stopped&&room.BoatRunning){stopped=true;c.StopReply();}}
                    room.BoatChanged+=CancelLaunch;
                    try{c.ConfirmBoatProposal();}catch(OperationCanceledException){}catch(OperationFailureException){}
                    finally{room.BoatChanged-=CancelLaunch;}
                    // StartBoat's game event is synchronous; cancellation must also be checked before the watcher is installed.
                    if(!stopped||room.BoatRunning||c.Store.Goal(run.Id)?.State!=GoalRunState.Cancelled)throw new Exception("Launch cancellation left unowned boat");
                }
                else
                {
                    if(scenario=="boat-layout")room.ConfigureBoat(room.BoatSettings with{Wind=1});
                    if(scenario=="boat-model")c.SavePreferences(c.Preferences with{PetModel="cat"});
                    if(scenario=="boat-service")c.SavePreferences(c.Preferences with{Endpoint="https://other.invalid/v1"});
                    if(scenario=="boat-config")c.SaveAiConfiguration(c.Preferences with{Model="new-model"},null);
                    if(scenario=="boat-close")host.House!.Close();
                    if(scenario=="boat-manual")room.StartBoat();
                    if(scenario=="boat-replace")
                    {
                        await c.ProposeBoat("willow");
                        try{c.ConfirmBoatFromTaskCard("goal:"+run.Id);throw new Exception("Old task card launched replacement suggestion");}catch(OperationFailureException){}
                        if(c.Store.Goal(run.Id)?.State!=GoalRunState.Cancelled||c.CooperativeBoatProposal is null)throw new Exception("Replacement proposal ownership is wrong");
                        c.StopTaskCard(c.TaskCard()!.Key);
                    }
                    if(c.Store.Goal(run.Id)?.State!=GoalRunState.Cancelled||c.CooperativeBoatProposal is not null||c.TaskCard()?.CanConfirm==true)throw new Exception("Stale suggestion remained confirmable: "+scenario);
                }
                try{c.ConfirmBoatFromTaskCard("goal:"+run.Id);throw new Exception("Terminal proposal was replayed");}catch(OperationFailureException){}
            }
            if(boat&&handler.Calls!=(scenario is "boat-replace" or "boat-feedback"?2:1))throw new Exception("Boat reconciliation called model");
            string ledger=JsonSerializer.Serialize(c.Store.GoalRuns());
            if(ledger.Contains("private-local-hint",StringComparison.Ordinal)||ledger.Contains("revised-public-hint",StringComparison.Ordinal)||ledger.Contains("isolated-test-key",StringComparison.Ordinal))throw new Exception("Ledger contains transient hint or secret");
            if(handler.Bodies.Any(x=>x.Contains(room.ActivitySubject,StringComparison.Ordinal)))throw new Exception("Private scene identity shared");
        }
        File.WriteAllText("artifacts/cooperative-goals-smoke-result.txt","PASS: "+cases.Length+" isolated native WPF scenarios. Cooperative hide real search/find, feedback replan, two-call bound, hint change, decline, malformed/truncated/network failures, cancellation, sharing withdrawal and replacement ownership; boat suggestion waits explicit confirmation, actual-dock feedback enables one user-triggered replanning request, feedback reuse is rejected, real reduced-motion docking, pause/stop, default-disabled chat-goal grant independence, stale layout/model/service/house invalidation, proposal replacement, decline, failed call and launch stop; task cards/history with wide/narrow PNGs; durable request count and no fake chat source, private hints/secret excluded from ledger, local identity excluded from requests. In-process mock HTTP only; no external AI requests.\n");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {yield return node;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Descendants(VisualTreeHelper.GetChild(node,i)))yield return child;}
    private static void Capture(Window window,string name)
    {var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine("artifacts/screenshots",name));png.Save(file);}
    private sealed class Handler:HttpMessageHandler
    {
        public string Scenario="";public bool Block;public int Calls;public List<string> Bodies {get;}=[];
        public TaskCompletionSource<bool> Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Calls++;string body=await request.Content!.ReadAsStringAsync(token);Bodies.Add(body);
            if(Block&&Calls==1){Entered.TrySetResult(true);await Release.Task.WaitAsync(token);}
            if(Scenario.EndsWith("network",StringComparison.Ordinal))return new(HttpStatusCode.ServiceUnavailable);
            string reply;
            if(Scenario.StartsWith("boat",StringComparison.Ordinal))
            {
                if(Scenario=="boat-decline")reply="{\"id\":null,\"reason\":\"暂不建议\"}";
                else if(Scenario=="boat-feedback")
                {
                    using var json=JsonDocument.Parse(body);var messages=json.RootElement.GetProperty("messages").EnumerateArray().ToArray();string payload=messages.Last(x=>x.GetProperty("role").GetString()=="user").GetProperty("content").GetString()!;
                    using var state=JsonDocument.Parse(payload);var options=state.RootElement.GetProperty("options").EnumerateArray().ToArray();string goal=state.RootElement.GetProperty("goal").GetString()!;
                    if(Calls==1)reply=JsonSerializer.Serialize(new{id=options.First(x=>x.GetProperty("dock").GetString()!=goal).GetProperty("id").GetString(),reason="进行一次实际对照"});
                    else
                    {
                        if(!messages[0].GetProperty("content").GetString()!.Contains("上一轮目标",StringComparison.Ordinal)||!state.RootElement.TryGetProperty("previous",out _))throw new Exception("Second boat request omitted actual-result feedback");
                        reply=JsonSerializer.Serialize(new{id=options[0].GetProperty("id").GetString(),reason="结合实测调整"});
                    }
                }
                else reply="{\"id\":\"straight\",\"reason\":\"采用本机候选路线\"}";
            }
            else
            {
                using var json=JsonDocument.Parse(body);string state=json.RootElement.GetProperty("messages").EnumerateArray().Last(x=>x.GetProperty("role").GetString()=="user").GetProperty("content").GetString()!;
                using var publicState=JsonDocument.Parse(state);var checkedIds=publicState.RootElement.GetProperty("checkedIds").EnumerateArray().Select(x=>x.GetString()).ToArray();
                if(Calls==2&&Scenario=="hide-feedback"&&!checkedIds.Contains("book"))throw new Exception("Feedback search lacks actual checked ID");
                if(Calls==2&&Scenario=="hide-hint"&&publicState.RootElement.GetProperty("hint").GetString()!="revised-public-hint")throw new Exception("Changed hint absent from second request");
                reply=Scenario=="hide-decline"?"[]":Scenario=="hide-malformed"?"not-json":Calls==1?"[\"book\"]":Scenario=="hide-exhausted"?"[\"feed\"]":"[\"lamp\"]";
            }
            string packet=JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content=reply},finish_reason=Scenario=="hide-truncated"?"length":"stop"}}});
            return new(HttpStatusCode.OK){Content=new StringContent("data: "+packet+"\n\ndata: [DONE]\n\n",Encoding.UTF8,"text/event-stream")};
        }
    }
}
#endif
