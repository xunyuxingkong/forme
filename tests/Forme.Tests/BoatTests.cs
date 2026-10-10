using Forme.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class BoatTests
{
    [TestMethod]
    public void SharedLaunchAndCheerPosesReachThenReturnToIdle()
    {
        foreach(var action in new[]{PetAction.Launch,PetAction.Cheer})
        {
            var motion=new PetMotion();motion.Play(action,0);var pose=motion.Sample(.8);
            Assert.IsTrue(double.IsFinite(pose.Reach)&&pose.Reach>0&&pose.Reach<=1);Assert.AreEqual("happy",pose.Expression);
            Assert.AreEqual(0d,motion.Sample(6).Reach);Assert.IsFalse(motion.Reacting);
        }
    }
    private static PaperBoat Finish(BoatLayout layout,double step=.05)
    {var boat=new PaperBoat(layout);for(int i=0;i<3000&&!boat.Complete;i++)boat.Advance(step);Assert.IsTrue(boat.Complete);return boat;}
    [TestMethod]
    public void WindChangesDestinationAndPredictionMatchesActualTravel()
    {
        foreach(var (wind,dock) in new[]{(-1,"willow"),(0,"bridge"),(1,"lily")})
        {
            var layout=BoatLayout.Default() with{Wind=wind};var boat=Finish(layout);Assert.AreEqual(dock,boat.Dock);
            var predicted=PaperBoat.Predict(layout).Last();Assert.AreEqual(boat.Position.X,predicted.X,.01);Assert.AreEqual(boat.Position.Z,predicted.Z,.01);
        }
    }
    [TestMethod]
    public void LeafPlacementShapeAndFrameRateHaveObservableEffects()
    {
        var plain=BoatLayout.Default();var guide=plain with{Leaves=[new(-1.25,0,70),new(0,.55,70),new(1.1,1.05,70)]};
        Assert.IsTrue(Finish(guide).Position.Z-Finish(plain).Position.Z>.4);
        Assert.IsTrue(Finish(plain with{Shape="wide"}).Elapsed>Finish(plain).Elapsed);
        var fast=Finish(guide,.02);var slow=Finish(guide,.1);Assert.AreEqual(fast.Position.Z,slow.Position.Z,.001);
        var game=new PaperBoat(plain);plain.Leaves[0]=new(0,0,70);Assert.AreNotEqual(plain.Leaves[0],game.Layout.Leaves[0]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(()=>game.Advance(double.NaN));
        Assert.ThrowsExactly<InvalidDataException>(()=>new PaperBoat(guide with{Leaves=[new(double.NaN,0,0),new(0,0,0),new(0,0,0)]}));
    }
    [TestMethod]
    public void SouvenirsAreIdempotentExportableDeletableAndOlderBackupsPreserveThem()
    {
        string root=Path.GetFullPath(Path.Combine("artifacts","boat-tests"));string directory=Path.Combine(root,Guid.NewGuid().ToString("N"));
        try
        {
            using var store=new Store(directory);var item=new BoatSouvenir(Guid.NewGuid().ToString("N"),"第一次小旅行",BoatLayout.Default(),"bridge",false,"cat",DateTimeOffset.Now);
            store.KeepBoat(item);store.KeepBoat(item);Assert.AreEqual(1,store.Boats().Entries.Count);Assert.AreEqual(item.Id,store.Boats().DisplayedId);
            var file=Path.Combine(directory,"boats.json");store.ExportFile(file,false,false,false,true);
            store.DeleteBoat(item.Id);Assert.AreEqual(0,store.Boats().Entries.Count);
            using(var plan=Store.PrepareImport(file))store.ApplyImport(plan);
            Assert.AreEqual(item.Title,store.Boats().Entries.Single().Title);Assert.AreEqual(item.Id,store.Boats().DisplayedId);
            store.Import(new ExportDocument{SchemaVersion=3,Room=new("团团","day","cream","none",[])});
            Assert.AreEqual(1,store.Boats().Entries.Count);
            Assert.ThrowsExactly<InvalidDataException>(()=>store.DisplayBoat("missing"));
            store.DeleteBoat(item.Id);Assert.IsNull(store.Boats().DisplayedId);Assert.IsFalse(File.Exists(store.BackupPath));
            store.KeepBoat(item);store.ResetAll();Assert.AreEqual(0,store.Boats().Entries.Count);
        }
        finally{Assert.IsTrue(directory.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
    [TestMethod]
    public void InvalidAlbumsCannotReplaceExistingData()
    {
        var item=new BoatSouvenir(Guid.NewGuid().ToString("N"),"船",BoatLayout.Default(),"bridge",false,"cat",DateTimeOffset.Now);
        Assert.ThrowsExactly<InvalidDataException>(()=>new BoatAlbum([item,item],null).Validate());
        Assert.ThrowsExactly<InvalidDataException>(()=>new BoatAlbum([item],"unknown").Validate());
        Assert.ThrowsExactly<InvalidDataException>(()=>new BoatAlbum([item with{Dock="arbitrary"}],null).Validate());
        Assert.ThrowsExactly<InvalidDataException>(()=>new BoatAlbum(Enumerable.Range(0,61).Select(_=>item with{Id=Guid.NewGuid().ToString("N")}).ToList(),null).Validate());
    }
    [TestMethod]
    public void GuidingThroughThePetalCreatesAnAdditionalDiscoverableOutcome()
    {
        var layout=BoatLayout.Default() with{Leaves=[new(-1.8,0,70),new(-.8,.45,70),new(.3,.95,70)]};
        var boat=Finish(layout);Assert.IsTrue(boat.PetalFound);Assert.AreEqual("lily",boat.Dock);Assert.IsFalse(Finish(BoatLayout.Default()).PetalFound);
    }
}
