#if FORME_DEVELOPMENT_TOOLS
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forme.Core;

namespace Forme.App;

internal static class ProjectChecks
{
    public static async Task Run()
    {
        string root=Path.GetFullPath(Path.Combine("artifacts","project-checks",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
        string file=Path.Combine(root,"资料.txt");File.WriteAllText(file,"交期尚未确认。\n需要负责人核对风险。",new UTF8Encoding(false));
        var handler=new ProjectHandler();using var controller=new Controller(Path.Combine(root,"data"),new AiClient(handler));
        controller.SavePreferences(new Preferences{Onboarded=true,Endpoint="https://project.invalid/v1",Model="test-project",ContextBudget=8192,MaxReplyTokens=512});controller.Secrets.Save("isolated-test-key");
        using var host=new DesktopHost(Application.Current,controller,"project-checks-"+Guid.NewGuid().ToString("N"),true);
        host.ShowHouse("project");await Task.Delay(150);
        controller.Store.SaveUserNote(new("private-note","未选择的记事","unshared-private-sentinel",[],false,false,"manual",DateTimeOffset.Now,DateTimeOffset.Now));
        await controller.LoadProjectSources([file]);
        try{await controller.GenerateProjectDraft();throw new Exception("Project request ran without sharing permission");}catch(InvalidDataException){}
        if(handler.Calls!=0)throw new Exception("No-consent request was sent");
        controller.AllowProjectSharing(true);handler.Block();var first=controller.GenerateProjectDraft();await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        controller.ProjectGoalDraft="只整理风险，保留待确认问题。";controller.ReviseProjectGoal();handler.Release.TrySetResult(true);await first;
        if(controller.Store.UserNotes().Count!=1||controller.CurrentProject?.Revision!=2)throw new Exception("Late result overwrote changed goal");
        await controller.GenerateProjectDraft();var saved=controller.CurrentProject!;
        if(saved.State!=ProjectRunState.DraftReady||saved.Requests!=2||controller.Store.ProjectArtifact(saved) is null||controller.Store.UserTasks().Count!=0||controller.Store.DueReminders(DateTimeOffset.MaxValue).Count!=0)
            throw new Exception("Project draft was not atomically saved, or formal tasks were created");
        if(handler.Bodies.Any(x=>x.Contains("unshared-private-sentinel",StringComparison.Ordinal)))throw new Exception("Unselected local note was uploaded");
        try{await controller.GenerateProjectDraft();throw new Exception("Third request bypassed quota");}catch(InvalidDataException){}
        if(handler.Calls!=2)throw new Exception("Quota check sent an extra request");
        host.House!.Navigate("project");host.House.UpdateLayout();
        if(Descendants(host.House).OfType<Button>().Single(x=>Equals(x.Content,"生成或修改草稿")).IsEnabled)throw new Exception("Quota-exhausted button stayed enabled");
        if(!Descendants(host.House).OfType<TextBox>().Any(x=>x.Text.Contains("未创建正式待办或提醒",StringComparison.Ordinal)))throw new Exception("Project page did not display draft status");
        host.House.Width=1200;host.House.Height=850;host.House.UpdateLayout();await Task.Delay(100);Capture(host.House,"project-workspace.png");
        host.House.Width=760;host.House.UpdateLayout();await Task.Delay(100);Capture(host.House,"project-workspace-compact.png");
        controller.EndProject();if(controller.ProjectSources.Count!=0||controller.ProjectSharingAllowed)throw new Exception("End did not clear project sources and grant");
        await controller.LoadProjectSources([file]);controller.AllowProjectSharing(true);handler.Block();var revoked=controller.GenerateProjectDraft();await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        controller.AllowProjectSharing(false);handler.Release.TrySetResult(true);await revoked;
        if(controller.Store.UserNotes().Count!=2||controller.ProjectSources.Count!=0)throw new Exception("Revoked project wrote an artifact or kept sources");
        await controller.LoadProjectSources([file]);controller.AllowProjectSharing(true);handler.Truncate=true;await controller.GenerateProjectDraft();handler.Truncate=false;
        if(controller.CurrentProject?.State!=ProjectRunState.Failed||controller.Store.UserNotes().Count!=2)throw new Exception("Truncated reply produced a draft");
        await controller.LoadProjectSources([file]);controller.AllowProjectSharing(true);handler.Block();var expired=controller.GenerateProjectDraft();await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        controller.Store.SaveProject(controller.CurrentProject! with{ExpiresAt=DateTimeOffset.Now.AddSeconds(-1)});handler.Release.TrySetResult(true);await expired;
        if(controller.ProjectSources.Count!=0||controller.ProjectSharingAllowed||controller.Store.UserNotes().Count!=2)throw new Exception("Expired result kept sources or wrote a draft");
        host.House.Navigate("hide");if(host.House.CurrentPage!="hide")throw new Exception("Project-to-play navigation failed");
        controller.EndProject();
        File.WriteAllText("artifacts/project-smoke-result.txt","PASS: real WPF project page; local text snapshots; explicit sharing required; late old-goal reply rejected; revised draft saved atomically; no formal tasks/reminders; only selected data transmitted to in-process fake transport; two-call quota; revocation clears sources; truncated reply rejected; existing game navigation. No external requests.\n");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {yield return node;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Descendants(VisualTreeHelper.GetChild(node,i)))yield return child;}
    private static void Capture(Window window,string name)
    {
        var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(window);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));Directory.CreateDirectory("artifacts/screenshots");using var file=File.Create(Path.Combine("artifacts/screenshots",name));encoder.Save(file);
    }
    private sealed class ProjectHandler:HttpMessageHandler
    {
        public int Calls;public bool Truncate;
        public List<string> Bodies {get;}=[];
        public TaskCompletionSource<bool> Entered {get;private set;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release {get;private set;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _blocked;
        public void Block(){_blocked=true;Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);Release=new(TaskCreationOptions.RunContinuationsAsynchronously);}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Calls++;string body=await request.Content!.ReadAsStringAsync(CancellationToken.None);Bodies.Add(body);
            using var wire=JsonDocument.Parse(body);string content=wire.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            using var data=JsonDocument.Parse(content);var source=data.RootElement.GetProperty("sources")[0];string id=source.GetProperty("id").GetString()!;
            var tasks=Enumerable.Range(0,data.RootElement.GetProperty("expectedTasks").GetInt32()).Select(i=>new{title="确认风险"+(i+1),description="请负责人核对"}).ToArray();
            if(_blocked){_blocked=false;Entered.TrySetResult(true);await Release.Task;}
            string answer=JsonSerializer.Serialize(new{capability="notes.draft",arguments=new{title="项目测试草稿",summary="交期与风险需要确认。",citations=new[]{new{sourceId=id,paragraph=1,quote="交期尚未确认"}},tasks,questions=Array.Empty<string>()}});
            string json=JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content=answer},finish_reason=Truncate?"length":"stop"}}});
            return new(HttpStatusCode.OK){Content=new StringContent("data: "+json+"\n\ndata: [DONE]\n\n",Encoding.UTF8,"text/event-stream")};
        }
    }
}
#endif
