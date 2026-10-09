using Forme.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class IdleAndDanceTests
{
    [TestMethod,TestCategory("Motion")]
    public void DancesFinishAndCanBeReplaced()
    {
        foreach(var action in new[]{PetAction.DanceSway,PetAction.DanceHop,PetAction.DanceSpin})
        {
            var motion=new PetMotion();motion.Play(action,10);
            for(int frame=0;frame<180;frame++)
            {
                var pose=motion.Sample(10+frame/30d);
                Assert.IsTrue(double.IsFinite(pose.Yaw)&&pose.Lift<=.08&&Math.Abs(pose.Stride)<=1);
            }
            Assert.AreEqual("happy",motion.Sample(12).Expression);
            motion.Sample(16.1);Assert.IsFalse(motion.Reacting);
            motion.Play(action,20);motion.Play(PetAction.Rub,20.1);Assert.IsTrue(motion.Sample(20.3).Blink);
            motion.Reset();Assert.IsFalse(motion.Reacting);
        }
    }
    [TestMethod,TestCategory("Motion")]
    public void SleepAndPreferenceCompatibility()
    {
        var motion=new PetMotion{State="sleep"};var pose=motion.Sample(10);
        Assert.IsTrue(pose.Blink&&pose.Expression=="rest"&&pose.ScaleY<1);
        var preferences=System.Text.Json.JsonSerializer.Deserialize<Preferences>("{}")!;Assert.AreEqual("idle",preferences.PetIdleMode);
        foreach(string mode in new[]{"idle","sleep","walk","run"})(preferences with{PetIdleMode=mode}).Validate();
        Assert.ThrowsExactly<InvalidDataException>(()=>(preferences with{PetIdleMode="unknown"}).Validate());
    }
    [TestMethod,TestCategory("Motion")]
    public void RandomMovementRespectsWorkAreaAndPause()
    {
        var wander=new DesktopWander(new Random(42));wander.Reset(new(-400,200),-500,300,50,500);bool moved=false;
        for(int frame=0;frame<10000;frame++)
        {
            var pose=wander.Advance(1d/30,frame>5000,1.5);moved|=pose is not null;
            Assert.IsTrue(wander.Position.X>=-500&&wander.Position.X<=300&&wander.Position.Z>=50&&wander.Position.Z<=500);
        }
        Assert.IsTrue(moved);wander.Pause();var stopped=wander.Position;wander.Advance(.1,false);Assert.AreEqual(stopped,wander.Position);
        wander.Reset(new(999,999),0,0,0,0);for(int frame=0;frame<100;frame++)wander.Advance(.1,true);Assert.AreEqual(new GroundPoint(0,0),wander.Position);
    }
}
