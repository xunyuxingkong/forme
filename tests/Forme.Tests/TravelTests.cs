using Forme.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class TravelTests
{
    [TestMethod,TestCategory("Motion")]
    public void MovementAvoidsFurnitureAndArrivesExactly()
    {
        var travel=new PetTravel(-10,10,-10,10,[new(0,0,2,3)]);travel.Reset(new(-4,0));
        Assert.IsTrue(travel.MoveTo(new(4,0)));Assert.IsTrue(travel.Running);
        for(int frame=0;frame<1000&&travel.Moving;frame++){travel.Advance(1d/30);Assert.IsTrue(travel.Walkable(travel.Position),"Path crossed inflated furniture");}
        Assert.IsFalse(travel.Moving);Assert.AreEqual(new GroundPoint(4,0),travel.Position);
    }
    [TestMethod,TestCategory("Motion")]
    public void RetargetStopBoundsAndReducedMotion()
    {
        var travel=new PetTravel(-10,10,-10,10,[]);travel.Reset(new(0,0));
        Assert.IsFalse(travel.MoveTo(new(10,0)));Assert.IsFalse(travel.MoveTo(new(double.NaN,0)));
        Assert.IsTrue(travel.MoveTo(new(1,0)));Assert.IsFalse(travel.Running);travel.Advance(.1);
        Assert.IsTrue(travel.MoveTo(new(-1,0)));for(int i=0;i<100;i++)travel.Advance(.1);
        Assert.AreEqual(new GroundPoint(-1,0),travel.Position);
        travel.MoveTo(new(5,5));travel.Stop();var stopped=travel.Position;travel.Advance(.1);Assert.AreEqual(stopped,travel.Position);
        Assert.IsTrue(travel.MoveTo(new(5,5),true));Assert.IsFalse(travel.Moving);Assert.AreEqual(new GroundPoint(5,5),travel.Position);
    }
    [TestMethod,TestCategory("Motion")]
    public void UnreachableClickPreservesCurrentRoute()
    {
        var travel=new PetTravel(-5,5,-5,5,[new(0,0,.2,10)]);travel.Reset(new(-3,0));travel.MoveTo(new(-2,0));
        Assert.IsFalse(travel.MoveTo(new(3,0)));Assert.IsTrue(travel.Moving);
        for(int i=0;i<100;i++)travel.Advance(.1);Assert.AreEqual(new GroundPoint(-2,0),travel.Position);
    }

    [TestMethod,TestCategory("Motion")]
    public void SameCellShortPathStillChecksClearance()
    {
        var travel=new PetTravel(-5,5,-5,5,[new(0,0,.01,.01)]);travel.Reset(new(.3,0));
        Assert.AreEqual((int)Math.Round((.4+5)/.35),(int)Math.Round((travel.Position.X+5)/.35));
        Assert.IsFalse(travel.MoveTo(new(.4,0)),"Same-cell shortcut must check its blocked starting point");
        var clear=new PetTravel(-5,5,-5,5,[]);clear.Reset(new(0,0));
        Assert.IsTrue(clear.MoveTo(new(.1,0)));Assert.IsTrue(clear.Moving);clear.Advance(1);Assert.AreEqual(new GroundPoint(.1,0),clear.Position);
    }
}
