using System.Text;
using System.Text.Json;
using System.IO.Compression;
using Forme.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Forme.Tests;

[TestClass]
public sealed class ProjectWorkTests
{
    private static ProjectSource Source(string text="风险是交期未确认。\n验收需要记录。")=>ProjectSource.FromAttachment(AiAttachment.FromBytes("资料.txt",Encoding.UTF8.GetBytes(text)));
    private static ProjectRun Run(ProjectSource source)=>new(Guid.NewGuid().ToString("N"),"整理风险",1,1,ProjectRunState.Ready,[source.Reference],null,0,null,DateTimeOffset.Now,DateTimeOffset.Now);
    private static string Reply(ProjectSource source)=>JsonSerializer.Serialize(new{capability="notes.draft",arguments=new{title="风险草稿",summary="交期需要确认。",citations=new[]{new{sourceId=source.Reference.Id,paragraph=1,quote="交期未确认"}},tasks=new[]{new{title="确认交期",description="向负责人确认"}},questions=Array.Empty<string>()}},new JsonSerializerOptions{Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping});
    [TestMethod]
    public void DocxSourcesUseExtractedParagraphsRatherThanInventedPages()
    {
        using var bytes=new MemoryStream();
        using(var zip=new ZipArchive(bytes,ZipArchiveMode.Create,true))
        using(var writer=new StreamWriter(zip.CreateEntry("word/document.xml").Open(),Encoding.UTF8))
            writer.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>段落一</w:t></w:r></w:p><w:p><w:r><w:t>段落二</w:t></w:r></w:p></w:body></w:document>");
        var source=ProjectSource.FromAttachment(AiAttachment.FromBytes("资料.docx",bytes.ToArray()));
        Assert.AreEqual("docx-body-v1",source.Reference.Extractor);CollectionAssert.AreEqual(new[]{"段落一","段落二"},source.Paragraphs.ToArray());
    }
    [TestMethod]
    public void SnapshotsHaveStableNormalizedParagraphsAndNoOriginalPaths()
    {
        var a=Source("第一段\r\n\r\n第二段");var b=Source("第一段\n第二段");Assert.AreEqual(a.Reference,b.Reference);
        Assert.AreEqual(2,a.Paragraphs.Count);Assert.AreNotEqual(a.Reference.Hash,Source("第一段\n改变").Reference.Hash);
        Assert.ThrowsExactly<InvalidDataException>(()=>ProjectSource.FromAttachment(AiAttachment.FromBytes("图.png",new byte[]{137,80,78,71,13,10,26,10})));
    }
    [TestMethod]
    public void OnlyDeclaredCapabilitiesAndExactSourceQuotesAreAccepted()
    {
        var source=Source();var valid=Reply(source);var decision=ProjectWork.Parse(valid,[source],1);
        StringAssert.Contains(ProjectWork.Render(decision.Draft!,[source.Reference]),"未创建正式待办或提醒");
        foreach(var invalid in new[]{valid.Replace("交期未确认\"","不存在的引用\"",StringComparison.Ordinal),valid.Replace("notes.draft","computer.run",StringComparison.Ordinal),valid.Replace("\"paragraph\":1","\"paragraph\":999",StringComparison.Ordinal),valid+" extra"})
            Assert.ThrowsExactly<InvalidDataException>(()=>ProjectWork.Parse(invalid,[source],1));
        Assert.ThrowsExactly<InvalidDataException>(()=>ProjectWork.Parse(valid,[source],3));
        var ask=ProjectWork.Parse("{\"capability\":\"user.ask\",\"arguments\":{\"questions\":[\"需要哪种摘要？\"]}}",[source],1);Assert.IsNull(ask.Draft);
    }
    [TestMethod]
    public void ContextIsScopedAndBudgetIsCheckedWithoutSilentTrimming()
    {
        var source=Source();var run=Run(source);var turns=ProjectWork.BuildTurns(new Preferences(),run,[source],null);
        Assert.IsTrue(turns.Sum(AiClient.Estimate)<=4096);
        var prior=ProjectWork.Render(ProjectWork.Parse(Reply(source),[source],1).Draft!,[source.Reference]);
        Assert.IsTrue(ProjectWork.BuildTurns(new Preferences(),run,[source],prior).Sum(AiClient.Estimate)<=4096);
        StringAssert.Contains(turns[1].Content,source.Reference.Id);Assert.IsFalse(turns.Any(x=>x.Content.Contains("API Key",StringComparison.Ordinal)));
        var changed=Source("别的资料");Assert.ThrowsExactly<InvalidDataException>(()=>ProjectWork.BuildTurns(new(),run,[changed],null));
        var large=Source(new string('中',4000));Assert.ThrowsExactly<InvalidDataException>(()=>ProjectWork.BuildTurns(new(),Run(large),[large],null));
    }
    [TestMethod]
    public void JournalRejectsStaleResultsAndCommitsDraftExactlyOnceWithoutTasks()
    {
        WithStore(store=>
        {
            var source=Source();var run=Run(source);store.SaveProject(run);var old=store.BeginProjectAttempt(run.Id,1,DateTimeOffset.Now,"https://project.invalid/v1","test-model");
            Assert.AreEqual("https://project.invalid/v1",store.Project(run.Id)!.Endpoint);
            store.SaveProject(old with{Revision=2,Goal="只列风险",State=ProjectRunState.Ready});
            var decision=ProjectWork.Parse(Reply(source),[source],1);
            Assert.IsFalse(store.FinishProjectAttempt(old,decision,ProjectRunState.Failed,DateTimeOffset.Now));Assert.AreEqual(0,store.UserNotes().Count);
            var next=store.BeginProjectAttempt(run.Id,2,DateTimeOffset.Now);
            Assert.IsTrue(store.FinishProjectAttempt(next,decision,ProjectRunState.Failed,DateTimeOffset.Now));
            Assert.IsFalse(store.FinishProjectAttempt(next,decision,ProjectRunState.Failed,DateTimeOffset.Now));
            Assert.AreEqual(1,store.UserNotes().Count);Assert.AreEqual(0,store.UserTasks().Count);Assert.AreEqual(0,store.DueReminders(DateTimeOffset.MaxValue).Count);
            Assert.ThrowsExactly<InvalidDataException>(()=>store.BeginProjectAttempt(run.Id,2,DateTimeOffset.Now));
            var journal=JsonSerializer.Serialize(store.Project(run.Id),new JsonSerializerOptions{Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping});Assert.IsFalse(journal.Contains(source.Paragraphs[0],StringComparison.Ordinal));
            store.RecoverProjects();Assert.AreEqual(ProjectRunState.WaitingForSources,store.Project(run.Id)!.State);Assert.AreEqual(1,store.UserNotes().Count);
        });
    }
    [TestMethod]
    public void ExpiredOrClosedRunCannotWriteAndDeletionKeepsChosenArtifact()
    {
        WithStore(store=>
        {
            var source=Source();var run=Run(source);store.SaveProject(run);var issued=store.BeginProjectAttempt(run.Id,1,DateTimeOffset.Now);
            store.FinishProjectAttempt(issued,ProjectWork.Parse(Reply(source),[source],1),ProjectRunState.Failed,issued.ExpiresAt!.Value.AddSeconds(1));
            Assert.AreEqual(0,store.UserNotes().Count);Assert.AreEqual(ProjectRunState.Cancelled,store.Project(run.Id)!.State);
            Assert.ThrowsExactly<InvalidDataException>(()=>store.BeginProjectAttempt(run.Id,1,DateTimeOffset.Now));
        });
    }
    [TestMethod]
    public void RevisionCreatesNewArtifactAndDoesNotOverwriteManualEdits()
    {
        WithStore(store=>
        {
            var source=Source();var run=Run(source);store.SaveProject(run);var issued=store.BeginProjectAttempt(run.Id,1,DateTimeOffset.Now);
            var decision=ProjectWork.Parse(Reply(source),[source],1);store.FinishProjectAttempt(issued,decision,ProjectRunState.Failed,DateTimeOffset.Now);
            var original=store.ProjectArtifact(store.Project(run.Id)!)!;store.SaveUserNote(original with{Content="用户手工修改的草稿"});
            store.SaveProject(store.Project(run.Id)! with{Revision=2,Goal="只列风险",State=ProjectRunState.Ready});
            var second=store.BeginProjectAttempt(run.Id,2,DateTimeOffset.Now);store.FinishProjectAttempt(second,decision,ProjectRunState.Failed,DateTimeOffset.Now);
            Assert.AreEqual(2,store.UserNotes().Count);Assert.AreNotEqual(original.Id,store.Project(run.Id)!.ArtifactId);
            Assert.AreEqual("用户手工修改的草稿",store.UserNotes().Single(x=>x.Id==original.Id).Content);
            store.DeleteProject(run.Id);Assert.AreEqual(2,store.UserNotes().Count);Assert.IsNull(store.Project(run.Id));
        });
    }
    private static void WithStore(Action<Store> test)
    {
        string root=Path.Combine(Path.GetTempPath(),"forme-project-"+Guid.NewGuid().ToString("N"));
        try{using var store=new Store(root);test(store);}finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
