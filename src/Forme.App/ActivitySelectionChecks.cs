using Forme.Core;
using System.Net;
using System.Net.Http;
using System.Text;

namespace Forme.App;

internal static class ActivitySelectionChecks
{
    public static async Task Run(DesktopHost host,Controller c)
    {
        var directory=Path.Combine(c.Store.DirectoryPath,"activity-selection");
        foreach(string mode in new[]{"complete","two-step","none","unknown","revision","cancel","revoke","length","waiting"})
        {
            var handler=new SelectionHandler(mode);using var test=new Controller(Path.Combine(directory,mode),new AiClient(handler));
            test.Secrets.Save("offline-test-key");
            test.SavePreferences(test.Preferences with{AllowPetControl=true,AllowSceneControl=true,ShareGameState=true,GameStateEndpoint=test.Preferences.Endpoint});
            var world=new SelectionWorld{TwoSteps=mode=="two-step"};test.ActivityWorld=world;
            var work=test.ChooseActivity(CompanionView.Desktop,"根据当前环境自己选一个");
            await handler.Entered.Task;
            if(mode=="revision")world.Revision++;
            if(mode=="cancel")test.NotifyActivityInteraction();
            if(mode=="revoke")test.SavePreferences(test.Preferences with{ShareGameState=false});
            handler.Release.TrySetResult(true);
            if(mode=="waiting")
            {
                for(int i=0;i<100&&test.SelectedActivityState!=CompanionActivityState.Executing;i++)await Task.Delay(10);
                if(test.SelectedActivityState!=CompanionActivityState.Executing)throw new Exception("Activity never reached execution");
                world.Waiting=true;test.NotifyActivityInteraction();
            }
            else world.Finished.TrySetResult(true);
            await work;
            if(handler.Calls!=1||test.Busy||test.LiveReply.Length!=0)throw new Exception("Selection retried or retained request state");
            if(mode=="complete"&&(test.SelectedActivityState!=CompanionActivityState.Completed||world.Starts!=1||world.LastView!=CompanionView.Desktop))throw new Exception("Valid selection did not finish on bound subject");
            if(mode=="two-step"&&(world.Starts!=2||test.SelectedActivityState!=CompanionActivityState.Completed))throw new Exception("Own movement invalidated subsequent step");
            if(mode=="none"&&(test.SelectedActivityState!=CompanionActivityState.NoAction||world.Starts!=0))throw new Exception("No-action choice executed");
            if(mode is "unknown" or "revision" or "cancel" or "revoke" or "length"&&world.Starts!=0)throw new Exception("Invalid/stale/cancelled selection executed");
            if(mode=="waiting"&&test.SelectedActivityState!=CompanionActivityState.Cancelled)throw new Exception("In-progress activity ignored cancellation");
            var messages=test.Store.Messages(test.Session!.Id);
            if(messages.Any(x=>x.Content.Contains("test-subject")||x.Content.Contains("\"id\"")))throw new Exception("Game snapshot or raw protocol persisted");
            bool trimmed=false;var ordinary=AiClient.BuildContext(test.Preferences,messages,"你好",out trimmed);
            if(ordinary.Any(x=>x.Content.Contains("根据当前环境自己选一个")))throw new Exception("Selection observations leaked into ordinary conversation history");
        }
        var original=c.Preferences;
        try
        {
            c.SavePreferences(original with{AllowPetControl=true,AllowSceneControl=true,ShareGameState=true,GameStateEndpoint=original.Endpoint,ReducedMotion=false,Quiet=false,ShowHome3D=true,PetIdleMode="idle"});
            host.ShowHouse("chat");host.ShowPet();await Task.Delay(100);
            var world=(ICompanionActivityWorld)host;world.Stop(CompanionView.Room);world.Stop(CompanionView.Desktop);
            var room=world.Observe(CompanionView.Room);var desktop=world.Observe(CompanionView.Desktop);
            if(!room.Available||!desktop.Available||room.SubjectId==desktop.SubjectId)throw new Exception("Both views lack independent selectable subjects");
            if(Descendants(host.House!).OfType<System.Windows.Controls.Button>().Count(x=>x.Content?.ToString()=="自主选择")!=1)throw new Exception("Room selection button missing");
            var desktopRevision=desktop.Revision;
            var option=room.Options.First(x=>x.Id=="walk");
            foreach(var step in option.Steps){var started=world.Start(CompanionView.Room,step);if(started.Code!=ActionResultCode.Started||!await world.Wait(CompanionView.Room,step,new CancellationTokenSource(TimeSpan.FromSeconds(8)).Token))throw new Exception("Actual room activity failed");}
            if(world.Observe(CompanionView.Desktop).Revision!=desktopRevision)throw new Exception("Room choice moved desktop subject");
            var roomRevision=world.Observe(CompanionView.Room).Revision;
            var move=world.Start(CompanionView.Desktop,new("stroll","walk"));
            if(move.Code!=ActionResultCode.Started||!await world.Wait(CompanionView.Desktop,new("stroll","walk"),new CancellationTokenSource(TimeSpan.FromSeconds(8)).Token))throw new Exception("Actual desktop activity failed");
            if(world.Observe(CompanionView.Room).Revision!=roomRevision)throw new Exception("Desktop choice moved room subject");
            c.SavePreferences(c.Preferences with{PetModel="space-marine"});await Task.Delay(60);
            if(world.Observe(CompanionView.Room).Options.Any(x=>x.Id.StartsWith("use-")||x.Id=="sway"))throw new Exception("Marine exposed unsupported original interactions");
            host.HideDesktopPet();
            if(world.Observe(CompanionView.Desktop).Available)throw new Exception("Hidden desktop remained selectable");
        }
        finally{c.SavePreferences(original);}
        File.WriteAllText("artifacts/activity-selection-smoke-result.txt","PASS: single offline request; no-action/unknown/truncated/stale/revoked/late-result rejection; cancellation during planning/execution; local selection history and observation privacy; both actual subjects routed independently; marine capability restrictions and hidden subject unavailable. No live AI calls.\n");
    }
    private static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject node)
    {
        yield return node;
        for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);i++)
            foreach(var child in Descendants(System.Windows.Media.VisualTreeHelper.GetChild(node,i)))yield return child;
    }
    private sealed class SelectionHandler(string mode):HttpMessageHandler
    {
        public TaskCompletionSource<bool> Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {
            Calls++;Entered.TrySetResult(true);await Release.Task; // Intentionally allow a late network response after cancellation.
            string body=mode=="none"?"{\"id\":null,\"reason\":\"先休息一下\"}":mode=="unknown"?"{\"id\":\"delete-files\",\"reason\":\"试试\"}":"{\"id\":\"walk\",\"reason\":\"先走几步\"}";
            string json=System.Text.Json.JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content=body},finish_reason=mode=="length"?"length":"stop"}}});
            return new(HttpStatusCode.OK){Content=new StringContent("data: "+json+"\n\ndata: [DONE]\n\n",Encoding.UTF8,"text/event-stream")};
        }
    }
    private sealed class SelectionWorld:ICompanionActivityWorld
    {
        public int Revision=1,Starts;public bool Waiting,TwoSteps;public CompanionView LastView;
        public TaskCompletionSource<bool> Finished {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ActivityObservation Observe(CompanionView view)=>new(view,"test-subject",Revision,SceneKind.Desktop,"sprout","clear",true,true,"idle",[new("walk","走一小段","走一小段",TwoSteps?[new("stroll","walk"),new("dance","sway")]:[new("stroll","walk")])]);
        public ActionResult? Preflight(CompanionView view,ActionPlan plan)=>null;
        public ActionResult Start(CompanionView view,CompanionCommand command){Starts++;Revision++;LastView=view;return new(ActionResultCode.Started,command.Action,command.Value,"正在走",TimeSpan.Zero);}
        public async Task<bool> Wait(CompanionView view,CompanionCommand command,CancellationToken token){await Finished.Task.WaitAsync(token);return true;}
        public void Stop(CompanionView view){}
    }
}
