using Microsoft.Data.Sqlite;

namespace Forme.Core;

public sealed partial class Store
{
    public GameProgress GameProgress()
    {
        using var command=Command("SELECT xp,stars FROM game_progress WHERE id=1");using var row=command.ExecuteReader();
        if(!row.Read())throw new InvalidDataException("成长数据缺失。");int xp=row.GetInt32(0);return new(xp,Forme.Core.GameProgression.LevelFor(xp),row.GetInt32(1));
    }
    public IReadOnlyList<GameTask> GameTasks(DateOnly day)
    {
        var completed=Query("SELECT task_id FROM game_tasks WHERE day=$0 AND completed=1",r=>r.GetString(0),day.ToString("yyyy-MM-dd")).ToHashSet(StringComparer.Ordinal);
        return GameProgression.DailyTasks(day,completed);
    }
    public IReadOnlyList<GameInventoryItem> GameInventory()=>Query("SELECT item_id,quantity FROM game_inventory WHERE quantity>0 ORDER BY item_id",r=>new GameInventoryItem(r.GetString(0),r.GetInt32(1)));
    public IReadOnlyList<GameDiscovery> GameDiscoveries()=>Query("SELECT item_id,day FROM game_discoveries ORDER BY day,item_id",r=>new GameDiscovery(r.GetString(0),r.GetString(1)));
    public IReadOnlyList<GameAchievement> GameAchievements()=>Query("SELECT id,day FROM game_achievements ORDER BY day,id",r=>new GameAchievement(r.GetString(0),r.GetString(1)));
    public IReadOnlyDictionary<string,string> GameSlots()=>Query("SELECT slot_id,item_id FROM game_slots",r=>new KeyValuePair<string,string>(r.GetString(0),r.GetString(1))).ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);
    internal GameExport GameExportData()=>new(GameProgress().Experience,GameProgress().Stars,
        GameInventory().ToList(),Query("SELECT day,task_id FROM game_tasks WHERE completed=1 ORDER BY day,task_id",r=>new GameTaskRecord(r.GetString(0),r.GetString(1))),
        Query("SELECT day,xp FROM game_daily",r=>new GameDailyRecord(r.GetString(0),r.GetInt32(1))),GameDiscoveries().ToList(),
        GameSlots().Select(x=>new GameSlot(x.Key,x.Value)).ToList(),GameAchievements().ToList());
    internal static void ValidateGameExport(GameExport game)
    {
        if(game.Experience<0||game.Experience>1_000_000||game.Stars<0||game.Stars>1_000_000||game.Inventory.Count>200||game.Tasks.Count>100_000||game.Daily.Count>100_000||game.Discoveries.Count>100||game.Slots.Count>20||game.Achievements.Count>100)throw new InvalidDataException("成长数据超出安全范围。");
        var catalog=GameProgression.Catalog.ToDictionary(x=>x.Id,StringComparer.Ordinal);
        if(game.Inventory.Any(x=>!catalog.TryGetValue(x.ItemId,out var item)||x.Quantity<0||x.Quantity>item.MaxOwned)||game.Inventory.Select(x=>x.ItemId).Distinct(StringComparer.Ordinal).Count()!=game.Inventory.Count)throw new InvalidDataException("收藏库存无效。");
        bool Day(string d)=>DateOnly.TryParseExact(d,"yyyy-MM-dd",out _);
        if(game.Tasks.Any(x=>!Day(x.Day)||!GameProgression.IsTask(x.TaskId))||game.Tasks.Distinct().Count()!=game.Tasks.Count)throw new InvalidDataException("每日任务记录无效。");
        if(game.Daily.Any(x=>!Day(x.Day)||x.Experience is <0 or >20)||game.Daily.Select(x=>x.Day).Distinct(StringComparer.Ordinal).Count()!=game.Daily.Count)throw new InvalidDataException("每日成长记录无效。");
        if(game.Discoveries.Any(x=>!catalog.TryGetValue(x.ItemId,out var item)||item.Kind!="discovery"||!Day(x.Day))||game.Discoveries.Select(x=>x.ItemId).Distinct(StringComparer.Ordinal).Count()!=game.Discoveries.Count)throw new InvalidDataException("户外收藏记录无效。");
        var owned=game.Inventory.Where(x=>x.Quantity>0).Select(x=>x.ItemId).ToHashSet(StringComparer.Ordinal);
        if(game.Slots.Any(x=>!GameProgression.IsDecorSlot(x.SlotId)||!catalog.TryGetValue(x.ItemId,out var item)||item.Kind is not ("decor" or "rug")||!owned.Contains(x.ItemId))||game.Slots.Select(x=>x.SlotId).Distinct(StringComparer.Ordinal).Count()!=game.Slots.Count||game.Slots.Select(x=>x.ItemId).Distinct(StringComparer.Ordinal).Count()!=game.Slots.Count)throw new InvalidDataException("家具摆放数据无效。");
        if(game.Achievements.Any(x=>!GameProgression.IsAchievementRecord(x.Id)||!Day(x.Day))||game.Achievements.Select(x=>x.Id).Distinct(StringComparer.Ordinal).Count()!=game.Achievements.Count)throw new InvalidDataException("成就记录无效。");
    }
    internal void ReplaceGame(GameExport game,SqliteTransaction tx)
    {
        ValidateGameExport(game);Exec("UPDATE game_progress SET xp=$0,stars=$1 WHERE id=1",tx,game.Experience,game.Stars);
        Exec("DELETE FROM game_inventory; DELETE FROM game_tasks; DELETE FROM game_daily; DELETE FROM game_discoveries; DELETE FROM game_slots; DELETE FROM game_achievements;",tx);
        foreach(var x in game.Inventory)Exec("INSERT INTO game_inventory VALUES($0,$1)",tx,x.ItemId,x.Quantity);
        foreach(var x in game.Tasks)Exec("INSERT INTO game_tasks VALUES($0,$1,1)",tx,x.Day,x.TaskId);
        foreach(var x in game.Daily)Exec("INSERT INTO game_daily VALUES($0,$1)",tx,x.Day,x.Experience);
        foreach(var x in game.Discoveries)Exec("INSERT INTO game_discoveries VALUES($0,$1)",tx,x.ItemId,x.Day);
        foreach(var x in game.Slots)Exec("INSERT INTO game_slots VALUES($0,$1)",tx,x.SlotId,x.ItemId);
        foreach(var x in game.Achievements)Exec("INSERT INTO game_achievements VALUES($0,$1)",tx,x.Id,x.Day);
        if(!game.Inventory.Any(x=>x.ItemId=="ball-yellow"&&x.Quantity>0))Exec("INSERT INTO game_inventory VALUES('ball-yellow',1)",tx);
    }
    public bool CompleteGameTask(DateOnly day,string taskId)
    {
        using var tx=_db.BeginTransaction();bool changed=CompleteGameTask(tx,day,taskId);if(changed)tx.Commit();return changed;
    }
    public bool RecordGameAction(DateOnly day,params string[] actions)
    {
        if(actions is null||actions.Length==0)return false;
        using var tx=_db.BeginTransaction();bool changed=RecordGameActions(tx,day,actions);if(changed)tx.Commit();return changed;
    }
    private bool RecordGameActions(SqliteTransaction tx,DateOnly day,IEnumerable<string> actions)
    {
        var requested=actions.ToHashSet(StringComparer.Ordinal);if(requested.Count==0)return false;
        var selected=GameProgression.DailyTasks(day,new HashSet<string>(StringComparer.Ordinal));bool changed=false;
        foreach(var task in selected.Where(x=>requested.Contains(x.Action)))changed|=CompleteGameTask(tx,day,task.Id);
        return changed;
    }
    private bool CompleteGameTask(SqliteTransaction tx,DateOnly day,string taskId)
    {
        if(!GameProgression.IsTask(taskId))throw new ArgumentException("任务无效。",nameof(taskId));
        var definition=GameProgression.DailyTasks(day,new HashSet<string>(StringComparer.Ordinal)).FirstOrDefault(x=>x.Id==taskId);if(definition is null)return false;
        string date=day.ToString("yyyy-MM-dd");
        using(var prior=Command("SELECT completed FROM game_tasks WHERE day=$0 AND task_id=$1",tx,date,taskId))
        using(var row=prior.ExecuteReader())if(row.Read()&&row.GetInt32(0)==1)return false;
        Exec("INSERT INTO game_tasks(day,task_id,completed) VALUES($0,$1,1) ON CONFLICT(day,task_id) DO UPDATE SET completed=1",tx,date,taskId);
        Exec("INSERT INTO game_daily(day,xp) VALUES($0,0) ON CONFLICT(day) DO NOTHING",tx,date);
        Exec("UPDATE game_progress SET xp=xp+MIN($0,MAX(0,20-(SELECT xp FROM game_daily WHERE day=$1))),stars=stars+$2 WHERE id=1",tx,definition.RewardXp,date,definition.RewardStars);
        Exec("UPDATE game_daily SET xp=MIN(20,xp+$1) WHERE day=$0",tx,date,definition.RewardXp);
        GrantGameAchievement(tx,"task-"+taskId,date);
        using(var count=Command("SELECT count(*) FROM game_tasks WHERE completed=1",tx))
        {
            int total=Convert.ToInt32(count.ExecuteScalar());if(total>=5)GrantGameAchievement(tx,"tasks-5",date);if(total>=20)GrantGameAchievement(tx,"tasks-20",date);if(total>=50)GrantGameAchievement(tx,"tasks-50",date);
        }
        return true;
    }
    public bool DiscoverOutdoor(DateOnly day,string weather="clear",int localHour=12)
    {
        var known=GameDiscoveries().Select(x=>x.ItemId).ToHashSet(StringComparer.Ordinal);
        var next=GameProgression.NextDiscovery(day,known,weather,localHour);
        if(next is null)return CompleteGameTask(day,"discover");
        using var tx=_db.BeginTransaction();string date=day.ToString("yyyy-MM-dd");
        Exec("INSERT INTO game_discoveries(item_id,day) VALUES($0,$1)",tx,next.Id,date);Exec("INSERT INTO game_inventory VALUES($0,1) ON CONFLICT(item_id) DO UPDATE SET quantity=quantity+1",tx,next.Id);
        GrantGameAchievement(tx,"first-discovery",date);int total=known.Count+1;
        if(total>=5)GrantGameAchievement(tx,"discoveries-5",date);if(total>=10)GrantGameAchievement(tx,"discoveries-10",date);if(total>=GameProgression.Discoveries.Count)GrantGameAchievement(tx,"discoveries-all",date);
        RecordGameActions(tx,day,["discover"]);tx.Commit();return true;
    }
    public bool RecordBallPlay(DateOnly day)
    {
        return RecordToyPlay(day,"ball-yellow");
    }
    public bool RecordToyPlay(DateOnly day,string toyId)
    {
        if(!GameProgression.Catalog.Any(x=>x.Id==toyId&&x.Kind=="toy")||!GameInventory().Any(x=>x.ItemId==toyId&&x.Quantity>0))return false;
        using var tx=_db.BeginTransaction();string date=day.ToString("yyyy-MM-dd");string used="toy-used-"+toyId;
        GrantGameAchievement(tx,used,date);
        using(var c=Command("SELECT count(*) FROM game_achievements WHERE id LIKE 'toy-used-%'",tx))
        {
            int count=Convert.ToInt32(c.ExecuteScalar());if(count>=1)GrantGameAchievement(tx,"toy-first",date);if(count>=3)GrantGameAchievement(tx,"toy-3",date);if(count>=GameProgression.Catalog.Count(x=>x.Kind=="toy"))GrantGameAchievement(tx,"toy-all",date);
        }
        RecordGameActions(tx,day,toyId=="ball-yellow"?["toy","ball"]:["toy"]);tx.Commit();return true;
    }
    public void RecordBoatCompleted(DateOnly day)
    {
        using var tx=_db.BeginTransaction();string date=day.ToString("yyyy-MM-dd");GrantGameAchievement(tx,"boat-first",date);RecordGameActions(tx,day,["boat"]);tx.Commit();
    }
    public void RecordPetModels(string previous,string current,DateOnly day)
    {
        var valid=new HashSet<string>(["sprout","cat","fox","penguin"],StringComparer.Ordinal);if(!valid.Contains(previous)||!valid.Contains(current))return;
        using var tx=_db.BeginTransaction();string date=day.ToString("yyyy-MM-dd");
        GrantGameAchievement(tx,"model-used-"+previous,date);GrantGameAchievement(tx,"model-used-"+current,date);
        using(var c=Command("SELECT count(*) FROM game_achievements WHERE id LIKE 'model-used-%'",tx))if(Convert.ToInt32(c.ExecuteScalar())>=valid.Count)GrantGameAchievement(tx,"pet-all",date);
        if(previous!=current)RecordGameActions(tx,day,["model"]);tx.Commit();
    }
    public bool PurchaseGameItem(string itemId)
    {
        var item=GameProgression.Catalog.FirstOrDefault(x=>x.Id==itemId);if(item is null||item.Kind=="discovery")return false;
        using var tx=_db.BeginTransaction();
        using(var existing=Command("SELECT quantity FROM game_inventory WHERE item_id=$0",tx,itemId))
        using(var row=existing.ExecuteReader())if(row.Read()&&row.GetInt32(0)>=item.MaxOwned)return false;
        using(var wallet=Command("SELECT stars FROM game_progress WHERE id=1",tx))if(Convert.ToInt32(wallet.ExecuteScalar())<item.Price)return false;
        Exec("UPDATE game_progress SET stars=stars-$0 WHERE id=1",tx,item.Price);Exec("INSERT INTO game_inventory VALUES($0,1) ON CONFLICT(item_id) DO UPDATE SET quantity=quantity+1",tx,itemId);
        string date=DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");GrantGameAchievement(tx,"first-purchase",date);
        var decorIds=GameProgression.Catalog.Where(x=>x.Kind is "decor" or "rug").Select(x=>x.Id).ToArray();
        var placeholders=string.Join(",",Enumerable.Range(0,decorIds.Length).Select(i=>"$"+i));
        using(var owned=Command($"SELECT count(*) FROM game_inventory WHERE quantity>0 AND item_id IN ({placeholders})",tx,decorIds.Cast<object?>().ToArray()))
        {
            int count=Convert.ToInt32(owned.ExecuteScalar());if(count>=5)GrantGameAchievement(tx,"decor-5",date);if(count>=10)GrantGameAchievement(tx,"decor-10",date);
        }
        tx.Commit();return true;
    }
    public bool PlaceGameItem(string slot,string itemId)
    {
        if(!GameProgression.IsDecorSlot(slot)||!GameProgression.Catalog.Any(x=>x.Id==itemId&&(x.Kind is "decor" or "rug")))return false;
        using var tx=_db.BeginTransaction();using(var c=Command("SELECT quantity FROM game_inventory WHERE item_id=$0",tx,itemId))if(Convert.ToInt32(c.ExecuteScalar()??0)<1)return false;
        Exec("DELETE FROM game_slots WHERE item_id=$0",tx,itemId);
        Exec("INSERT INTO game_slots(slot_id,item_id) VALUES($0,$1) ON CONFLICT(slot_id) DO UPDATE SET item_id=excluded.item_id",tx,slot,itemId);tx.Commit();return true;
    }
    public bool ClearGameSlot(string slot)
    {
        if(!GameProgression.IsDecorSlot(slot))return false;using var command=Command("DELETE FROM game_slots WHERE slot_id=$0",null,slot);return command.ExecuteNonQuery()>0;
    }
    private void GrantGameAchievement(SqliteTransaction tx,string id,string day)=>Exec("INSERT OR IGNORE INTO game_achievements(id,day) VALUES($0,$1)",tx,id,day);
}
