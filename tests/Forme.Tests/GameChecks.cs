using Forme.Core;

internal static class GameChecks
{
    public static void Run(string root,Action<bool,string> check,Action<Action,string> throws)
    {
        var day=new DateOnly(2026,10,9);
        using var store=new Store(Path.Combine(root,"game"));
        check(store.GameProgress()==new GameProgress(0,1,0),"new local game starts at level one with no currency");
        check(store.GameInventory().Single(x=>x.ItemId=="ball-yellow").Quantity==1,"starter ball is available offline");
        var tasks=store.GameTasks(day);string first=tasks[0].Id;
        check(store.CompleteGameTask(day,first)&&!store.CompleteGameTask(day,first),"daily task reward is idempotent");
        foreach(var task in tasks.Skip(1))store.CompleteGameTask(day,task.Id);
        check(store.GameProgress().Experience==20&&store.GameProgress().Stars==3,"daily experience cap preserves all three star rewards");
        check(store.DiscoverOutdoor(day)&&store.GameDiscoveries().Count==1,"outdoor discovery is persisted locally");
        check(store.PurchaseGameItem("plant-pot")&&store.PlaceGameItem("desk","plant-pot"),"owned decor can be placed in a stable room slot");
        check(!store.PurchaseGameItem("plant-pot")&&!store.PlaceGameItem("shelf","lamp-paper"),"catalog ownership and slot placement are checked");
        var export=store.Export(false,false,false,true);Store.ValidateExport(export);
        using var restored=new Store(Path.Combine(root,"game-restored"));restored.Import(export);
        check(restored.GameProgress()==store.GameProgress()&&restored.GameSlots().SequenceEqual(store.GameSlots()),"room backup restores progression and furniture placement");
        check(restored.GameDiscoveries().SequenceEqual(store.GameDiscoveries())&&restored.GameAchievements().Count==store.GameAchievements().Count,"room backup restores collection and achievements");
        string path=Path.Combine(root,"game-export.json");store.ExportFile(path,false,false,false,true);
        using(var plan=Store.PrepareImport(path)){using var streamed=new Store(Path.Combine(root,"game-streamed"));streamed.ApplyImport(plan);check(streamed.GameProgress()==store.GameProgress()&&streamed.GameSlots().SequenceEqual(store.GameSlots()),"streaming backup restores game progress and room slots");}
        var invalid=store.Export(false,false,false,true);invalid.Room=invalid.Room! with{Game=invalid.Room.Game! with{Stars=-1}};
        throws(()=>Store.ValidateExport(invalid),"invalid game payload rejected");
    }
}
