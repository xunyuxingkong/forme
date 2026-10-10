using Forme.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace Forme.Tests;
[TestClass]
public sealed class ActionRuleTests
{
    [TestMethod]
    public void WholePhrasesAndPoliteRequestsMatchButNegationAndQuotationDoNot()
    {
        var rules=ActionRules.Parse(ActionRules.Default().Serialize());
        Assert.AreEqual("stroll",rules.Match("你能走两步吗？")![0].Action);
        Assert.AreEqual("sleep",rules.Match("请睡觉吧")![0].Value);
        Assert.AreEqual("spin",rules.Match("转一圈看看")![0].Value);
        Assert.AreEqual(2,rules.Match("走两步，然后跳个舞")!.Count);
        foreach(string text in new[]{"不要走两步","如果让你跳舞会怎样","他说你能走两步吗","解释走两步是什么意思","我刚才让你走两步","今天走两步心情好了","跳舞再跳舞再跳舞再跳舞"})Assert.IsNull(rules.Match(text));
        Assert.AreEqual("stop",rules.Match("别动")![0].Action);
        var custom=new ActionRules(1,[new(["先走两步再跳个舞"],[new("stroll","walk"),new("dance","sway")])]);Assert.AreEqual(2,custom.Match("先走两步再跳个舞")!.Count);
    }
    [TestMethod]
    public void EmbodiedIdentityIsIncludedWithAndWithoutActionPermissions()
    {
        foreach(bool allowed in new[]{false,true})
        {
            var settings=new Preferences{AllowPetControl=allowed};
            var turns=AiClient.BuildContext(settings,[],"你有身体吗",out _);
            StringAssert.Contains(turns[0].Content,"宠物模型就是你在这个应用里的身体");
            StringAssert.Contains(turns[0].Content,"不用括号描写");
            StringAssert.Contains(turns[0].Content,"即使历史回复说过没有身体");
            Assert.IsTrue(turns.Sum(AiClient.Estimate)<=settings.ContextBudget);
        }
    }
    [TestMethod]
    public void DefaultUpgradePreservesCustomRules()
    {
        var current=ActionRules.Default();
        var old=current with{Rules=current.Rules.Select(r=>r.Commands[0]==new CompanionCommand("dance","spin")?r with{Phrases=["转圈舞"]}:r).ToArray()};
        Assert.AreEqual("spin",old.UpgradeDefaults().Match("转一圈看看")![0].Value);
        var custom=old with{Rules=old.Rules.Append(new ActionRule(["休息一下"],[new("idle","sleep")])).ToArray()};
        Assert.AreSame(custom,custom.UpgradeDefaults());
    }
    [TestMethod]
    public void EditableRulesRejectExecutableCommandsUnknownFieldsAndDuplicatePhrases()
    {
        Assert.ThrowsExactly<InvalidDataException>(()=>ActionRules.Parse(new ActionRules(1,[new(["测试"],[new("shell","powershell")])]).Serialize()));
        Assert.ThrowsExactly<InvalidDataException>(()=>ActionRules.Parse(new ActionRules(1,[new(["走两步","请走两步"],[new("stroll","walk")])]).Serialize()));
        Assert.ThrowsExactly<InvalidDataException>(()=>ActionRules.Parse("{\"version\":1,\"rules\":[],\"script\":\"x\"}"));
        Assert.ThrowsExactly<InvalidDataException>(()=>ActionRules.Parse(new string('x',32769)));
    }
    [TestMethod]
    public void DesktopSingleStepMovesWithinWorkAreaAndFinishes()
    {
        var wander=new DesktopWander(new Random(1));wander.Reset(new(490,200),0,500,0,300);Assert.IsTrue(wander.BeginStep(80));
        for(int i=0;i<200&&wander.Moving;i++)wander.Advance(.1,false);
        Assert.IsFalse(wander.Moving);Assert.AreEqual(new GroundPoint(410,200),wander.Position);
        Assert.IsTrue(wander.BeginStep(100));wander.FinishStep();Assert.IsFalse(wander.Moving);Assert.IsTrue(wander.Position.X<=500);
    }
    [TestMethod]
    public void LocalTranscriptCanRoundTripWithoutEnteringAiHistory()
    {
        string path=Path.Combine(Path.GetTempPath(),"Forme-rule-check-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var store=new Store(path);var session=store.NewSession("https://api.deepseek.com");
            store.SaveMessage(new("u",session.Id,"user","走两步","local",DateTimeOffset.Now));store.SaveMessage(new("a",session.Id,"assistant","本地动作","local",DateTimeOffset.Now.AddTicks(1)));
            var export=store.Export(true,false,false,false);Store.ValidateExport(export);store.Import(export);
            string backup=Path.Combine(path,"local.json");store.ExportFile(backup,true,false,false,false);using(var prepared=Store.PrepareImport(backup))store.ApplyImport(prepared);
            var turns=AiClient.BuildContext(new(),store.Messages(session.Id),"你好",out _);Assert.AreEqual(2,turns.Count);Assert.IsFalse(turns.Any(t=>t.Content.Contains("走两步")));
            Assert.AreEqual(0,store.ContextMessages(session.Id).Count);
        }
        finally{if(Directory.Exists(path))Directory.Delete(path,true);}
    }
}
