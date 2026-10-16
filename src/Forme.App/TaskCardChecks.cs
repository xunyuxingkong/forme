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

internal static class TaskCardChecks
{
    public static async Task Run()
    {
        string root=Path.GetFullPath(Path.Combine("artifacts","task-card-checks",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
        var handler=new Handler();using var c=new Controller(Path.Combine(root,"data"),new AiClient(handler));
        c.SavePreferences(new Preferences{Onboarded=true,Endpoint="https://cards.invalid/v1",Model="fake-cards",AllowPetControl=true,AllowGoalPlans=true,ContextBudget=8192});c.Secrets.Save("isolated-test-key");
        using var host=new DesktopHost(Application.Current,c,"task-cards-"+Guid.NewGuid().ToString("N"),true);
        host.ShowPet();host.ShowFloatingChat();
        if(c.TaskCard() is not null||Cards(host.FloatingChat!).Any())throw new Exception("Empty startup created a task card");
        var entered=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        c.ActionHandler=command=>{entered.TrySetResult(true);return new(ActionResultCode.Started,command.Action,command.Value,"模拟走动",TimeSpan.Zero);};
        c.CommandWaiter=async(_,token)=>{await Task.Delay(Timeout.Infinite,token);return false;};
        c.Draft="记下准备开会，在桌面走几步，再创建确认会议待办";var goal=c.Send();await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var running=c.TaskCard()??throw new Exception("Running goal missing");
        if(!running.CanStop||running.CanPlay||!running.Steps[0].Contains("已完成")||!running.Steps[1].Contains("执行中")||!running.Current.Contains("桌面"))throw new Exception("Goal card claimed wrong state");
        host.FloatingChat!.UpdateLayout();if(Cards(host.FloatingChat).Count()!=1)throw new Exception("Floating task card missing");
        if(Descendants(host.FloatingChat).OfType<ScrollViewer>().Single(x=>x.Name=="聊天记录滚动区").ActualHeight<80)throw new Exception("Task card crowded out floating transcript");
        Capture(host.FloatingChat,"task-card-floating.png");
        host.FloatingChat.Width=300;host.FloatingChat.UpdateLayout();
        if(Descendants(host.FloatingChat).OfType<ScrollViewer>().Single(x=>x.Name=="聊天记录滚动区").ActualHeight<80)throw new Exception("Narrow task card crowded out floating transcript");
        Capture(host.FloatingChat,"task-card-floating-compact.png");
        var floatingDetails=Descendants(Cards(host.FloatingChat).Single()).OfType<Expander>().Single();floatingDetails.IsExpanded=true;host.FloatingChat.UpdateLayout();
        if(Descendants(host.FloatingChat).OfType<ScrollViewer>().Single(x=>x.Name=="聊天记录滚动区").ActualHeight<80)throw new Exception("Expanded task card crowded out floating transcript");
        Capture(host.FloatingChat,"task-card-floating-expanded.png");
        host.ShowHouse("notes");host.House!.UpdateLayout();
        var card=Cards(host.House).Single();
        Descendants(card).OfType<Button>().Single(x=>Equals(x.Content,"停止当前任务")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await goal;
        var stopped=c.TaskCard()??throw new Exception("Stopped goal missing");
        if(stopped.CanStop||stopped.Phase!="已停止"||!stopped.Artifacts[0].Exists||stopped.Artifacts[1].Exists||c.Store.UserTasks().Count!=0)throw new Exception("Task card stop ran later write or lost saved note");
        entered=new(TaskCreationOptions.RunContinuationsAsynchronously);c.Draft="记下新的会议，在桌面走几步，再创建确认会议待办";var newer=c.Send();await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try{c.StopTaskCard(stopped.Key);throw new Exception("Old card stopped a new task");}catch(OperationFailureException){}
        c.StopReply();await newer;var latest=c.TaskCard()!;c.DismissTaskCard(latest.Key);
        if(c.TaskCard() is not null||!c.Store.GoalRuns().Any())throw new Exception("Dismiss deleted ledger or left card visible");

        string file=Path.Combine(root,"项目资料.txt");File.WriteAllText(file,"交期尚未确认。\nsource-private-sentinel",new UTF8Encoding(false));
        c.ProjectTaskCount=0;host.ShowHouse("project");await c.LoadProjectSources([file]);
        int before=handler.Calls;if(c.TaskCard()?.Sources.Count!=1||handler.Calls!=before)throw new Exception("Local task card sent a request or hid sources");
        c.AllowProjectSharing(true);await c.GenerateProjectDraft();var saved=c.CurrentProject!;var ready=c.TaskCard()!;
        if(ready.Phase!="草稿已保存，待审阅"||!ready.Artifacts.Single().Exists||!ready.CanPlay)throw new Exception("Project card did not verify draft");
        c.ProjectGoalDraft="只整理风险，保留待确认问题。";c.ReviseProjectGoal();
        var revised=c.TaskCard()!;if(revised.Phase!="等待生成"||revised.Artifacts.Single().Label!="先前版本草稿")throw new Exception("Old draft reported as current revision");
        try{c.PrepareTaskPlay(ready.Key,"hide");throw new Exception("Old revision card entered game");}catch(OperationFailureException){}
        await c.GenerateProjectDraft();host.House!.Navigate("home");host.House.UpdateLayout();
        var expander=Descendants(Cards(host.House).Single()).OfType<Expander>().Single();expander.IsExpanded=true;
        host.House.Width=1200;host.House.Height=850;host.House.UpdateLayout();await Task.Delay(100);Capture(host.House,"task-card-project.png");
        host.House.Width=760;host.House.UpdateLayout();await Task.Delay(100);Capture(host.House,"task-card-project-compact.png");
        if(Descendants(Cards(host.House).Single()).OfType<TextBlock>().Any(x=>x.Text.Contains("source-private-sentinel")))throw new Exception("Source body entered task card");
        int calls=handler.Calls;
        Descendants(Cards(host.House).Single()).OfType<Button>().Single(x=>Equals(x.Content,"结束工作，玩藏物")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(host.House.CurrentPage!="hide"||c.ProjectSources.Count!=0||c.ProjectSharingAllowed||handler.Calls!=calls||c.Store.ProjectArtifact(c.CurrentProject!) is null||c.TaskCard() is not null)
            throw new Exception("Work-to-play handoff kept sources/grant, lost artifact, or sent AI request");
        handler.Chat=true;c.Draft="你好";await c.Send();
        if(handler.LastBody.Contains("source-private-sentinel"))throw new Exception("Project data leaked into later ordinary chat");
        handler.Chat=false;await c.LoadProjectSources([file]);c.AllowProjectSharing(true);await c.GenerateProjectDraft();
        calls=handler.Calls;var boatCard=c.TaskCard()!;host.House.Navigate(c.PrepareTaskPlay(boatCard.Key,"boat"));
        if(host.House.CurrentPage!="boat"||c.ProjectSources.Count!=0||c.ProjectSharingAllowed||handler.Calls!=calls||c.Store.ProjectArtifact(c.CurrentProject!) is null)throw new Exception("Work-to-boat handoff retained sources or sent a request");
        await c.LoadProjectSources([file]);c.AllowProjectSharing(true);await c.GenerateProjectDraft();
        var missing=c.CurrentProject!;c.Store.DeleteUserNote(missing.ArtifactId!);c.Refresh();
        if(c.TaskCard()!.Artifacts.Single().Exists||c.TaskCard()!.CanPlay)throw new Exception("Deleted draft still shown as an available result");
        c.EndProject();await c.LoadProjectSources([file]);c.AllowProjectSharing(true);handler.Block=true;
        var pending=c.GenerateProjectDraft();await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        host.House.Navigate("notes");host.House.UpdateLayout();
        Descendants(Cards(host.House).Single()).OfType<Button>().Single(x=>Equals(x.Content,"停止当前任务")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await pending;
        if(c.ProjectSources.Count!=0||c.ProjectSharingAllowed||c.CurrentProject!.ArtifactId is not null||c.Busy)throw new Exception("Project task-card stop did not revoke request and data");
        c.EndProject();host.House.Navigate("home");host.House.UpdateLayout();
        File.WriteAllText("artifacts/task-card-smoke-result.txt","PASS: native WPF cards on house pages and floating chat; live mixed-goal progress and stop preserved note and skipped task; stale card cannot stop a newer goal; dismiss retains ledger; source metadata without bodies; revised target labels earlier draft; stale revision cannot enter game; explicit work-to-hide/boat navigation clears project sources/grant, retains draft, and adds no AI request; later chat excludes project data; deleted artifact invalidates result; task-card project cancellation clears data. Wide/narrow/floating expanded screenshots captured; 380/300-width floating transcript remains visible. Only in-process mock transport; no external AI requests.\n");
    }
    private static IEnumerable<DependencyObject> Cards(DependencyObject window)=>Descendants(window).Where(x=>AutomationProperties.GetName(x)=="当前任务卡");
    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {yield return node;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Descendants(VisualTreeHelper.GetChild(node,i)))yield return child;}
    private static void Capture(Window window,string name)
    {
        var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));Directory.CreateDirectory("artifacts/screenshots");using var file=File.Create(Path.Combine("artifacts/screenshots",name));encoder.Save(file);
    }
    private sealed class Handler:HttpMessageHandler
    {
        public int Calls;public string LastBody="";public bool Chat,Block;
        public TaskCompletionSource<bool> Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Calls++;LastBody=await request.Content!.ReadAsStringAsync(token);
            if(Block){Entered.TrySetResult(true);await Task.Delay(Timeout.Infinite,token);}
            string answer;
            using var doc=JsonDocument.Parse(LastBody);string system=doc.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
            if(system.StartsWith("你是Forme项目伙伴",StringComparison.Ordinal))
            {
                using var data=JsonDocument.Parse(doc.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
                string id=data.RootElement.GetProperty("sources")[0].GetProperty("id").GetString()!;
                answer=JsonSerializer.Serialize(new{capability="notes.draft",arguments=new{title="风险草稿",summary="交期需要确认。",citations=new[]{new{sourceId=id,paragraph=1,quote="交期尚未确认"}},tasks=Array.Empty<object>(),questions=Array.Empty<string>()}});
            }
            else answer=Chat?"你好":GoalProtocol.Marker+"\n[{\"intent\":\"CreateNote\",\"title\":\"会议\",\"content\":\"meeting-private-body\"},{\"type\":\"Stroll\",\"mode\":\"walk\",\"view\":\"desktop\"},{\"intent\":\"CreateTask\",\"title\":\"确认会议\"}]\n"+new string((char)96,3);
            string json=JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content=answer},finish_reason="stop"}}});
            return new(HttpStatusCode.OK){Content=new StringContent("data: "+json+"\n\ndata: [DONE]\n\n",Encoding.UTF8,"text/event-stream")};
        }
    }
}
#endif
