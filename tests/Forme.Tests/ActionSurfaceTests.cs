using Forme.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Forme.Tests;

[TestClass]
public sealed class ActionSurfaceTests
{
    [TestMethod]
    public void ModelSelectedDesktopRemainsDesktopWithRoomContext()
    {
        var intents=AiIntentProtocol.Parse("[{\"type\":\"Stroll\",\"mode\":\"walk\",\"view\":\"desktop\"}]","在桌面走几步");
        var plan=ActionPlanner.Build(intents,SceneKind.Indoor);
        Assert.AreEqual(CompanionView.Desktop,plan.Steps.Single().View);
        Assert.IsNull(ActionPlanner.Preflight(plan,new Preferences{AllowPetControl=true}));
        var room=ActionPlanner.Build(AiIntentProtocol.Parse("[{\"type\":\"Stroll\",\"view\":\"room\"}]"),SceneKind.Desktop);
        Assert.AreEqual(CompanionView.Room,room.Steps.Single().View);
    }
    [TestMethod]
    public void SceneSwitchBindsOnlyFollowingBodyAndDesktopIdleIsIndependent()
    {
        var plan=ActionPlanner.Build([new(CompanionIntentType.Stroll),new(CompanionIntentType.ChangeScene,"indoor"),new(CompanionIntentType.Dance)],SceneKind.Desktop);
        CollectionAssert.AreEqual(new[]{CompanionView.Desktop,CompanionView.Room,CompanionView.Room},plan.Steps.Select(x=>x.View!.Value).ToArray());
        var idle=ActionPlanner.Build([new(CompanionIntentType.Idle,Mode:"walk")],SceneKind.Indoor);
        Assert.AreEqual(CompanionView.Desktop,idle.Steps.Single().View);
        Assert.IsFalse(ActionRules.Default().Serialize().Contains("\"view\"",StringComparison.Ordinal),"Default rule files retain the old schema when no explicit surface is configured");
    }
    [TestMethod]
    public void InvalidSurfacesAndUnsupportedDesktopCapabilitiesNeverExecute()
    {
        foreach(var json in new[]{"[{\"type\":\"Stroll\",\"view\":\"screen2\"}]","[{\"type\":\"Stroll\",\"count\":2,\"view\":\"room\"}]","[{\"type\":\"GoTo\",\"target\":\"book\",\"view\":\"desktop\"}]","[{\"type\":\"Idle\",\"mode\":\"walk\",\"view\":\"room\"}]"})
            Assert.ThrowsExactly<ActionFailureException>(()=>AiIntentProtocol.Parse(json));
        Assert.IsFalse(CompanionCommands.Allowed(new("stroll","walk",View:(CompanionView)99),new Preferences{AllowPetControl=true}));
        var stop=ActionPlanner.Build([new(CompanionIntentType.Stop)],SceneKind.Indoor);
        Assert.IsNull(stop.Steps.Single().View,"Emergency stop remains global even when the room is selected");
    }
    [TestMethod]
    public void VisibleReminderDoesNotBlockNextScheduledReminder()
    {
        string path=Path.Combine(Path.GetTempPath(),"forme-reminders-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var store=new Store(path);var now=DateTimeOffset.Now;
            store.SaveUserReminder(new("active","已经显示",now.AddMinutes(-1),null,"pending","",now,now,null));
            store.SaveUserReminder(new("next","下一条",now.AddSeconds(5),null,"pending","",now,now,null));
            Assert.AreEqual(now.AddSeconds(5),store.NextReminderTime(new[]{"active"}));
            Assert.IsNull(store.NextReminderTime(new[]{"active","next"}));
            store.SnoozeUserReminder("active",now.AddHours(1),now);
            Assert.AreEqual(now.AddSeconds(5),store.NextReminderTime());
        }
        finally{if(Directory.Exists(path))Directory.Delete(path,true);}
    }
}
