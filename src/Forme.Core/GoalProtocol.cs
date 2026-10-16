using System.Globalization;
using System.Text.Json;

namespace Forme.Core;

public sealed record GoalStepSpec(string Capability,CompanionCommand? Command=null,ProductivityIntent? Work=null);
public sealed record GoalPlan(IReadOnlyList<GoalStepSpec> Steps);
public enum GoalRunState { Ready, Running, Completed, Failed, Cancelled, Interrupted, WaitingForPlay, WaitingForUser }
public enum GoalStepState { Pending, Running, Completed, Failed, Cancelled, Skipped, Uncertain, WaitingForPlay, WaitingForUser }
public enum GoalOrigin { Chat, Cooperative }
public sealed record GoalStepRecord(int Index,string Capability,CompanionView? View,GoalStepState State,string? ArtifactId=null,string? Evidence=null);
public sealed record GoalRun(string Id,string SessionId,GoalRunState State,IReadOnlyList<GoalStepRecord> Steps,DateTimeOffset Created,DateTimeOffset Updated,string? Endpoint=null,string? Model=null)
{
    public GoalOrigin Origin {get;init;}
    public int Requests {get;init;}
    public int Replans {get;init;}
}

public static class GoalProtocol
{
    public const string Marker="```forme-plan";
    private static readonly HashSet<string> FiniteActions=["go","stroll","dance","pat","rub","recall","scene","interact","stop"];
    public static bool CanReplan(GoalRun run)=>run.Origin==GoalOrigin.Chat&&run.State==GoalRunState.Failed&&run.Replans==0&&
        run.Steps.Count>0&&run.Steps[0].State==GoalStepState.Failed&&run.Steps[0].Capability is "GoTo" or "Stroll"&&
        run.Steps[0].Evidence==ActionResultCode.PathBlocked.ToString()&&run.Steps.Skip(1).All(x=>x.State==GoalStepState.Skipped);
    public static bool TryExtract(string response,out string json)
    {
        json="";int start=response.IndexOf(Marker,StringComparison.Ordinal);if(start<0)return false;
        int end=response.IndexOf("```",start+Marker.Length,StringComparison.Ordinal);
        if(end<0||response.IndexOf(Marker,start+Marker.Length,StringComparison.Ordinal)>=0||
            !string.IsNullOrWhiteSpace(response[..start])||!string.IsNullOrWhiteSpace(response[(end+3)..]))
            throw new InvalidDataException("目标计划格式不完整，没有执行。");
        json=response[(start+Marker.Length)..end].Trim();return true;
    }
    public static GoalPlan Parse(string json,string input,SceneKind scene)
    {
        try
        {
            if(json.Length>12000)throw new JsonException();
            using var document=JsonDocument.Parse(json,new(){MaxDepth=5});var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Array||root.GetArrayLength() is <1 or >3)throw new JsonException();
            var steps=new List<GoalStepSpec>();
            foreach(var item in root.EnumerateArray())
            {
                if(item.ValueKind!=JsonValueKind.Object||item.TryGetProperty("type",out _)==item.TryGetProperty("intent",out _))throw new JsonException();
                string one="["+item.GetRawText()+"]";
                if(item.TryGetProperty("type",out _))
                {
                    var intent=AiIntentProtocol.Parse(one,input).Single();
                    foreach(var command in ActionPlanner.Build([intent],scene).Steps)
                    {
                        if(!FiniteActions.Contains(command.Action)&&!GoalPlay.Supported(command))throw new JsonException();
                        steps.Add(new(command.Action=="scene"?"ChangeScene":intent.Type.ToString(),command));
                        if(command.Action=="scene")scene=command.Value=="outdoor"?SceneKind.Outdoor:SceneKind.Indoor;
                    }
                }
                else
                {
                    var work=ProductivityIntentProtocol.Parse(one,input).Single();ValidateWork(work,DateTimeOffset.Now);
                    steps.Add(new(work.Type.ToString(),Work:work));
                }
                if(steps.Count>3)throw new JsonException();
            }
            if(steps.Take(steps.Count-1).Any(x=>x.Command is {} c&&(c.Action=="stop"||GoalPlay.Kind(c) is not null)))throw new JsonException();
            return new(Array.AsReadOnly(steps.ToArray()));
        }
        catch(Exception ex) when(ex is JsonException or InvalidOperationException or ActionFailureException or InvalidDataException or FormatException)
        {throw new InvalidDataException("没有获得完整合法的三步目标计划，整组未执行。",ex);}
    }
    public static ActionResult? Preflight(GoalPlan plan,Preferences preferences,Func<ActionPlan,ActionResult?>? availability)
    {
        var commands=new ActionPlan(plan.Steps.Where(x=>x.Command is not null).Select(x=>x.Command!).ToArray());
        if(commands.Steps.Count==0)return null;
        if(!commands.Steps.Any(x=>GoalPlay.Kind(x) is not null))return ActionPlanner.Preflight(commands,preferences,availability);
        if(plan.Steps.Last().Command is not {} game||!GoalPlay.Supported(game)||commands.Steps.Count(x=>GoalPlay.Kind(x) is not null)!=1)
            return ActionResult.Failure(ActionResultCode.NotRecognized,"联合目标只允许最后一步进入一项支持的玩法。");
        var prefix=new ActionPlan(commands.Steps.Take(commands.Steps.Count-1).ToArray());
        var denied=prefix.Steps.Count>0?ActionPlanner.Preflight(prefix,preferences):null;
        return denied??ActionPlanner.Preflight(new([game]),preferences)??availability?.Invoke(commands);
    }
    public static string ArtifactId(string id,int index)=>"goal-"+id+"-"+index;
    private static DateTimeOffset? Time(string? value)=>value is null?null:DateTimeOffset.Parse(value,CultureInfo.InvariantCulture);
    public static UserNote Note(ProductivityIntent w,string id,DateTimeOffset now)=>new(id,w.Title!,w.Content!,w.Tags??[],false,false,"ai",now,now);
    public static UserTask Task(ProductivityIntent w,string id,DateTimeOffset now)=>new(id,w.Title!,w.Description??"","todo",w.Priority??"normal",Time(w.DueAt),Time(w.RemindAt),now,now,null,"ai","",[],0);
    public static UserReminder Reminder(ProductivityIntent w,string id,DateTimeOffset now)=>new(id,w.Title!,Time(w.DueAt)??throw new InvalidDataException("提醒缺少时间。"),null,"pending","",now,now,null);
    public static void ValidateWork(ProductivityIntent w,DateTimeOffset now)
    {
        switch(w.Type)
        {
            case ProductivityIntentType.CreateNote:Store.ValidateUserNote(Note(w,"preview",now));break;
            case ProductivityIntentType.CreateTask:Store.ValidateUserTask(Task(w,"preview",now));break;
            case ProductivityIntentType.CreateReminder:Store.ValidateUserReminder(Reminder(w,"preview",now));break;
            case ProductivityIntentType.SearchNote:case ProductivityIntentType.ListTodayTasks:break;
            default:throw new InvalidDataException("不支持这项工作能力。");
        }
    }
    public static string Label(string capability)=>capability switch{
        "CreateNote"=>"保存记事","CreateTask"=>"创建待办","CreateReminder"=>"设置提醒","SearchNote"=>"查询记事","ListTodayTasks"=>"查询今日待办",
        "GoTo"=>"到目标旁","Stroll"=>"走动","Dance"=>"舞步","Touch"=>"互动","Recall"=>"叫回","ChangeScene"=>"切换场景","Interact"=>"物件互动","Stop"=>"停止","StartPlay"=>"进入玩法",
        "PlanHide"=>"规划藏物搜索","SearchHide"=>"实际检查藏物","ProposeBoat"=>"比较纸船方案",_=>"操作"};
}
