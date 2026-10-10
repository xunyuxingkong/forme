using System.Text.Json;

namespace Forme.Core;

public static class GameProgression
{
    public static int LevelFor(int experience)=>Math.Clamp(experience/100+1,1,20);
    public static int ExperienceToNext(int experience)=>LevelFor(experience)>=20?0:100-experience%100;
    public static IReadOnlyList<GameTaskDefinition> TaskCatalog {get;}=Load<GameTaskDefinition[]>("game-tasks.json");
    public static IReadOnlyList<GameAchievementDefinition> Achievements {get;}=Load<GameAchievementDefinition[]>("game-achievements.json");
    public static IReadOnlyList<GameTask> DailyTasks(DateOnly day,IReadOnlySet<string> completed)
    {
        int rotation=day.DayNumber%TaskCatalog.Count;
        return Enumerable.Range(0,Math.Min(3,TaskCatalog.Count)).Select(i=>TaskCatalog[(rotation+i)%TaskCatalog.Count]).Select(x=>new GameTask(x.Id,x.Title,x.Hint,x.RewardXp,x.RewardStars,completed.Contains(x.Id),x.Action)).ToArray();
    }
    public static IReadOnlyList<GameItem> Catalog {get;}=LoadCatalog();
    public static IReadOnlyList<GameAchievementDefinition> AchievementsCatalog=>Achievements;
    public static bool IsTask(string id)=>TaskCatalog.Any(x=>x.Id==id);
    public static IReadOnlyList<GameSlotDefinition> DecorSlots {get;}=
    [
        new("desk","书桌",-1.4,-.55,"surface"),new("shelf","墙边",2.15,-2.12,"surface"),new("garden","花园",2.25,1.65,"ground"),
        new("window","窗台",.3,-3.0,"surface"),new("bedside","小窝旁",3.4,1.2,"surface"),new("wall-left","左墙",-3.8,-.6,"wall"),
        new("wall-right","右墙",3.9,-.6,"wall"),new("garden-left","花圃左侧",-3.0,4.5,"ground"),new("garden-right","花圃右侧",3.0,4.5,"ground"),
        new("floor","前庭地面",0,3.35,"ground")
    ];
    public static IReadOnlyList<GameItem> Discoveries=>Catalog.Where(x=>x.Kind=="discovery").ToArray();
    public static bool IsDecorSlot(string id)=>DecorSlots.Any(x=>x.Id==id);
    public static bool IsAchievementRecord(string id)
    {
        if(AchievementsCatalog.Any(x=>x.Id==id))return true;
        if(id.StartsWith("model-used-",StringComparison.Ordinal))return new[]{"sprout","cat","fox","penguin"}.Contains(id[11..],StringComparer.Ordinal);
        if(id.StartsWith("toy-used-",StringComparison.Ordinal))return Catalog.Any(x=>x.Kind=="toy"&&x.Id==id[9..]);
        return false;
    }
    public static GameItem? NextDiscovery(DateOnly day,IReadOnlySet<string> known,string weather="clear",int localHour=12)
    {
        var remaining=Discoveries.Where(x=>!known.Contains(x.Id)).ToArray();if(remaining.Length==0)return null;
        string season=day.Month switch{3 or 4 or 5=>"spring",6 or 7 or 8=>"summer",9 or 10 or 11=>"autumn",_=>"winter"};
        var conditions=new HashSet<string>(StringComparer.Ordinal){"any",weather,season,localHour is >=19 or <7?"night":"day"};
        var eligible=remaining.Where(x=>conditions.Contains(x.Condition)).ToArray();
        if(eligible.Length==0)eligible=remaining.Where(x=>x.Condition=="any").ToArray();
        if(eligible.Length==0)return null;
        int weight(GameItem item)=>item.Rarity switch{"rare"=>1,"uncommon"=>3,_=>8};
        var random=new Random(unchecked(day.DayNumber*397+known.Count*7919));int pick=random.Next(eligible.Sum(weight));
        foreach(var item in eligible){pick-=weight(item);if(pick<0)return item;}
        return eligible[^1];
    }
    private static IReadOnlyList<GameItem> LoadCatalog()
    {
        return Load<GameItem[]>("game-catalog.json");
    }
    private static T Load<T>(string name)
    {using var stream=typeof(GameProgression).Assembly.GetManifestResourceStream("Forme.Core."+name)??throw new InvalidDataException("本地游戏内容资源缺失。");return JsonSerializer.Deserialize<T>(stream)??throw new InvalidDataException("本地游戏内容格式无效。");}
}
