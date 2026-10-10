using Forme.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
[TestClass]
public sealed class LivingTests
{
    [TestMethod]
    public void DefaultFurnitureHasReachableActionsAndSafeChannels()
    {
        var world=LivingWorld.Default();Assert.IsNull(world.PlacementProblem());var travel=world.Travel();travel.Reset(new(.15,1.25));foreach(var item in world.Items)Assert.IsNotNull(LivingWorld.Approach(item,travel));
        var copy=world.Copy();copy.Items.RemoveAt(0);Assert.AreEqual(6,world.Items.Count);
    }
    [TestMethod]
    public void InvalidOverlapBoundsAndUnknownFurnitureCannotBeSaved()
    {
        var world=LivingWorld.Default();world.Items.Add(world.Items[0] with{Id="copy"});Assert.IsNotNull(world.PlacementProblem());
        Assert.ThrowsExactly<InvalidDataException>(()=>(world with{Items=[new("bad","lamp",double.NaN,0,0)]}).Validate());
        Assert.ThrowsExactly<InvalidDataException>(()=>(world with{Items=[new("bad","lamp",0,0,45)]}).Validate());
        Assert.ThrowsExactly<InvalidDataException>(()=>(world with{Items=[new("bad","unknown",0,0,0)]}).Validate());
        Assert.ThrowsExactly<InvalidDataException>(()=>(world with{Items=[new("bad","sleep",4.2,0,0)]}).Validate());
    }
    [TestMethod]
    public void EightRulesDependOnLayoutWeatherAndLight()
    {
        var world=new LivingWorld([new("a","cushion",0,-2,0),new("b","book",1,-2,0),new("c","lamp",0,-1,0),new("d","sleep",.5,-1,0),new("e","feed",.5,0,0),new("f","fish",-.5,-2,0),new("g","cushion",0,3,0)],[]);
        var rain=LifeRules.Evaluate(world,"rain",false,true).Select(e=>e.Id).ToHashSet();Assert.IsTrue(rain.Contains("rain-reading"));Assert.IsFalse(rain.Contains("sun-nap"));Assert.IsFalse(LifeRules.Evaluate(world,"rain",false,false).Any(e=>e.Id=="rain-reading"));
        var ids=rain.Concat(LifeRules.Evaluate(world,"clear",false,true).Select(e=>e.Id)).Concat(LifeRules.Evaluate(world,"clear",true,true).Select(e=>e.Id)).Distinct().ToArray();CollectionAssert.AreEquivalent(LifeRules.Ids,ids);
        var separated=world with{Items=world.Items.Select((i,n)=>i with{X=n*10,Z=0}).ToList()};Assert.AreEqual(0,LifeRules.Evaluate(separated,"clear",false,false).Count);
    }
    [TestMethod]
    public void LifeEventsRemainSafeWithEmptyOrPartialFurnitureLayouts()
    {
        var empty=new LivingWorld([],[]);
        Assert.AreEqual(0,LifeRules.Evaluate(empty,"rain",true,true).Count);
        foreach(var kind in LivingWorld.Catalog.Select(x=>x.Id))
        {
            var partial=LivingWorld.Default() with{Items=LivingWorld.Default().Items.Where(x=>x.Kind!=kind).ToList()};
            Assert.IsNotNull(LifeRules.Evaluate(partial,"rain",false,true));
            Assert.IsNotNull(LifeRules.Evaluate(partial,"clear",true,false));
        }
        var importedPartial=new LivingWorld([new("chair","cushion",0,-2,0)],[]);
        Assert.IsNotNull(LifeRules.Evaluate(importedPartial,"clear",false,false));
    }
    [TestMethod]
    public void LayoutAndDiscoveriesRoundTripAndOlderBackupsPreserveThem()
    {
        var dir=Path.GetFullPath(Path.Combine("artifacts","living-tests",Guid.NewGuid().ToString("N")));
        try{using var store=new Store(dir);var world=LivingWorld.Default();store.SaveWorld(world);Assert.IsTrue(store.DiscoverLife("reading"));Assert.IsFalse(store.DiscoverLife("reading"));var file=Path.Combine(dir,"world.json");store.ExportFile(file,false,false,false,true);store.SaveWorld(LivingWorld.Default());using(var prepared=Store.PrepareImport(file))store.ApplyImport(prepared);Assert.AreEqual("reading",store.World().Discoveries.Single());
            store.Import(new(){SchemaVersion=4,Room=new("团团","day","cream","none",[])});Assert.AreEqual("reading",store.World().Discoveries.Single());
            world.Items.Add(world.Items[0] with{Id="overlap"});Assert.ThrowsExactly<InvalidDataException>(()=>store.SaveWorld(world));Assert.AreEqual(6,store.World().Items.Count);Assert.ThrowsExactly<InvalidDataException>(()=>store.DiscoverLife("arbitrary"));store.ResetAll();Assert.AreEqual(0,store.World().Discoveries.Count);
        }finally{var root=Path.GetFullPath("artifacts/living-tests")+Path.DirectorySeparatorChar;Assert.IsTrue(dir.StartsWith(root,StringComparison.OrdinalIgnoreCase));if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
