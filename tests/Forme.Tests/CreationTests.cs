using Forme.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace Forme.Tests;

[TestClass]
public sealed class CreationTests
{
    [TestMethod]
    public void NaturalMovementKeepsPermissionsAndDoesNotRequireCoordinates()
    {
        var p=new Preferences{AllowPetControl=true};
        foreach(var c in new CompanionCommand[]{new("stroll","walk"),new("stroll","run"),new("go","book"),new("go","window")})
        {Assert.IsTrue(CompanionCommands.Allowed(c,p));Assert.IsFalse(CompanionCommands.Allowed(c,new()));}
        Assert.IsFalse(CompanionCommands.Allowed(new("go","shell"),p));Assert.IsFalse(CompanionCommands.Allowed(new("stroll","walk",1,1),p));
        Assert.IsTrue(CompanionCommands.Instructions(p,null).Contains("独立意图识别"));
    }
    [TestMethod]
    public void NearbyGoalAvoidsObstaclesAndLeavesCurrentRouteUntouched()
    {
        var travel=new PetTravel(-4,4,-4,4,[new(0,1.1,1,1)]);travel.Reset(new(0,0));
        var target=travel.NearbyTarget(1.1);Assert.IsNotNull(target);Assert.IsTrue(travel.CanReach(target.Value));Assert.AreEqual(new GroundPoint(0,0),travel.Position);Assert.IsFalse(travel.Moving);
        Assert.IsTrue(travel.MoveTo(target.Value));travel.SetGait(true);Assert.IsTrue(travel.Running);travel.SetGait(false);Assert.IsFalse(travel.Running);
        var trapped=new PetTravel(-.4,.4,-.4,.4,[]);trapped.Reset(new(0,0));Assert.IsNull(trapped.NearbyTarget(1.1));
    }
    [TestMethod]
    public void ExistingSceneGrantDoesNotGrantNewPlayOrLayoutActions()
    {
        var old=new Preferences{AllowSceneControl=true};
        foreach(var c in new CompanionCommand[]{new("life","reading"),new("layout","reading"),new("boat","start"),new("hide","pet-hides"),new("furniture","book",0,0,90)})Assert.IsFalse(CompanionCommands.Allowed(c,old));
        var p=old with{AllowPlayControl=true,AllowLayoutPreview=true};
        foreach(var c in new CompanionCommand[]{new("life","reading"),new("layout","reading"),new("boat","resume"),new("hide","pet-hides"),new("furniture","book",0,0,90),new("boat-leaf","2",1,1,-30),new("boat-name","听雨的小旅行")})Assert.IsTrue(CompanionCommands.Allowed(c,p));
        foreach(var c in new CompanionCommand[]{new("layout","save"),new("life","unknown"),new("furniture","book",0,0,45),new("boat-leaf","4",0,0,0),new("boat-leaf","1",0,0,71),new("boat-leaf","1",0,2,0),new("boat-name","a\nb"),new("dance","spin",Rotation:90),new("throw",X:double.NaN,Z:0)})Assert.IsFalse(CompanionCommands.Allowed(c,p));
    }
    [TestMethod]
    public void NewProtocolRejectsDuplicatesExtraKeysAndMoreThanThreeSteps()
    {
        var p=new Preferences{AllowLayoutPreview=true,AllowPlayControl=true};
        string wrap(string s)=>"预览。```forme-actions\n"+s+"```";
        Assert.AreEqual(1,CompanionCommands.Parse(wrap("[{\"action\":\"furniture\",\"value\":\"book\",\"x\":0,\"z\":0,\"rotation\":90}]"),p).Commands.Count);
        foreach(string s in new[]{"[{\"action\":\"boat\",\"action\":\"layout\",\"value\":\"reading\"}]","[{\"action\":\"layout\",\"value\":\"reading\",\"save\":true}]","[{\"action\":\"boat\",\"value\":\"open\"},{\"action\":\"boat\",\"value\":\"open\"},{\"action\":\"boat\",\"value\":\"open\"},{\"action\":\"boat\",\"value\":\"open\"}]"})Assert.AreEqual(0,CompanionCommands.Parse(wrap(s),p).Commands.Count);
        Assert.IsFalse(CompanionCommands.HasControl(new()));Assert.IsTrue(CompanionCommands.HasControl(p));
    }
    [TestMethod]
    public void ControlInstructionsFitStandardBudget()
    {
        var p=new Preferences{AllowPetControl=true,AllowSceneControl=true,AllowPlayControl=true,AllowLayoutPreview=true};
        var turns=AiClient.BuildContext(p,[],"布置阅读角",out _,scene:"小屋；可用事件reading");
        Assert.IsTrue(turns.Sum(AiClient.Estimate)<=p.ContextBudget);
        turns=AiClient.BuildContext(p,[],"布置阅读角",out bool trimmed,scene:new string('x',6000));
        Assert.IsFalse(trimmed);Assert.IsTrue(turns.Sum(AiClient.Estimate)<=p.ContextBudget);Assert.IsFalse(turns[0].Content.Contains(new string('x',100)));
        Assert.IsTrue(AiIntentProtocol.BuildContext(p,"去池塘边").Sum(AiClient.Estimate)<=p.ContextBudget);
    }
}
