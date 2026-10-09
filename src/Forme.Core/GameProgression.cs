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
        return Enumerable.Range(0,Math.Min(3,TaskCatalog.Count)).Select(i=>TaskCatalog[(rotation+i)%TaskCatalog.Count]).Select(x=>new GameTask(x.Id,x.Title,x.Hint,x.RewardXp,x.RewardStars,completed.Contains(x.Id))).ToArray();
    }
    public static IReadOnlyList<GameItem> Catalog {get;}=LoadCatalog();
    public static IReadOnlyList<GameAchievementDefinition> AchievementsCatalog=>Achievements;
    public static bool IsTask(string id)=>TaskCatalog.Any(x=>x.Id==id);
    private static IReadOnlyList<GameItem> LoadCatalog()
    {
        return Load<GameItem[]>("game-catalog.json");
    }
    private static T Load<T>(string name)
    {using var stream=typeof(GameProgression).Assembly.GetManifestResourceStream("Forme.Core."+name)??throw new InvalidDataException("本地游戏内容资源缺失。");return JsonSerializer.Deserialize<T>(stream)??throw new InvalidDataException("本地游戏内容格式无效。");}
}
