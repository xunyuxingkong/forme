using Forme.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

[TestClass]
public sealed class CompanionTests
{
    private readonly string _directory=Path.GetFullPath(Path.Combine("artifacts","companion-tests",Guid.NewGuid().ToString("N")));
    [TestCleanup]
    public void Cleanup()
    {
        string root=Path.GetFullPath(Path.Combine("artifacts","companion-tests"))+Path.DirectorySeparatorChar;
        Assert.IsTrue(_directory.StartsWith(root,StringComparison.OrdinalIgnoreCase));
        if(Directory.Exists(_directory))Directory.Delete(_directory,true);
    }
    private static ChatMemory Memory(string id,string provider,string content,bool enabled=true)=>new(id,"记忆"+id,content,provider,enabled,DateTimeOffset.Now,DateTimeOffset.Now);
    [TestMethod,TestCategory("Animation")]
    public void SceneActivitiesUseFiniteModelIndependentActions()
    {
        foreach(var (action,expression) in new[]{(PetAction.Eat,"happy"),(PetAction.Read,"focus"),(PetAction.Rest,"rest"),(PetAction.Look,"thinking")})
        {
            var motion=new PetMotion();motion.Play(action,0);Assert.AreEqual(expression,motion.Sample(.8).Expression);Assert.IsTrue(motion.Reacting);
            Assert.AreEqual("idle",motion.Sample(12).Expression);Assert.IsFalse(motion.Reacting);
        }
    }
    [TestMethod,TestCategory("Privacy")]
    public void MemoryRequiresOptInAndStaysWithItsProvider()
    {
        var records=new[]{Memory("a","deepseek","我喜欢红茶"),Memory("b","siliconflow","不应泄露的记忆"),Memory("c","deepseek","已停用记忆",false)};
        var p=new Preferences();var turns=AiClient.BuildContext(p,[],"你好",out _,records);
        Assert.IsFalse(turns[0].Content.Contains("红茶"));
        turns=AiClient.BuildContext(p with{MemoryEnabled=true},[],"你好",out _,records,new("session","跨服务摘要","siliconflow"));
        Assert.IsTrue(turns[0].Content.Contains("红茶")||turns[0].Content.Contains("\\u7EA2"));
        Assert.IsFalse(turns[0].Content.Contains("b\""));Assert.IsFalse(turns[0].Content.Contains("c\""));
        Assert.IsFalse(turns[0].Content.Contains("跨服务摘要"));Assert.IsTrue(turns.Sum(AiClient.Estimate)<=p.ContextBudget);
        var many=Enumerable.Range(0,30).Select(i=>Memory(i.ToString(),"deepseek",new string('a',350))).ToArray();
        turns=AiClient.BuildContext(p with{MemoryEnabled=true,ContextBudget=2048},[],"hi",out bool trimmed,many);
        Assert.IsTrue(trimmed);Assert.IsTrue(turns.Sum(AiClient.Estimate)<=2048);
    }
    [TestMethod,TestCategory("Storage")]
    public void AtomicTurnsSearchMemoryAndRoomBackupRoundTrip()
    {
        using var store=new Store(_directory);var session=store.NewSession("https://api.deepseek.com");var now=DateTimeOffset.Now;
        var user=new ChatMessage("u",session.Id,"user","literal 100%_text","complete",now);
        var response=new ChatMessage("a",session.Id,"assistant","你好","streaming",now.AddTicks(1));
        store.BeginChatTurn(user,response);Assert.AreEqual(2,store.Messages(session.Id).Count);
        store.SaveMessage(response with{Content="保存完成",Status="complete"});
        Assert.AreEqual(1,store.SearchMessages(session.Id,"100%_").Count);Assert.AreEqual(0,store.SearchMessages(session.Id,"999%_").Count);
        store.SaveMemory(Memory("one","deepseek","喜欢低饱和度场景"));store.SaveNote(new(session.Id,"我们正在讨论小屋布置","deepseek"));
        store.SavePreferences(new(){PetModel="fox",Weather="snow",Fireplace=true,RoomLamp=false});
        string path=Path.Combine(_directory,"backup.json");store.ExportFile(path,true,false,false,true);
        using var plan=Store.PrepareImport(path);using var target=new Store(Path.Combine(_directory,"restored"));target.SavePreferences(new(){MemoryEnabled=true});target.ApplyImport(plan);
        Assert.AreEqual("fox",target.LoadPreferences().PetModel);Assert.AreEqual("snow",target.LoadPreferences().Weather);Assert.IsTrue(target.LoadPreferences().Fireplace);Assert.IsFalse(target.LoadPreferences().RoomLamp);
        Assert.AreEqual("喜欢低饱和度场景",target.Memories().Single().Content);Assert.AreEqual("我们正在讨论小屋布置",target.Note(session.Id)!.Content);
        Assert.IsFalse(target.LoadPreferences().MemoryEnabled,"Restore must not silently opt a new installation into sharing memories");
        target.DeleteSession(session.Id);Assert.IsNull(target.Note(session.Id));Assert.AreEqual(1,target.Memories().Count);
        target.ClearCategory("chat");Assert.AreEqual(0,target.Memories().Count);
        var badUser=user with{Id="rollback"};Assert.ThrowsExactly<Microsoft.Data.Sqlite.SqliteException>(()=>store.BeginChatTurn(badUser,response));
        Assert.IsFalse(store.Messages(session.Id).Any(x=>x.Id=="rollback"));
    }
    [TestMethod,TestCategory("Privacy")]
    public void WhitelistedCommandsRejectExecutableAndUnauthorizedIntent()
    {
        var allowed=new Preferences{AllowPetControl=true,AllowSceneControl=true};
        string text="跳一支舞吧。\n```forme-actions\n[{\"action\":\"dance\",\"value\":\"spin\"},{\"action\":\"scene\",\"value\":\"outdoor\"}]\n```";
        var parsed=CompanionCommands.Parse(text,allowed);Assert.AreEqual(2,parsed.Commands.Count);Assert.AreEqual("跳一支舞吧。",parsed.Text);
        Assert.AreEqual(0,CompanionCommands.Parse(text,new()).Commands.Count);
        foreach(var command in new[]{new CompanionCommand("shell","powershell"),new("weather","download"),new("move",X:double.NaN,Z:0),new("move",X:11,Z:0),new("dance","spin",X:1),new("delete","all")})Assert.IsFalse(CompanionCommands.Allowed(command,allowed));
        string bad="你好```forme-actions\n[{\"action\":\"dance\",\"value\":\"sway\",\"script\":\"bad\"}]```";
        Assert.AreEqual(0,CompanionCommands.Parse(bad,allowed).Commands.Count);
        Assert.AreEqual("你好",CompanionCommands.VisibleText("你好```forme-actions\n[{\"action\":"));
        Assert.AreEqual("你好",CompanionCommands.ContextText("你好\n\n[Forme 本地动作] 动作已执行"));
    }
    [TestMethod,TestCategory("Migration")]
    public void LegacyBackupRemainsReadableAndNewMemoryLimitsAreEnforced()
    {
        using var store=new Store(_directory);store.Import(new ExportDocument{SchemaVersion=2,Sessions=[],Messages=[]});Assert.AreEqual(0,store.Sessions().Count);
        for(int i=0;i<30;i++)store.SaveMemory(Memory(i.ToString(),"deepseek","内容"));
        Assert.ThrowsExactly<InvalidDataException>(()=>store.SaveMemory(Memory("31","deepseek","超额")));
        store.SaveMemory(Memory("1","deepseek","编辑已有记忆"));Assert.AreEqual(30,store.Memories().Count);
        Assert.ThrowsExactly<InvalidDataException>(()=>store.SaveMemory(Memory("bad","unbound","内容")));
        var document=store.Export(true,false,false,false);Assert.AreEqual(Store.ExportVersion,document.SchemaVersion);Store.ValidateExport(document);
    }
}
