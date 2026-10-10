using Forme.Core;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Forme.Tests;
[TestClass]
public sealed class ContentTests
{
    [TestMethod]
    public void CatalogAndStableIdsAreValidated()
    {
        ContentCatalogValidator.Validate(GameProgression.Catalog,GameProgression.TaskCatalog,GameProgression.AchievementsCatalog,LifeRules.Catalog);
        var baseline=JsonSerializer.Deserialize<Dictionary<string,string[]>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"content-ids-0.6.json")))!;
        foreach(var (key,ids) in new[]{("Items",GameProgression.Catalog.Select(x=>x.Id)),("Tasks",GameProgression.TaskCatalog.Select(x=>x.Id)),("Achievements",GameProgression.AchievementsCatalog.Select(x=>x.Id))})
            foreach(string stable in baseline[key])Assert.IsTrue(ids.Contains(stable),"Persisted ID removed: "+stable);
        CollectionAssert.IsSubsetOf(baseline["Life"],LifeRules.Ids);
        var invalid=GameProgression.Catalog.ToArray();invalid[0]=invalid[0] with{Color="red"};
        Assert.ThrowsExactly<InvalidDataException>(()=>ContentCatalogValidator.Validate(invalid,GameProgression.TaskCatalog,GameProgression.AchievementsCatalog,LifeRules.Catalog));
        invalid=GameProgression.Catalog.Append(GameProgression.Catalog[0]).ToArray();
        Assert.ThrowsExactly<InvalidDataException>(()=>ContentCatalogValidator.Validate(invalid,GameProgression.TaskCatalog,GameProgression.AchievementsCatalog,LifeRules.Catalog));
    }
    [TestMethod]
    public void DailyTasksRemainDeterministicDiverseAndDoNotRepeatAdjacentDays()
    {
        var day=new DateOnly(2026,1,1);var completed=new HashSet<string>();
        for(int i=0;i<730;i++)
        {
            var today=GameProgression.DailyTasks(day.AddDays(i),completed);var yesterday=GameProgression.DailyTasks(day.AddDays(i-1),completed);
            Assert.AreEqual(3,today.Count);Assert.AreEqual(3,today.Select(x=>x.Action).Distinct().Count());
            Assert.IsTrue(today.Select(x=>x.Id).Intersect(yesterday.Select(x=>x.Id)).Count()<=1);
            CollectionAssert.AreEqual(today.ToArray(),GameProgression.DailyTasks(day.AddDays(i),completed).ToArray());
        }
    }
    [TestMethod]
    public void MorningSnackUsesActualHour()
    {
        var world=new LivingWorld([new("a","feed",0,0,0),new("b","book",1,0,0)],[]);
        Assert.IsTrue(LifeRules.Evaluate(world,new("clear",8,"autumn",false,true)).Any(x=>x.Id=="morning-snack"));
        Assert.IsFalse(LifeRules.Evaluate(world,new("clear",14,"autumn",false,true)).Any(x=>x.Id=="morning-snack"));
    }
    [TestMethod]
    public void CooldownSurvivesRestartAndDoesNotPreventLaterDiscoveries()
    {
        string root=Path.GetFullPath(Path.Combine("artifacts","content-tests",Guid.NewGuid().ToString("N")));var day=new DateOnly(2026,10,10);var at=new DateTimeOffset(2026,10,10,12,0,0,TimeSpan.FromHours(8));
        try
        {
            using(var store=new Store(root)){Assert.IsTrue(store.DiscoverOutdoor(day,now:at));Assert.IsFalse(store.DiscoverOutdoor(day,now:at.AddSeconds(1)));Assert.AreEqual(1,store.GameDiscoveries().Count);}
            using(var store=new Store(root)){Assert.IsFalse(store.DiscoverOutdoor(day,now:at.AddMinutes(1)));Assert.IsTrue(store.DiscoverOutdoor(day,now:at.AddMinutes(5)));Assert.AreEqual(2,store.GameDiscoveries().Count);}
        }
        finally{Assert.IsTrue(root.StartsWith(Path.GetFullPath("artifacts/content-tests")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [TestMethod]
    public void RevisionMetadataDoesNotOverwriteCustomRules()
    {
        var current=ActionRules.Default();Assert.AreEqual(ActionRules.CurrentBuiltinRevision,current.BuiltinRevision);
        string legacy=current.Serialize().Replace("\"builtinRevision\": 4","\"builtinRevision\": 0",StringComparison.Ordinal);
        Assert.AreEqual(4,ActionRules.Parse(legacy).UpgradeDefaults().BuiltinRevision);
        var edited=current with{Rules=[new(["来玩吧"],[new("boat","open")])]};Assert.AreSame(edited,edited.UpgradeDefaults());
    }
}
