using System.Text.RegularExpressions;

namespace Forme.Core;

public static class ContentCatalogValidator
{
    public static void Validate(IReadOnlyList<GameItem> items,IReadOnlyList<GameTaskDefinition> tasks,IReadOnlyList<GameAchievementDefinition> achievements,IReadOnlyList<LifeEvent> life)
    {
        void unique(IEnumerable<string> values,string label)
        {
            var ids=values.ToArray();
            if(ids.Any(x=>string.IsNullOrWhiteSpace(x)||!Regex.IsMatch(x,"^[a-z0-9-]{1,60}$",RegexOptions.CultureInvariant))||ids.Distinct(StringComparer.Ordinal).Count()!=ids.Length)throw new InvalidDataException(label+" ID为空、重复或无效。");
        }
        unique(items.Select(x=>x.Id),"物品");unique(tasks.Select(x=>x.Id),"任务");unique(achievements.Select(x=>x.Id),"成就");unique(life.Select(x=>x.Id),"生活事件");
        string[] visuals=["plant","lamp","rug","flower","vine","lantern","jar","wall","books","cushion","aquarium","seasonal"];
        string[] actions=["ball","discover","relax","focus","walk","furniture","water","life","boat","toy","model","garden","dance","outdoor"];
        foreach(var item in items)
            if(string.IsNullOrWhiteSpace(item.Name)||item.Kind is not ("toy" or "decor" or "rug" or "discovery")||item.Price<0||item.MaxOwned is <1 or >100||item.MinLevel is <1 or >20||!Regex.IsMatch(item.Color,"^#[0-9A-Fa-f]{6}$",RegexOptions.CultureInvariant)||item.Rarity is not ("common" or "uncommon" or "rare")||item.Condition is not ("any" or "clear" or "rain" or "snow" or "day" or "night" or "spring" or "summer" or "autumn" or "winter")||item.Kind is "decor" or "rug"&&!visuals.Contains(item.Visual)||item.Kind=="toy"&&item.Action is not ("ball" or "feather" or "yarn" or "frisbee" or "plane" or "bell"))throw new InvalidDataException("物品内容无效："+item.Id);
        foreach(var task in tasks)
            if(!actions.Contains(task.Action)||task.RewardXp is <0 or >20||task.RewardStars is <0 or >10||!achievements.Any(x=>x.Id=="task-"+task.Id))throw new InvalidDataException("任务动作、奖励或成就引用无效："+task.Id);
        foreach(var entry in life)
            if(!LivingWorld.Catalog.Any(x=>x.Id==entry.Target)||!TargetCatalog.Supports(entry.Action,TargetCapabilities.Interact))throw new InvalidDataException("生活事件目标引用无效："+entry.Id);
    }
}
