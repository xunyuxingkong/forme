using Forme.Core;
using System.Windows;
using System.Windows.Controls;

namespace Forme.App;

internal sealed record TaskCardArtifact(string Label,string Page,bool Exists);
internal sealed record TaskCardSnapshot(string Key,string Stamp,string Title,string Phase,string Current,
    IReadOnlyList<string> Steps,IReadOnlyList<string> Sources,IReadOnlyList<TaskCardArtifact> Artifacts,
    string Page,bool CanStop,bool CanPlay,bool CanConfirm=false);

internal sealed partial class Controller
{
    private string? _taskCardPreferred,_taskCardDismissed;
    private void FocusTaskCard(string kind){_taskCardPreferred=kind;_taskCardDismissed=null;}
    internal TaskCardSnapshot? TaskCard()
    {
        var goal=_goalPlay is {} waiting?Store.Goal(waiting.Id):
            (_cooperativeRequest||_boatProposalGoalId is not null||_taskCardPreferred=="cooperative")&&_cooperativeGoalId is {} cooperative?Store.Goal(cooperative):
            Session is {} session?Store.GoalRuns(session.Id).FirstOrDefault():null;
        var project=CurrentProject;
        TaskCardSnapshot? card;
        if((_goalExecuting||_cooperativeRequest||_boatProposalGoalId is not null||_goalPlay is not null&&_projectRequest is null)&&goal is not null)card=GoalCard(goal);
        else if(project is not null&&(_projectRequest is not null||_taskCardPreferred=="project"))card=ProjectCard(project);
        else card=goal is not null?GoalCard(goal):null;
        return card is null||card.Stamp==_taskCardDismissed?null:card;
    }
    private TaskCardSnapshot GoalCard(GoalRun run)
    {
        var source=Store.GoalSource(run);
        int finished=run.Steps.Count(x=>x.State==GoalStepState.Completed);
        var current=run.Steps.FirstOrDefault(x=>x.State==GoalStepState.Running);
        string phase=run.State switch{GoalRunState.Completed=>"已完成",GoalRunState.Cancelled=>"已停止",GoalRunState.Interrupted=>"中断，需核对",GoalRunState.Failed=>"未全部完成",GoalRunState.WaitingForPlay=>_goalPlay?.Phase switch{GoalPlayPhase.Running=>"纸船航行中，等待到岸",GoalPlayPhase.Paused=>"玩法已暂停，等待继续",GoalPlayPhase.Unavailable=>"玩法结果待核对",_=>"等待你参与玩法"},_=>"执行中"};
        if(_goalExecuting&&_request?.IsCancellationRequested==true)phase="正在停止，等待结果核对";
        if(run.State==GoalRunState.WaitingForUser)phase="等待你确认纸船建议，尚未放船";
        if(run.Origin==GoalOrigin.Cooperative&&run.State==GoalRunState.Running&&run.Id==_cooperativeGoalId)phase=_cooperativeStatus;
        if(_cooperativeRequest&&run.Id==_cooperativeGoalId&&_request?.IsCancellationRequested==true)phase="正在停止，等待结果核对";
        var artifacts=run.Steps.Where(x=>x.ArtifactId is not null).Select(x=>new TaskCardArtifact(
            GoalProtocol.Label(x.Capability),x.Capability=="CreateNote"?"notes":"today",Store.GoalArtifactExists(x))).ToArray();
        string progress=$"已确认 {finished}/{run.Steps.Count} 步"+(current is null?"":" · 正在"+GoalProtocol.Label(current.Capability)+GoalView(current.View));
        if(run.Origin==GoalOrigin.Cooperative)progress+=$" · AI请求 {run.Requests}/{(run.Steps[0].Capability=="PlanHide"?2:1)}";
        return new("goal:"+run.Id,"goal:"+run.Id+":"+run.Updated.ToString("O"),source??"跨组件目标",phase,progress,
            run.Steps.Select(x=>$"{x.Index+1}. {GoalProtocol.Label(x.Capability)}{GoalView(x.View)} · {GoalStepLabel(x.State)}"+GoalEvidence(x.Evidence)+GoalObstacle(x.Evidence)).ToArray(),[],artifacts,
            run.Origin==GoalOrigin.Cooperative?(run.Steps[0].Capability=="PlanHide"?"hide":"boat"):run.State==GoalRunState.WaitingForPlay?_goalPlay?.Initial.Kind??"chat":"chat",
            _goalPlay?.Id==run.Id||(_cooperativeRequest&&_cooperativeGoalId==run.Id||_goalExecuting)&&_request?.IsCancellationRequested!=true&&(run.State is GoalRunState.Ready or GoalRunState.Running)||_boatProposalGoalId==run.Id,
            !Busy&&run.State is not (GoalRunState.Ready or GoalRunState.Running or GoalRunState.WaitingForPlay or GoalRunState.WaitingForUser)&&artifacts.Any(x=>x.Exists),
            !Busy&&run.State==GoalRunState.WaitingForUser&&_boatProposalGoalId==run.Id);
    }
    private static string GoalObstacle(string? evidence)
    {
        string? code=evidence?.StartsWith("preflight:",StringComparison.Ordinal)==true?evidence[10..]:evidence;
        return code switch{
            "Unauthorized"=>" · 对应权限未开启","TargetUnavailable"=>" · 目标不存在或不可用","SceneUnavailable"=>" · 场景隐藏、暂停或状态已变化",
            "PathBlocked"=>" · 路径被阻挡","Interrupted"=>" · 伙伴或动作已被替换","Cancelled"=>" · 已取消","Timeout"=>" · 等待动作超时",
            "NotRecognized"=>" · 参数或步骤组合不支持","Failed" or "missing-receipt"=>" · 未取得成功回执",_=>""};
    }
    private TaskCardSnapshot ProjectCard(ProjectRun run)
    {
        var artifact=Store.ProjectArtifact(run);
        bool current=artifact?.Id.StartsWith("project-"+run.Id+"-"+run.Revision+"-",StringComparison.Ordinal)==true;
        string phase=run.State switch{ProjectRunState.Ready=>"等待生成",ProjectRunState.Running=>"生成草稿中",ProjectRunState.DraftReady=>"草稿已保存，待审阅",
            ProjectRunState.WaitingUser=>"等待补充",ProjectRunState.WaitingForSources=>"需重选资料",ProjectRunState.Cancelled=>"已停止",ProjectRunState.Failed=>"生成失败",_=>"项目已结束"};
        if(_projectRequest?.IsCancellationRequested==true)phase="正在停止，等待结果核对";
        else if(run.State==ProjectRunState.DraftReady&&artifact is null)phase="草稿产物已删除，需核对";
        var artifacts=run.ArtifactId is null?Array.Empty<TaskCardArtifact>():[new TaskCardArtifact(current?"本版本草稿":"先前版本草稿","notes",artifact is not null)];
        var details=new List<string>{ProjectStatus};
        if(run.Notice is {} notice)details.Add(notice);
        if(artifact is not null&&!current)details.Add("保留的是先前版本草稿，尚未交付当前目标的新草稿。");
        if(run.State==ProjectRunState.DraftReady)details.Add($"待办建议 {run.TaskSuggestions.Count} 条，已确认加入 {run.PromotedTaskIndexes.Count} 条；其余仍为建议。");
        details.Add((artifact is not null?"来源定位已核对；草稿内容仍需审阅。":"生成草稿后核对引用定位，内容仍需审阅。")+"玩法不会继承项目资料或分享许可。");
        string key="project:"+run.Id+":"+run.Revision;
        return new(key,key+":"+run.Updated.ToString("O"),run.Goal,phase,$"目标版本 {run.Revision} · 已用 {run.Requests}/2 次请求",
            details,run.Sources.Select(x=>x.Name+" · "+x.Paragraphs+" 段").ToArray(),artifacts,"project",_projectRequest is {IsCancellationRequested:false},
            !Busy&&artifact is not null);
    }
    internal void StopTaskCard(string key)
    {
        var card=TaskCard();if(card?.Key!=key||!card.CanStop)throw new OperationFailureException("当前任务已变化，请查看最新状态。");
        if(_goalPlay is {} waiting&&key=="goal:"+waiting.Id){StopWaitingGoalPlay();return;}
        if(key=="goal:"+_cooperativeGoalId){StopCooperativeTask(key);return;}
        if(card.Page=="project"){EndProject();return;}
        StopReply();
    }
    internal void DismissTaskCard(string key)
    {
        var card=TaskCard();if(card?.Key!=key||card.CanStop||Busy)return;
        _taskCardDismissed=card.Stamp;Refresh();
    }
    internal string PrepareTaskPlay(string key,string page)
    {
        if(page is not ("hide" or "boat"))throw new InvalidDataException("不支持这个玩法入口。");
        var card=TaskCard();if(card?.Key!=key||!card.CanPlay||Busy)throw new OperationFailureException("请先等待当前任务结束，并核对已保存产物。");
        EndProject();_taskCardDismissed=TaskCard()?.Stamp;Refresh();return page;
    }
}

