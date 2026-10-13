using Forme.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Forme.Tests;
[TestClass]
public sealed class AttachmentTests
{
    private static AiAttachment Image()=>AiAttachment.FromBytes("image.png",Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX1sAAAAASUVORK5CYII="));
    [TestMethod]
    public async Task SelectedFilesAreBoundedSnapshotsWithNoLocalPath()
    {
        string root=Path.GetFullPath(Path.Combine("artifacts","attachment-tests",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
        string path=Path.Combine(root,"note.txt");await File.WriteAllTextAsync(path,"explicit text");
        var item=await AiAttachment.Read(path);File.Delete(path);
        Assert.AreEqual("note.txt",item.Name);Assert.AreEqual("explicit text",item.Text);
        Assert.IsFalse(item.ContextText.Contains(root));Assert.IsNull(item.DataUrl);
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        path=Path.Combine(root,"another.txt");await File.WriteAllTextAsync(path,"bounded");
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async()=>await AiAttachment.Read(path,cancelled.Token));
    }
    [TestMethod]
    public void InvalidAndExcessiveFilesAreRejectedWithoutSilentTruncation()
    {
        foreach(var (name,data) in new[]{("file.exe",new byte[]{1}), ("bad.png",new byte[]{1,2,3}), ("bad.pdf",Encoding.UTF8.GetBytes("not PDF")),("empty.txt",Array.Empty<byte>()),("binary.txt",new byte[]{65,0,66}), ("invalid.txt",new byte[]{255,255,255})})
            Assert.ThrowsExactly<InvalidDataException>(()=>AiAttachment.FromBytes(name,data),name);
        Assert.ThrowsExactly<InvalidDataException>(()=>AiAttachment.FromBytes("large.pdf",new byte[AiAttachment.MaxFileBytes+1]));
        Assert.ThrowsExactly<InvalidDataException>(()=>AiAttachment.FromBytes("large.txt",new byte[AiAttachment.MaxTextBytes+1]));
        Assert.ThrowsExactly<InvalidDataException>(()=>AiAttachment.Validate(Enumerable.Repeat(Image(),5).ToArray()));
        var large=Encoding.ASCII.GetBytes("%PDF-"+new string('x',4*1024*1024));var pdf=AiAttachment.FromBytes("large.pdf",large);
        Assert.ThrowsExactly<InvalidDataException>(()=>AiAttachment.Validate([pdf,pdf,pdf]));
    }
    [TestMethod]
    public void WordExtractsOnlyBodyTextAndRejectsEntityExpansion()
    {
        static byte[] docx(string xml)
        {
            using var bytes=new MemoryStream();using(var zip=new ZipArchive(bytes,ZipArchiveMode.Create,true))using(var writer=new StreamWriter(zip.CreateEntry("word/document.xml").Open()))writer.Write(xml);
            return bytes.ToArray();
        }
        var word=AiAttachment.FromBytes("note.docx",docx("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Hello</w:t><w:tab/><w:t>world</w:t></w:r></w:p><w:p><w:r><w:t>Next</w:t></w:r></w:p></w:body></w:document>"));
        Assert.AreEqual("Hello\tworld\nNext",word.Text);Assert.IsNull(word.DataUrl);
        Assert.ThrowsExactly<InvalidDataException>(()=>AiAttachment.FromBytes("evil.docx",docx("<!DOCTYPE doc [<!ENTITY a SYSTEM 'file:///private'>]><doc>&a;</doc>")));
    }
    [TestMethod]
    public async Task MultimodalWireFormatIncludesImagePdfAndExplicitTextOnly()
    {
        var image=Image();var pdf=AiAttachment.FromBytes("document.pdf",Encoding.ASCII.GetBytes("%PDF-1.7\nmock"));var text=AiAttachment.FromBytes("note.txt",Encoding.UTF8.GetBytes("attachment body"));int calls=0;
        using var client=new AiClient(new FakeHandler(async(request,token)=>
        {
            calls++;using var doc=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var parts=doc.RootElement.GetProperty("messages")[1].GetProperty("content");Assert.AreEqual(JsonValueKind.Array,parts.ValueKind);Assert.AreEqual(3,parts.GetArrayLength());
            Assert.IsTrue(parts[0].GetProperty("text").GetString()!.Contains("attachment body"));
            Assert.AreEqual(image.DataUrl,parts[1].GetProperty("image_url").GetProperty("url").GetString());
            Assert.AreEqual("auto",parts[1].GetProperty("image_url").GetProperty("detail").GetString());
            Assert.AreEqual("document.pdf",parts[2].GetProperty("file").GetProperty("filename").GetString());
            Assert.AreEqual(pdf.DataUrl,parts[2].GetProperty("file").GetProperty("file_data").GetString());
            Assert.IsFalse(doc.RootElement.GetRawText().Contains("test-only-key"));
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("data: {\"choices\":[{\"delta\":{\"content\":\"看到了\"},\"finish_reason\":\"stop\"}]}\n\n",Encoding.UTF8,"text/event-stream")};
        }));
        var settings=new Preferences{Endpoint="https://example.com/v1"};
        var turns=AiClient.BuildContext(settings,[],"看看附件",out _,attachments:[image,pdf,text]);
        Assert.IsFalse(turns[^1].Preview.Contains("base64,"));Assert.IsTrue(turns[^1].Preview.Contains("document.pdf"));
        await client.SendAsync(settings,"test-only-key",turns,_=>{},CancellationToken.None);Assert.AreEqual(1,calls);
        Assert.IsInstanceOfType<string>(new AiTurn("user","ordinary").WireContent());
        Assert.IsInstanceOfType<string>(new AiTurn("user","text",[text]).WireContent());
    }
    [TestMethod]
    public void AttachmentTextObeysBudgetAndIsNeverSilentlyOmitted()
    {
        var text=AiAttachment.FromBytes("note.txt",Encoding.UTF8.GetBytes(new string('x',6000)));
        Assert.ThrowsExactly<InvalidDataException>(()=>AiClient.BuildContext(new(),[],"看看",out _,attachments:[text]));
        var turns=AiClient.BuildContext(new(){ContextBudget=16384},[],"",out _,attachments:[text]);
        Assert.IsTrue(turns[^1].TextContent.Contains(new string('x',6000)));Assert.IsTrue(turns.Sum(AiClient.Estimate)<16384);
        var history=new[]{new ChatMessage("u","s","user","前次附件：note.txt；原内容未保存","attachment",DateTimeOffset.Now),new ChatMessage("a","s","assistant","reply","complete",DateTimeOffset.Now)};
        turns=AiClient.BuildContext(new(),history,"继续聊",out _);Assert.IsTrue(turns.All(x=>x.Attachments is null));
    }
    [TestMethod]
    public async Task UnsupportedMediaReturnsUsefulErrorWithoutRetryOrServerBodyLeak()
    {
        int calls=0;using var client=new AiClient(new FakeHandler((_,_)=>{calls++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest){Content=new StringContent("private provider detail")});}));
        var turns=AiClient.BuildContext(new(),[],"看看",out _,attachments:[Image()]);
        var error=await Assert.ThrowsExactlyAsync<OperationFailureException>(async()=>await client.SendAsync(new(),"test-key",turns,_=>{},CancellationToken.None));
        Assert.IsTrue(error.Message.Contains("服务拒绝了附件"));Assert.IsFalse(error.Message.Contains("private"));Assert.AreEqual(1,calls);
    }
    [TestMethod]
    public void AttachmentMessageMetadataRemainsExportableAndImportable()
    {
        string root=Path.Combine("artifacts","attachment-store",Guid.NewGuid().ToString("N"));using var store=new Store(root);var session=store.NewSession("https://example.com/v1");
        store.SaveMessage(new("attachment-user",session.Id,"user","看看图片\n[本次附件：image.png；原内容未保存]","attachment",DateTimeOffset.Now));
        var export=store.Export(true,false,false,false);Store.ValidateExport(export);store.Import(export);Assert.AreEqual("attachment",store.Messages(session.Id).Single().Status);Assert.AreEqual(Store.ExportVersion,export.SchemaVersion);
        export.SchemaVersion=6;export.Messages=[export.Messages!.Single() with{Status="complete"}];Store.ValidateExport(export);store.Import(export);
    }
    [TestMethod]
    public async Task DeepSeekPdfUsesOrderedPageImagesAndRejectsRawFileBeforeNetwork()
    {
        var raw=AiAttachment.FromBytes("scan.pdf",Encoding.ASCII.GetBytes("%PDF-1.7 mock"));var image=Image();
        var settings=new Preferences{Endpoint="https://api.deepseek.com",Model="deepseek-v4-flash"};
        Assert.ThrowsExactly<InvalidDataException>(()=>AiClient.BuildContext(settings,[],"读文件",out _,attachments:[raw]));
        Assert.ThrowsExactly<InvalidDataException>(()=>AiAttachment.FromPdfPages("scan.pdf",100,Enumerable.Repeat(image,5).ToArray()));
        var rendered=AiAttachment.FromPdfPages("scan.pdf",100,[image,image]);Assert.IsNull(rendered.DataUrl);Assert.AreEqual(2,rendered.Pages.Count);
        int calls=0;using var client=new AiClient(new FakeHandler(async(request,token)=>
        {
            calls++;using var doc=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));var root=doc.RootElement;
            Assert.AreEqual("deepseek-v4-flash",root.GetProperty("model").GetString());Assert.AreEqual("disabled",root.GetProperty("thinking").GetProperty("type").GetString());
            var parts=root.GetProperty("messages").EnumerateArray().Last().GetProperty("content");Assert.AreEqual(3,parts.GetArrayLength());Assert.IsTrue(parts[0].GetProperty("text").GetString()!.Contains("scan.pdf"));
            foreach(var part in parts.EnumerateArray().Skip(1)){Assert.AreEqual("image_url",part.GetProperty("type").GetString());Assert.AreEqual(image.DataUrl,part.GetProperty("image_url").GetProperty("url").GetString());}
            Assert.IsFalse(root.GetRawText().Contains("application/pdf;base64"));
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("data: {\"choices\":[{\"delta\":{\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}\n\n")};
        }));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async()=>await client.SendAsync(settings,"test-key",[new("user","read",[raw])],_=>{},CancellationToken.None));Assert.AreEqual(0,calls);
        var turns=AiClient.BuildContext(settings,[],"read",out _,attachments:[rendered]);await client.SendAsync(settings,"test-key",turns,_=>{},CancellationToken.None);Assert.AreEqual(1,calls);
    }
}
