using Forme.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Forme.Tests;
[TestClass]
public sealed class IntentTests
{
    [TestMethod]
    public void ReviewPondAndBookVariantsResolveWithoutPhraseExpansion()
    {
        foreach(var (target,phrases) in new[] {
            ("pond",new[]{"去池塘边","去池塘旁边","到水边","走去木桥那里","过去池子那边","你去水边待会","团团你去水边溜达一下吧"}),
            ("book",new[]{"去书架旁","走到书柜那里","过去看看书架","到看书的地方","去书柜那边待一会"})})
            foreach(string phrase in phrases)
            {
                var result=IntentResolver.Resolve(phrase,ActionRules.Default(),"团团");
                Assert.AreEqual(1,result.Intents.Count,phrase);
                Assert.AreEqual(CompanionIntentType.GoTo,result.Intents[0].Type,phrase);
                Assert.AreEqual(target,result.Intents[0].Target,phrase);
            }
        foreach(string phrase in new[]{"出去走走","溜达一下","转悠会儿","活动活动","在附近逛一下","出去活动活动腿脚"})
        {var result=IntentResolver.Resolve(phrase);Assert.AreEqual(CompanionIntentType.Stroll,result.Intents.Single().Type,phrase);Assert.AreEqual("walk",result.Intents.Single().Mode);}
    }
    [TestMethod]
    public void NegationDiscussionQuotesAndUnrelatedSentencesNeverExecute()
    {
        foreach(string phrase in new[]{"别去池塘","不要散步","不用过去","不能去池塘","我不想让你去池塘","“去池塘边”支持吗？","刚才为什么没去池塘？","如果让你散步会怎样？","解释一下“绕屏幕跑一圈”","我今天在附近逛一下心情很好","池塘很好看","去书架和池塘","先去池塘然后聊天"})
            Assert.AreEqual(0,IntentResolver.Resolve(phrase,ActionRules.Default()).Intents.Count,phrase);
        Assert.AreEqual(CompanionIntentType.Stop,IntentResolver.Resolve("别动").Intents.Single().Type);
        Assert.AreEqual(CompanionIntentType.Stop,IntentResolver.Resolve("不要动").Intents.Single().Type);
        Assert.AreEqual(1,IntentResolver.Resolve("你能不能走两步吗？",ActionRules.Default()).Intents.Count);
    }
    [TestMethod]
    public void PlansSwitchScenesOnceAndRespectStepBudget()
    {
        var plan=ActionPlanner.Build(IntentResolver.Resolve("先去户外，再到池塘边").Intents,SceneKind.Indoor);
        CollectionAssert.AreEqual(new[]{"scene","go"},plan.Steps.Select(x=>x.Action).ToArray());
        plan=ActionPlanner.Build(IntentResolver.Resolve("去池塘边然后看看水").Intents,SceneKind.Indoor);
        CollectionAssert.AreEqual(new[]{"scene","go","interact"},plan.Steps.Select(x=>x.Action).ToArray());
        Assert.AreEqual(1,ActionPlanner.Build([new(CompanionIntentType.GoTo,"pond")],SceneKind.Outdoor).Steps.Count);
        Assert.AreEqual(2,ActionPlanner.Build(IntentResolver.Resolve("出去走一圈再回来").Intents,SceneKind.Desktop).Steps.Count);
        Assert.AreEqual("run",ActionPlanner.Build([ActionPlanner.FromCommand(new("go","pond",Motion:"run"))],SceneKind.Outdoor).Steps.Single().Motion);
        Assert.ThrowsExactly<ActionFailureException>(()=>ActionPlanner.Build([new(CompanionIntentType.GoTo,"pond"),new(CompanionIntentType.GoTo,"book")],SceneKind.Indoor));
    }
    [TestMethod]
    public async Task MissingPermissionsAndMissingTargetPreventEverySideEffect()
    {
        var plan=ActionPlanner.Build([new(CompanionIntentType.GoTo,"pond")],SceneKind.Indoor);
        foreach(var preferences in new[]{new Preferences{AllowPetControl=true},new Preferences{AllowSceneControl=true},new Preferences()})
        {
            int count=0;var result=await ActionExecutor.Execute(plan,()=>preferences,null,c=>{count++;return Started(c);},(_,_)=>Task.FromResult(true),()=>{},CancellationToken.None);
            Assert.AreEqual(0,count);Assert.AreEqual(ActionResultCode.Unauthorized,result.Single().Code);
        }
        var world=LivingWorld.Default();world.Items.RemoveAll(x=>x.Kind=="book");
        plan=ActionPlanner.Build([new(CompanionIntentType.GoTo,"book")],SceneKind.Outdoor);
        Assert.AreEqual(ActionResultCode.TargetUnavailable,TargetNavigation.Preflight(plan,world,SceneKind.Outdoor,TargetNavigation.Start)!.Code);
        foreach(var target in TargetCatalog.All.Where(x=>x.Capabilities.HasFlag(TargetCapabilities.GoTo)))
        {
            var targetPlan=ActionPlanner.Build([new(CompanionIntentType.GoTo,target.Id)],SceneKind.Indoor);
            Assert.IsNull(TargetNavigation.Preflight(targetPlan,LivingWorld.Default(),SceneKind.Indoor,TargetNavigation.Start),target.Id);
        }
    }
    [TestMethod]
    public async Task CompletionInterruptionTimeoutAndCancellationAreTypedAndSequential()
    {
        var plan=new ActionPlan([new("dance","sway"),new("dance","hop")]);var prefs=new Preferences{AllowPetControl=true};int started=0;bool waiting=false;
        ActionResult start(CompanionCommand c){Assert.IsFalse(waiting);started++;return Started(c);}
        async Task<bool> wait(CompanionCommand c,CancellationToken token){waiting=true;await Task.Delay(5,token);waiting=false;return true;}
        var results=await ActionExecutor.Execute(plan,()=>prefs,null,start,wait,()=>{},CancellationToken.None);
        Assert.AreEqual(2,started);Assert.IsTrue(results.All(x=>x.Code==ActionResultCode.Completed));
        results=await ActionExecutor.Execute(plan,()=>prefs,null,Started,(_,_)=>Task.FromResult(false),()=>{},CancellationToken.None);
        Assert.AreEqual(ActionResultCode.Interrupted,results.Single().Code);
        results=await ActionExecutor.Execute(plan,()=>prefs,null,Started,async(_,token)=>{await Task.Delay(1000,token);return true;},()=>{},CancellationToken.None,timeout:TimeSpan.FromMilliseconds(10));
        Assert.AreEqual(ActionResultCode.Timeout,results.Single().Code);
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();started=0;
        results=await ActionExecutor.Execute(plan,()=>prefs,null,start,wait,()=>{},cancelled.Token);
        Assert.AreEqual(0,started);Assert.AreEqual(ActionResultCode.Cancelled,results.Single().Code);
        results=await ActionExecutor.Execute(plan,()=>prefs,null,_=>throw new ActionFailureException(ActionResultCode.PathBlocked,"路被挡住了"),wait,()=>{},CancellationToken.None);
        Assert.AreEqual(ActionResultCode.PathBlocked,results.Single().Code);
    }
    [TestMethod]
    public void AiProtocolRejectsCoordinatesPartialAndMixedInvalidPlans()
    {
        Assert.AreEqual("pond",AiIntentProtocol.Parse("[{\"type\":\"GoTo\",\"target\":\"pond\"}]").Single().Target);
        foreach(string text in new[]{"我已经到池塘了","[{\"type\":\"GoTo\",\"target\":\"pond\",\"x\":1}]","[{\"type\":\"GoTo\",\"target\":\"unknown\"}]","[{\"type\":\"GoTo\",\"type\":\"Stop\"}]","[{\"type\":\"GoTo\",\"target\":\"pond\"},{\"type\":\"shell\"}]","[{\"type\":\"Stroll\",\"count\":3}]","[{\"type\":\"Shortcut\"}]","[{\"type\":\"999\"}]"})
            Assert.ThrowsExactly<ActionFailureException>(()=>AiIntentProtocol.Parse(text),text);
        var turns=AiIntentProtocol.BuildContext(new(),"去有水的地方待会吧");Assert.AreEqual(2,turns.Count);Assert.AreEqual("去有水的地方待会吧",turns[1].Content);
        Assert.IsFalse(turns[0].Content.Contains("坐标X="));
        Assert.IsFalse(ConversationText.Visible("我已经到了池塘边。").Contains("我已经"));
        Assert.IsFalse(ConversationText.Visible("我来执行。```forme-actions\n[]```").Contains("我来"));
        Assert.AreEqual("你好，我们聊聊吧。",ConversationText.Visible("你好，我们聊聊吧。"));
    }
    [TestMethod]
    public void CreationIntentsRetainCapabilitiesWithoutInventingCoordinates()
    {
        Assert.AreEqual(CompanionIntentType.Touch,AiIntentProtocol.Parse("[{\"type\":\"Touch\",\"mode\":\"pat\"}]").Single().Type);
        Assert.AreEqual(CompanionIntentType.LayoutPreview,AiIntentProtocol.Parse("[{\"type\":\"LayoutPreview\",\"mode\":\"reading\"}]").Single().Type);
        Assert.AreEqual("boat-color",ActionPlanner.Build(AiIntentProtocol.Parse("[{\"type\":\"BoatConfiguration\",\"target\":\"color\",\"mode\":\"rose\"}]"),SceneKind.Indoor).Steps.Single().Action);
        const string furniture="[{\"type\":\"LayoutPreview\",\"target\":\"book\",\"mode\":\"furniture\",\"x\":0,\"z\":2.5,\"rotation\":90}]";
        Assert.ThrowsExactly<ActionFailureException>(()=>AiIntentProtocol.Parse(furniture,"把书架挪过去"));
        Assert.ThrowsExactly<ActionFailureException>(()=>AiIntentProtocol.Parse(furniture,"书架位置0,-2.5旋转90"));
        Assert.ThrowsExactly<ActionFailureException>(()=>AiIntentProtocol.Parse(furniture,"书架位置0,2.5e2旋转90"));
        Assert.AreEqual("furniture",ActionPlanner.Build(AiIntentProtocol.Parse(furniture,"书架位置0,2.5旋转90"),SceneKind.Indoor).Steps.Single().Action);
        Assert.ThrowsExactly<ActionFailureException>(()=>AiIntentProtocol.Parse("[{\"type\":\"NameBoat\",\"target\":\"猜出的名字\"}]","为船命名"));
        Assert.AreEqual(CompanionIntentType.NameBoat,AiIntentProtocol.Parse("[{\"type\":\"NameBoat\",\"target\":\"听雨\"}]","给船命名听雨").Single().Type);
    }
    [TestMethod]
    public void PathPreflightAndGaitDoNotMutateTheWorld()
    {
        var world=LivingWorld.Default();var before=world.Copy();
        var plan=ActionPlanner.Build(IntentResolver.Resolve("跑到池塘边").Intents,SceneKind.Indoor);
        Assert.AreEqual("run",plan.Steps.Last().Motion);Assert.IsNull(TargetNavigation.Preflight(plan,world,SceneKind.Indoor,TargetNavigation.Start));CollectionAssert.AreEqual(before.Items,world.Items);
        world.Items.AddRange(Enumerable.Range(0,14).Select(i=>new FurnitureItem("barrier"+i,"lamp",-4+i*.6,.9,0)));
        plan=ActionPlanner.Build([new(CompanionIntentType.GoTo,"book")],SceneKind.Indoor);
        Assert.AreEqual(ActionResultCode.PathBlocked,TargetNavigation.Preflight(plan,world,SceneKind.Indoor,TargetNavigation.Start)!.Code);
        Assert.AreEqual(0,IntentResolver.Resolve("池塘").Intents.Count);
    }
    private static ActionResult Started(CompanionCommand command)=>new(ActionResultCode.Started,command.Action,command.Value,"正在执行",TimeSpan.Zero);
}
