#if FORME_DEVELOPMENT_TOOLS
using Forme.Core;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Forme.App;

internal static class ChatCapabilityChecks
{
    public static async Task Run(DesktopHost host,Controller native)
    {
        foreach(string scenario in new[]{"stale-room","other-view","revoke","endpoint","no-sharing","default-view","between-views"})
        {
            string root=Path.GetFullPath(Path.Combine("artifacts","chat-capability-checks",Guid.NewGuid().ToString("N")));
            var transport=new Handler();using var c=new Controller(root,new AiClient(transport));
            c.SavePreferences(new Preferences{Onboarded=true,Endpoint="https://capability.invalid/v1",Model="fake-capabilities",ContextBudget=8192,AllowPetControl=true});
            c.SavePreferences(c.Preferences with{ShareChatState=scenario!="no-sharing",ChatStateEndpoint=c.Preferences.Endpoint});c.Secrets.Save("isolated-test-key");
            var world=new World();c.ActivityWorld=world;SceneKind scene=scenario=="default-view"?SceneKind.Desktop:SceneKind.Indoor;c.CurrentScene=()=>scene;
            var firstStep=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);var finishStep=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int starts=0;CompanionView? executed=null;c.ActionHandler=command=>{starts++;executed=command.View;if(scenario=="between-views"){firstStep.TrySetResult(true);return new(ActionResultCode.Started,command.Action,command.Value,"模拟动作开始",TimeSpan.Zero);}return new(ActionResultCode.Completed,command.Action,command.Value,"模拟动作完成",TimeSpan.Zero);};
            c.CommandWaiter=(_,token)=>finishStep.Task.WaitAsync(token);
            c.ActionPreflight=_=>null;c.StopCommands=()=>{};
            string? view=scenario=="stale-room"?"room":scenario=="default-view"?null:"desktop";
            transport.Reply=CompanionCommands.Marker+"\n"+JsonSerializer.Serialize(new[]{new{type="Stroll",mode="walk",view}})+"\n"+new string((char)96,3);
            if(scenario=="between-views")transport.Reply=CompanionCommands.Marker+"\n"+JsonSerializer.Serialize(new[]{new{type="Stroll",mode="walk",view="room"},new{type="Stroll",mode="walk",view="desktop"}})+"\n"+new string((char)96,3);
            c.Draft="请走几步";var pending=c.Send();await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if(scenario is "stale-room" or "other-view")world.Room=world.Room with{Revision=2};
            if(scenario=="revoke")c.SavePreferences(c.Preferences with{ShareChatState=false});
            if(scenario=="endpoint")c.SavePreferences(c.Preferences with{Endpoint="https://changed.invalid/v1"});
            if(scenario=="default-view"){scene=SceneKind.Indoor;world.Room=world.Room with{Revision=2};}
            transport.Release.SetResult(true);
            if(scenario=="between-views"){await firstStep.Task.WaitAsync(TimeSpan.FromSeconds(5));world.Desktop=world.Desktop with{Revision=2};finishStep.SetResult(true);}
            await pending;
            bool rejects=scenario is "stale-room" or "revoke" or "endpoint";
            if(starts!=(rejects?0:1))throw new Exception("Unexpected execution: "+scenario+" "+c.ChatStatus);
            if(!rejects&&executed!=(scenario=="between-views"?CompanionView.Room:CompanionView.Desktop))throw new Exception("Default or explicit view was rebound: "+scenario);
            if(scenario=="between-views"&&c.ActionResults.Last().Code!=ActionResultCode.Interrupted)throw new Exception("Later view was not revalidated");
            if(transport.System.Contains("本轮已授权的伙伴快照")!=(scenario!="no-sharing"))throw new Exception("Sharing gate failed: "+scenario);
            if(transport.System.Contains("private-subject")||transport.System.Contains("private-experience"))throw new Exception("Local identity leaked");
            if(c.Store.Messages(c.Session!.Id).Any(x=>x.Content.Contains("本轮已授权的伙伴快照")))throw new Exception("Snapshot persisted in transcript");
        }
        native.SavePreferences(new Preferences{Onboarded=true,AllowPetControl=true,AllowSceneControl=true});
        host.ShowPet();host.ShowHouse("settings");host.House!.UpdateLayout();
        var check=Descendants(host.House).OfType<CheckBox>().Single(x=>x.Content is TextBlock text&&text.Text.StartsWith("聊天时分享伙伴状态"));
        if(check.IsChecked==true)throw new Exception("Chat sharing defaulted to on");
        native.SavePreferences(native.Preferences with{ShareChatState=true,ChatStateEndpoint=native.Preferences.Endpoint});
        var before=native.CaptureChatCapabilities();
        if(!before.Actors.Single(x=>x.View==CompanionView.Room).Visible||!before.Actors.Single(x=>x.View==CompanionView.Desktop).Visible)
            throw new Exception("Actual subjects missing from snapshot");
        host.House.WindowState=WindowState.Minimized;
        var minimized=native.CaptureChatCapabilities();
        if(minimized.Actors.Single(x=>x.View==CompanionView.Room).Visible||!minimized.Actors.Single(x=>x.View==CompanionView.Desktop).Visible)
            throw new Exception("Minimizing house changed wrong snapshot");
        host.House.WindowState=WindowState.Normal;
        File.WriteAllText("artifacts/chat-capability-smoke-result.txt","PASS: seven in-process mock chat requests; sharing gate, stale room rejection, unrelated view independence, immediate revocation cancellation, endpoint change, initial default view binding, later-view revalidation between steps; no private IDs/experiences in model context or transcript. Actual WPF settings opt-in defaults off; native room/desktop visibility and minimized house snapshot verified. No external model requests.\n");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {yield return node;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Descendants(VisualTreeHelper.GetChild(node,i)))yield return child;}
    private sealed class Handler:HttpMessageHandler
    {
        public string Reply="",System="";
        public TaskCompletionSource<bool> Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            using(var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token)))System=body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
            Entered.SetResult(true);await Release.Task.WaitAsync(token);
            string json=JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content=Reply},finish_reason="stop"}}});
            return new(HttpStatusCode.OK){Content=new StringContent("data: "+json+"\n\ndata: [DONE]\n\n",Encoding.UTF8,"text/event-stream")};
        }
    }
    private sealed class World:ICompanionActivityWorld
    {
        public ActivityObservation Room=Actor(CompanionView.Room),Desktop=Actor(CompanionView.Desktop);
        private static ActivityObservation Actor(CompanionView view)=>new ActivityObservation(view,"private-subject-"+view,1,view==CompanionView.Room?SceneKind.Indoor:SceneKind.Desktop,
            "sprout","clear",true,true,"idle",[new("walk","走几步","短活动",[new("stroll","walk")])],"private-experience"){Visible=true};
        public ActivityObservation Observe(CompanionView view)=>view==CompanionView.Room?Room:Desktop;
        public ActionResult? Preflight(CompanionView view,ActionPlan plan)=>null;
        public ActionResult Start(CompanionView view,CompanionCommand command)=>throw new NotSupportedException();
        public Task<bool> Wait(CompanionView view,CompanionCommand command,CancellationToken cancellation)=>Task.FromResult(true);
        public void Stop(CompanionView view){}
    }
}
#endif
