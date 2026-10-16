namespace Forme.Core;

public enum CapabilityDomain { Companion, Productivity, Project, Goal }
public enum CapabilityInvoker { Model, User }
public sealed record CapabilityDefinition(string Id,CapabilityDomain Domain,CapabilityInvoker Invoker,string Input,string Description);

public static class CapabilityCatalog
{
    public static IReadOnlyList<CapabilityDefinition> All {get;}=Array.AsReadOnly(
        Enum.GetValues<CompanionIntentType>().Where(x=>x!=CompanionIntentType.Shortcut)
            .Select(x=>new CapabilityDefinition(x.ToString(),CapabilityDomain.Companion,CapabilityInvoker.Model,
                CompanionInput(x),"将当前用户明确要求转为动作计划；由本机权限、场景和目标预检后执行。")).Where(x=>x.Input.Length>0)
            .Concat(Enum.GetValues<ProductivityIntentType>()
                .Select(x=>new CapabilityDefinition(x.ToString(),CapabilityDomain.Productivity,CapabilityInvoker.Model,
                    ProductivityInput(x),"只根据本轮用户明确输入；读取或写入本地记事、待办和提醒，不把既有记录加入模型请求。")).Where(x=>x.Input.Length>0))
            .Concat([
                new("plan.execute",CapabilityDomain.Goal,CapabilityInvoker.Model,"最多三步的type/intent混合数组","按本轮明确目标串联已授权的有限动作与工作操作；末步可进入藏物或纸船，等待本机确认本局结果；失败即停。"),
                new("play.hide.cooperate",CapabilityDomain.Goal,CapabilityInvoker.User,"候选区域、已检查项和用户线索","独立分享许可下由用户主动开始；最多两次搜索规划。"),
                new("play.boat.propose",CapabilityDomain.Goal,CapabilityInvoker.User,"目标码头与本机模拟候选","用户主动请求一次建议，确认后才放船。"),
                new("notes.draft",CapabilityDomain.Project,CapabilityInvoker.Model,"有来源的草稿与待办建议","仅使用用户选择且明确授权发送的资料；草稿在本机保存。"),
                new("user.ask",CapabilityDomain.Project,CapabilityInvoker.Model,"最多三个必要澄清问题","仅根据当前目标和已选资料提出问题，不执行写入。"),
                new("task.promote",CapabilityDomain.Project,CapabilityInvoker.User,"确认一条待办建议","需用户逐条确认；只在本机创建待办，不自动设置时间或提醒。")
            ]).ToArray());

    public static IReadOnlyList<CapabilityDefinition> ForModel(CapabilityDomain domain)=>
        Array.AsReadOnly(All.Where(x=>x.Domain==domain&&x.Invoker==CapabilityInvoker.Model).ToArray());
    public static string ModelNames(CapabilityDomain domain)=>string.Join(", ",ForModel(domain).Select(x=>x.Id));
    public static bool AllowsModel(CapabilityDomain domain,string id)=>All.Any(x=>x.Domain==domain&&x.Invoker==CapabilityInvoker.Model&&x.Id==id);
    public static bool RequiresUserConfirmation(CapabilityDomain domain,string id)=>
        All.Any(x=>x.Domain==domain&&x.Id==id&&x.Invoker==CapabilityInvoker.User);
    public static string ModelSignatures(CapabilityDomain domain,ChatCapabilitySnapshot? snapshot=null)=>
        string.Join(";",ForModel(domain).Where(x=>domain!=CapabilityDomain.Companion||snapshot is null||Granted(x.Id,snapshot)).Select(x=>x.Id+"("+x.Input+")"));
    private static bool Granted(string id,ChatCapabilitySnapshot p)=>id switch{
        "GoTo" or "Stroll" or "Dance" or "Idle" or "ChangeModel" or "Touch"=>p.PetControl,
        "Interact" or "ChangeScene" or "ChangeWeather" or "ToggleDevice"=>p.SceneControl,
        "Recall" or "Stop"=>p.PetControl||p.PlayControl,
        "StartPlay" or "UseToy" or "Life" or "BoatConfiguration" or "NameBoat"=>p.PlayControl,
        "LayoutPreview"=>p.LayoutControl,_=>false};
    private static string CompanionInput(CompanionIntentType type)=>type switch{
        CompanionIntentType.GoTo=>"target,mode=walk/run",
        CompanionIntentType.Interact=>"target",
        CompanionIntentType.Stroll=>"mode=walk/run,count=1/2",
        CompanionIntentType.Dance=>"mode=sway/hop/spin,count=1..3",
        CompanionIntentType.Idle=>"mode=idle/sleep/walk/run",
        CompanionIntentType.Recall or CompanionIntentType.Stop=>"-",
        CompanionIntentType.ChangeScene=>"target=indoor/outdoor",
        CompanionIntentType.ChangeWeather=>"mode=clear/rain/snow",
        CompanionIntentType.ChangeModel=>"target=sprout/cat/fox/penguin/space-marine",
        CompanionIntentType.ToggleDevice=>"target=light/fireplace,mode=on/off",
        CompanionIntentType.Touch=>"mode=pat/rub",
        CompanionIntentType.UseToy=>"target",
        CompanionIntentType.StartPlay=>"target=boat,mode=open/start/pause/resume/retrieve|target=hide,mode=pet-hides/pet-seeks",
        CompanionIntentType.Life=>"target",
        CompanionIntentType.LayoutPreview=>"mode=reading/undo|mode=furniture,target,x,z,rotation",
        CompanionIntentType.BoatConfiguration=>"target=shape/color/wind,mode|target=1/2/3,x,z,rotation",
        CompanionIntentType.NameBoat=>"target",_=>""};
    private static string ProductivityInput(ProductivityIntentType type)=>type switch{
        ProductivityIntentType.CreateNote=>"title,content,tags",
        ProductivityIntentType.SearchNote=>"query",
        ProductivityIntentType.CreateTask=>"title,description,dueAt,remindAt,priority",
        ProductivityIntentType.ListTodayTasks=>"-",
        ProductivityIntentType.CreateReminder=>"title,dueAt",_=>""};
}
