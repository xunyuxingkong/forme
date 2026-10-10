using Forme.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class GameProgressionTests
{
    [TestMethod]
    public void LocalContentCatalogsMeetTheReviewedExpansionTargets()
    {
        Assert.AreEqual(14,GameProgression.TaskCatalog.Count);
        Assert.AreEqual(30,GameProgression.Catalog.Count(x=>x.Kind is "decor" or "rug"));
        Assert.AreEqual(6,GameProgression.Catalog.Count(x=>x.Kind=="toy"));
        Assert.AreEqual(16,GameProgression.Discoveries.Count);
        Assert.AreEqual(30,GameProgression.AchievementsCatalog.Count);
        Assert.AreEqual(20,LifeRules.Catalog.Length);
        Assert.AreEqual(10,GameProgression.DecorSlots.Count);
        for(int i=0;i<GameProgression.TaskCatalog.Count;i++)
        {
            var day=new DateOnly(2026,10,1).AddDays(i);
            var tasks=GameProgression.DailyTasks(day,new HashSet<string>(StringComparer.Ordinal));
            Assert.AreEqual(3,tasks.Count);
            Assert.AreEqual(3,tasks.Select(x=>x.Action).Distinct(StringComparer.Ordinal).Count());
        }
    }

    [TestMethod]
    public void OutdoorDiscoveryConditionsRemainOptionalAndDoNotHideItemsPermanently()
    {
        var wanted=GameProgression.Discoveries.ToDictionary(x=>x.Id,x=>x,StringComparer.Ordinal);
        void Available(string id,DateOnly day,string weather,int hour)
        {
            var known=wanted.Keys.Where(x=>x!=id).ToHashSet(StringComparer.Ordinal);
            Assert.AreEqual(id,GameProgression.NextDiscovery(day,known,weather,hour)?.Id);
        }
        Available("discovery-glass",new(2026,4,15),"rain",12);
        Available("discovery-raindrop",new(2026,4,15),"rain",12);
        Available("discovery-snowflake",new(2026,1,15),"snow",12);
        Available("discovery-moonstone",new(2026,10,15),"clear",22);
        Available("discovery-blossom",new(2026,4,15),"clear",12);
        Available("discovery-shell-pink",new(2026,7,15),"clear",12);
        Available("discovery-maple",new(2026,10,15),"clear",12);
        Available("discovery-ice-bead",new(2026,1,15),"clear",12);
    }

    [TestMethod]
    public void PetProfilesPrioritizeDifferentFurnitureAndToyReactions()
    {
        Assert.AreEqual("cushion",PetBehaviors.For("sprout").FavoriteFurniture);
        Assert.AreEqual("fish",PetBehaviors.For("cat").FavoriteFurniture);
        Assert.AreEqual("book",PetBehaviors.For("fox").FavoriteFurniture);
        Assert.AreEqual("frisbee-sky",PetBehaviors.For("penguin").FavoriteToy);
        Assert.AreEqual(PetAction.DanceHop,PetBehaviors.ToyReaction("cat","feather"));
        Assert.AreEqual(PetAction.DanceSpin,PetBehaviors.ToyReaction("fox","plane"));
        Assert.AreEqual(PetAction.DanceSway,PetBehaviors.ToyReaction("penguin","frisbee"));
        Assert.AreEqual(PetAction.Cheer,PetBehaviors.ToyReaction("sprout","ball"));
    }

    [TestMethod]
    public void ActionsAwardOnlySelectedDailyTasksAndProgressMilestonesLocally()
    {
        var dir=Path.GetFullPath(Path.Combine("artifacts","game-content-tests",Guid.NewGuid().ToString("N")));
        try
        {
            using var store=new Store(dir);var firstDay=new DateOnly(2026,10,1);
            for(int offset=0;offset<GameProgression.TaskCatalog.Count;offset++)
            {
                var day=firstDay.AddDays(offset);var tasks=store.GameTasks(day);
                foreach(var task in tasks)Assert.IsTrue(store.RecordGameAction(day,task.Action),$"Action {task.Action} should complete its selected task.");
                Assert.IsTrue(store.GameTasks(day).All(x=>x.Completed));
            }
            Assert.AreEqual(280,store.GameProgress().Experience);
            Assert.AreEqual(42,store.GameProgress().Stars);
            var achievementIds=store.GameAchievements().Select(x=>x.Id).ToHashSet(StringComparer.Ordinal);
            Assert.IsTrue(achievementIds.Contains("tasks-20"));
            Assert.IsFalse(achievementIds.Contains("tasks-50"));

            foreach(var item in GameProgression.Catalog.Where(x=>x.Kind is "decor" or "rug").Take(10))Assert.IsTrue(store.PurchaseGameItem(item.Id));
            Assert.IsTrue(store.GameAchievements().Any(x=>x.Id=="decor-10"));
            var chosen=GameProgression.Catalog.First(x=>x.Kind is "decor" or "rug");
            Assert.IsTrue(store.PlaceGameItem("window",chosen.Id));
            Assert.AreEqual(chosen.Id,store.GameSlots()["window"]);
            Assert.IsTrue(store.ClearGameSlot("window"));
            Assert.IsFalse(store.ClearGameSlot("bad-slot"));

            Assert.IsTrue(store.RecordToyPlay(firstDay,"ball-yellow"));
            store.RecordPetModels("sprout","cat",firstDay);store.RecordPetModels("cat","fox",firstDay);store.RecordPetModels("fox","penguin",firstDay);
            Assert.IsTrue(store.GameAchievements().Any(x=>x.Id=="toy-first"));
            Assert.IsTrue(store.GameAchievements().Any(x=>x.Id=="pet-all"));
            Store.ValidateExport(store.Export(false,false,false,true));
        }
        finally
        {
            var root=Path.GetFullPath(Path.Combine("artifacts","game-content-tests"))+Path.DirectorySeparatorChar;
            Assert.IsTrue(dir.StartsWith(root,StringComparison.OrdinalIgnoreCase));
            if(Directory.Exists(dir))Directory.Delete(dir,true);
        }
    }
}