internal static class TaskCardUi
{
    internal static UIElement? Create(Controller c,Action<string> navigate,bool expanded,Action<bool> expansion,bool compact=false)
    {
        if(c.TaskCard() is not {} card)return null;
        string title=card.Title.Length>(compact?26:66)?card.Title[..(compact?26:66)]+"…":card.Title;
        var summary=Ui.Stack(Ui.Text("当前目标 · "+title,compact?12:14,null,true),Ui.Text(card.Phase+" · "+card.Current,11,Ui.Muted));
        var actions=Ui.Row();
        if(card.CanStop)actions.Children.Add(Ui.Button("停止当前任务",()=>c.StopTaskCard(card.Key)));
        var steps=Ui.Stack(Ui.Text(card.Title,13,null,true));
        foreach(string step in card.Steps)steps.Children.Add(Ui.Text(step,12,Ui.Muted));
        if(card.Sources.Count>0){steps.Children.Add(Ui.Text("本目标来源 · 本机展示",12,null,true));foreach(string source in card.Sources)steps.Children.Add(Ui.Text(source,11,Ui.Muted));}
        foreach(var artifact in card.Artifacts)steps.Children.Add(Ui.Text(artifact.Label+" · "+(artifact.Exists?"本机产物存在":"尚未生成或已删除"),12,Ui.Muted));
        if(card.CanConfirm)steps.Children.Add(Ui.Button("确认AI建议并放船",()=>c.ConfirmBoatFromTaskCard(card.Key),true));
        steps.Children.Add(Ui.Button(card.Page is "hide" or "boat"?"返回本局玩法":"查看任务与记录",()=>navigate(card.Page)));
        foreach(string page in card.Artifacts.Where(x=>x.Exists&&card.Page is not ("hide" or "boat")).Select(x=>x.Page).Distinct())
            steps.Children.Add(Ui.Button(page=="notes"?"查看已保存记事":"查看待办和提醒",()=>navigate(page)));
        if(card.CanPlay)steps.Children.Add(Ui.Row(
            Ui.Button("结束工作，玩藏物",()=>navigate(c.PrepareTaskPlay(card.Key,"hide"))),
            Ui.Button("结束工作，玩纸船",()=>navigate(c.PrepareTaskPlay(card.Key,"boat")))));
        if(!card.CanStop){var dismiss=Ui.Button("收起此任务卡",()=>c.DismissTaskCard(card.Key));dismiss.IsEnabled=!c.Busy;steps.Children.Add(dismiss);}
        var details=new Expander{Header=compact?Ui.Text(card.Phase+" · "+title,11,null,true):"步骤、来源与产物",IsExpanded=expanded,Content=new ScrollViewer{Content=steps,MaxHeight=compact?110:170,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}};
        if(compact)steps.Children.Insert(1,Ui.Text(card.Current,11,Ui.Muted));
        details.Expanded+=(_,_)=>expansion(true);details.Collapsed+=(_,_)=>expansion(false);
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        grid.Children.Add(compact?details:summary);Grid.SetColumn(actions,1);grid.Children.Add(actions);
        var result=Ui.Card(compact?grid:Ui.Stack(grid,details),new Thickness(compact?8:12));result.Margin=new Thickness(0,0,0,8);
        System.Windows.Automation.AutomationProperties.SetName(result,"当前任务卡");return result;
    }
}

internal sealed partial class MainWindow
{
    private readonly ContentControl _taskCard=new();
    private bool _taskCardExpanded,_taskCardCompact;
    private void UpdateTaskCard()
    {_taskCardCompact=ActualWidth>0&&ActualWidth<940;_taskCard.Content=TaskCardUi.Create(_c,Navigate,_taskCardExpanded,value=>_taskCardExpanded=value,_taskCardCompact);}
}
