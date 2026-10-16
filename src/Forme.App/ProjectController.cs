using Forme.Core;
using System.Windows.Threading;

namespace Forme.App;

internal sealed partial class Controller
{
    private string? _projectId;
    private IReadOnlyList<ProjectSource> _projectSources=[];
    private CancellationTokenSource? _projectRequest;
    private DispatcherTimer? _projectExpiry;
    private string? _projectExpiryId;
    private EventHandler? _projectExpiryHandler;
    private string? _projectGrantEndpoint,_projectGrantModel;
    internal event Action? ProjectChanged;
    internal string ProjectGoalDraft {get;set;}="整理一页资料摘要，重点列出风险和待确认信息。";
    internal int ProjectTaskCount {get;set;}=3;
    internal string ProjectStatus {get;private set;}="选择本项目资料后开始。";
    internal ProjectRun? CurrentProject=>_projectId is null?null:Store.Project(_projectId);
    internal IReadOnlyList<ProjectSource> ProjectSources=>_projectSources;
    internal bool ProjectOperationBusy=>_projectRequest is not null;
    internal bool ProjectSharingAllowed=>_projectGrantEndpoint==Preferences.Endpoint&&_projectGrantModel==Preferences.Model;
    private void ProjectRefresh(){ProjectChanged?.Invoke();Changed?.Invoke();}
    internal void AllowProjectSharing(bool allowed)
    {
        if(!allowed){EndProject();return;}
        if(CurrentProject is null||_projectSources.Count==0)throw new InvalidDataException("请先选择本项目的资料。");
        _projectGrantEndpoint=Preferences.Endpoint;_projectGrantModel=Preferences.Model;ProjectRefresh();
    }
    internal async Task LoadProjectSources(IReadOnlyList<string> paths)
    {
        await TakeForegroundAsync();if(Busy)throw new OperationFailureException("请先停止当前操作。");
        NotifyActivityInteraction();
        if(paths.Count is <1 or >4)throw new InvalidDataException("每次选择1至4份文本或DOCX资料。");
        using var request=new CancellationTokenSource();_projectRequest=request;ProjectStatus="正在本地读取；尚未上传。";ProjectRefresh();
        try
        {
            var sources=await Task.Run(async()=>
            {
                var attachments=new List<AiAttachment>();
                foreach(var path in paths){var item=await AiAttachment.Read(path,request.Token);attachments.Add(item);AiAttachment.Validate(attachments);}
                return attachments.Select(ProjectSource.FromAttachment).ToArray();
            },request.Token);
            request.Token.ThrowIfCancellationRequested();
            if(sources.Select(x=>x.Reference.Id).Distinct().Count()!=sources.Length)throw new InvalidDataException("所选资料的正文有重复，请选择内容不同的资料。");
            var previous=CurrentProject;var now=DateTimeOffset.Now;
            var run=previous is {State:ProjectRunState.WaitingForSources,Requests:<2}&&previous.Sources.SequenceEqual(sources.Select(x=>x.Reference))&&(previous.ExpiresAt is null||previous.ExpiresAt>now)
                ?previous with{State=ProjectRunState.Ready,Updated=now,Notice=null}
                :new ProjectRun(Guid.NewGuid().ToString("N"),ProjectGoalDraft.Trim(),1,ProjectTaskCount,ProjectRunState.Ready,sources.Select(x=>x.Reference).ToArray(),null,0,null,now,now);
            Store.SaveProject(run);
            if(previous is not null&&previous.Id!=run.Id)Store.SaveProject(previous with{State=ProjectRunState.Closed,Updated=now});
            _projectId=run.Id;_projectSources=sources;_projectGrantEndpoint=_projectGrantModel=null;
            FocusTaskCard("project");
            ScheduleProjectExpiry(run);
            ProjectStatus="资料已在本机读取；允许本项目发送并点击生成后才会联网。";
        }
        catch(OperationCanceledException){ProjectStatus="本地读取已停止，未上传。";}
        catch(Exception ex) when(OperationErrors.Expected(ex)){ProjectStatus="资料未读取："+OperationErrors.Message(ex);throw;}
        finally{_projectRequest=null;ProjectRefresh();}
    }
    internal async Task LoadProjectWebSource(string address)
    {
        await TakeForegroundAsync();if(Busy)throw new OperationFailureException("请先停止当前操作。");
        NotifyActivityInteraction();
        var previous=CurrentProject;
        if(previous is {State:ProjectRunState.Closed or ProjectRunState.Cancelled})previous=null;
        if(_projectSources.Count>=4)throw new InvalidDataException("每个项目最多选择4份资料；请先结束当前项目。");
        using var request=new CancellationTokenSource();_projectRequest=request;ProjectStatus="正在启动本机只读网页工具；正文尚未发送给AI，可结束并停止。";ProjectRefresh();
        try
        {
            var source=await McpWebSource.ReadAsync(address,request.Token);
            request.Token.ThrowIfCancellationRequested();
            var sources=_projectSources.Append(source).ToArray();
            if(sources.Select(x=>x.Reference.Id).Distinct().Count()!=sources.Length)throw new InvalidDataException("该网页正文与当前项目资料重复。");
            var now=DateTimeOffset.Now;
            var run=new ProjectRun(Guid.NewGuid().ToString("N"),ProjectGoalDraft.Trim(),1,ProjectTaskCount,ProjectRunState.Ready,sources.Select(x=>x.Reference).ToArray(),null,0,null,now,now);
            Store.SaveProject(run);
            if(previous is not null&&previous.Id!=run.Id)Store.SaveProject(previous with{State=ProjectRunState.Closed,Updated=now});
            _projectId=run.Id;_projectSources=sources;_projectGrantEndpoint=_projectGrantModel=null;
            FocusTaskCard("project");ScheduleProjectExpiry(run);
            ProjectStatus="网页已在本机提取；授权当前AI并点击生成后才会发送来源正文。原资料仍按本项目期限保留。";
        }
        catch(OperationCanceledException) when(request.IsCancellationRequested){ProjectStatus="网页读取已停止；尚未发送给AI。";}
        catch(OperationCanceledException){ProjectStatus="网页读取超过20秒，尚未发送给AI。";throw new TimeoutException(ProjectStatus);}
        catch(Exception ex) when(OperationErrors.Expected(ex)){ProjectStatus="网页未读取："+OperationErrors.Message(ex);throw;}
        finally{_projectRequest=null;ProjectRefresh();}
    }
    internal void ReviseProjectGoal()
    {
        NotifyActivityInteraction();
        var run=CurrentProject??throw new InvalidDataException("请先选择项目资料。");
        if(run.State is ProjectRunState.Closed or ProjectRunState.Cancelled)throw new InvalidDataException("项目已结束，请重新选择资料建立新目标。");
        var next=run with{Goal=ProjectGoalDraft.Trim(),TaskCount=ProjectTaskCount,Revision=run.Revision+1,State=ProjectRunState.Ready,TaskSuggestions=[],PromotedTaskIndexes=[],Updated=DateTimeOffset.Now,Notice=null};
        Store.SaveProject(next);_projectRequest?.Cancel();ProjectStatus="新目标已生效；旧计划不会覆盖草稿。";FocusTaskCard("project");ProjectRefresh();
    }
    private List<AiTurn> ProjectTurns(ProjectRun run)=>ProjectWork.BuildTurns(Preferences,run,_projectSources,Store.ProjectArtifact(run)?.Content);
    internal string ProjectPreview()
    {
        var run=CurrentProject??throw new InvalidDataException("请先选择资料。");
        return string.Join("\n\n",ProjectTurns(run).Select(x=>x.Role+"\n"+x.Content));
    }
    internal async Task GenerateProjectDraft()
    {
        await TakeForegroundAsync();if(Busy)throw new OperationFailureException("请等待当前操作停止。");
        NotifyActivityInteraction();
        if(!ProjectSharingAllowed)throw new InvalidDataException("请先允许本项目向当前服务发送所选资料与草稿。");
        var run=CurrentProject??throw new InvalidDataException("请先选择资料。");
        if(run.Goal!=ProjectGoalDraft.Trim()||run.TaskCount!=ProjectTaskCount){ReviseProjectGoal();run=CurrentProject!;}
        var turns=ProjectTurns(run);string key=Secrets.Read()??"";if(key.Length==0)throw new InvalidDataException("请先在AI设置保存连接和密钥。");
        var preferences=Preferences with{};var issued=Store.BeginProjectAttempt(run.Id,run.Revision,DateTimeOffset.Now,preferences.Endpoint,preferences.Model);
        FocusTaskCard("project");
        var sources=_projectSources;using var request=new CancellationTokenSource();var remaining=issued.ExpiresAt!.Value-DateTimeOffset.Now;request.CancelAfter(remaining>TimeSpan.Zero?TimeSpan.FromSeconds(Math.Min(120,remaining.TotalSeconds)):TimeSpan.FromMilliseconds(1));_projectRequest=request;
        ScheduleProjectExpiry(issued);
        ProjectStatus="正在按当前目标生成草稿，可停止或应用新目标。";ProjectRefresh();
        try
        {
            if(DateTimeOffset.Now>=issued.ExpiresAt)throw new OperationCanceledException(request.Token);
            var reply=await Ai.SendAsync(preferences,key,turns,_=>{},request.Token);
            request.Token.ThrowIfCancellationRequested();
            if(!ProjectSharingAllowed||preferences.Endpoint!=Preferences.Endpoint||preferences.Model!=Preferences.Model)throw new OperationCanceledException(request.Token);
            if(reply.LengthLimited)throw new InvalidDataException("模型输出被截断，未保存草稿，也未自动重试。");
            var decision=ProjectWork.Parse(reply.Content,sources,issued.TaskCount);
            bool saved=Store.FinishProjectAttempt(issued,decision,ProjectRunState.Failed,DateTimeOffset.Now);
            ProjectStatus=!saved?"旧目标结果已作废，未覆盖草稿。":decision.Draft is null?"需要你补充："+string.Join("；",decision.Questions):"草稿已保存到项目和记事；引用已核对，内容请审阅。待办建议需逐条确认后才会加入待办。";
            if(CurrentProject?.ExpiresAt<=DateTimeOffset.Now){EndProject();ProjectStatus="项目期限已结束，未接收迟到结果；资料已清除。";}
        }
        catch(OperationCanceledException)
        {
            Store.FinishProjectAttempt(issued,null,ProjectRunState.Cancelled,DateTimeOffset.Now);
            if(DateTimeOffset.Now>=issued.ExpiresAt){EndProject();ProjectStatus="项目达到120秒期限，资料已清除；旧计划不会写入草稿。";}
            else ProjectStatus="请求已停止；已发生的服务用量可能仍计费。旧计划不会写入草稿。";
        }
        catch(Exception ex) when(OperationErrors.Expected(ex))
        {Store.FinishProjectAttempt(issued,null,ProjectRunState.Failed,DateTimeOffset.Now);ProjectStatus=OperationErrors.Message(ex);}
        finally{_projectRequest=null;ProjectRefresh();}
    }
    private void ScheduleProjectExpiry(ProjectRun run)
    {
        if(run.ExpiresAt is null){_projectExpiry?.Stop();_projectExpiryId=null;return;}
        if(_projectExpiryId==run.Id&&_projectExpiry?.IsEnabled==true)return;
        _projectExpiry??=new DispatcherTimer(DispatcherPriority.Background);_projectExpiry.Stop();
        if(_projectExpiryHandler is not null)_projectExpiry.Tick-=_projectExpiryHandler;
        _projectExpiryId=run.Id;_projectExpiryHandler=(_,_)=>ExpireProject(run.Id);_projectExpiry.Tick+=_projectExpiryHandler;
        var remaining=run.ExpiresAt.Value-DateTimeOffset.Now;
        _projectExpiry.Interval=remaining>TimeSpan.Zero?TimeSpan.FromSeconds(Math.Min(120,remaining.TotalSeconds)):TimeSpan.FromMilliseconds(1);_projectExpiry.Start();
    }
    private void ExpireProject(string id)
    {
        if(CurrentProject?.Id!=id)return;
        EndProject();ProjectStatus="本次120秒期限已结束，资料已清除；草稿保留在记事。";ProjectRefresh();
    }
    internal void EndProject()
    {
        _projectExpiry?.Stop();_projectExpiryId=null;_projectRequest?.Cancel();var run=CurrentProject;
        if(run is not null&&run.State!=ProjectRunState.Closed)Store.SaveProject(run with{State=ProjectRunState.Closed,Updated=DateTimeOffset.Now});
        _projectSources=[];_projectGrantEndpoint=_projectGrantModel=null;ProjectStatus="项目已结束，内存资料已清除；草稿仍在记事。";ProjectRefresh();
    }
    internal void SelectProject(string id)
    {
        if(Busy)throw new InvalidDataException("请先停止当前请求。");
        if(CurrentProject?.Id==id)return;
        EndProject();_projectId=id;var run=CurrentProject??throw new InvalidDataException("项目不存在。");FocusTaskCard("project");ProjectGoalDraft=run.Goal;ProjectTaskCount=run.TaskCount;ProjectStatus=run.Notice??"历史草稿可在下方查看；未恢复资料或联网许可。";ProjectRefresh();
    }
    private void DisposeProject(){_projectExpiry?.Stop();_projectRequest?.Cancel();_projectSources=[];_projectGrantEndpoint=_projectGrantModel=null;}
}
