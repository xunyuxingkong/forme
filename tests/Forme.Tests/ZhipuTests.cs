using System.Net;
using System.Text;
using System.Text.Json;
using Forme.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class ZhipuTests
{
    private const string Reasoning="data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"PRIVATE_REASONING\"}}]}\n\n";
    private const string Reply="data: {\"choices\":[{\"delta\":{\"content\":\"收到附件\"}}]}\n\ndata: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n";

    [TestMethod]
    public async Task OfficialStreamingPreservesBoundsAndNativeAttachments()
    {
        var image=AiAttachment.FromBytes("sample.png",Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX1sAAAAASUVORK5CYII="));
        var pdf=AiAttachment.FromBytes("sample.pdf","%PDF-1.7 sample"u8.ToArray());
        foreach(var (endpoint,model,adapt,effort) in new[]{(AiClient.ZhipuEndpoint,AiClient.ZhipuModel,true,"low"),(AiClient.ZhipuEndpoint+"/","glm-5.3-flashx",true,"max"),(AiClient.ZhipuEndpoint,"glm-5.3",true,"high"),(AiClient.ZhipuEndpoint,"other-model",false,"max"),("https://example.com/v1",AiClient.ZhipuModel,false,"high")})
        {
            int calls=0;using var client=new AiClient(new FakeHandler(async(request,token)=>
            {
                calls++;Assert.AreEqual(endpoint.TrimEnd('/')+"/chat/completions",request.RequestUri!.AbsoluteUri);Assert.AreEqual("Bearer",request.Headers.Authorization!.Scheme);Assert.AreEqual("zhipu-test-key",request.Headers.Authorization.Parameter);
                using var doc=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));var root=doc.RootElement;
                Assert.AreEqual(model,root.GetProperty("model").GetString());Assert.IsTrue(root.GetProperty("stream").GetBoolean());Assert.AreEqual(512,root.GetProperty("max_tokens").GetInt32());Assert.AreEqual(.7,root.GetProperty("temperature").GetDouble());
                Assert.AreEqual(adapt,root.TryGetProperty("thinking",out var thinking));if(adapt){Assert.AreEqual("enabled",thinking.GetProperty("type").GetString());Assert.IsTrue(thinking.GetProperty("clear_thinking").GetBoolean());Assert.AreEqual(effort,root.GetProperty("reasoning_effort").GetString());}else Assert.IsFalse(root.TryGetProperty("reasoning_effort",out _));
                var parts=root.GetProperty("messages")[0].GetProperty("content");Assert.AreEqual(3,parts.GetArrayLength());Assert.AreEqual(!AiClient.IsZhipuEndpoint(endpoint),parts[1].GetProperty("image_url").TryGetProperty("detail",out _));Assert.AreEqual(pdf.DataUrl,parts[2].GetProperty("file").GetProperty("file_data").GetString());Assert.AreEqual("sample.pdf",parts[2].GetProperty("file").GetProperty("filename").GetString());Assert.IsFalse(root.TryGetProperty("tools",out _));Assert.IsFalse(root.GetRawText().Contains("zhipu-test-key"));
                return new(HttpStatusCode.OK){Content=new StringContent(Reasoning+Reply,Encoding.UTF8,"text/event-stream")};
            }));string visible="";
            var result=await client.SendAsync(new(){Endpoint=endpoint,Model=model,ZhipuReasoningEffort=effort},"zhipu-test-key",[new("user","看看附件",[image,pdf])],delta=>visible+=delta,CancellationToken.None);
            Assert.AreEqual("收到附件",result.Content);Assert.AreEqual(result.Content,visible);Assert.AreEqual(1,calls);
        }
    }

    [TestMethod]
    public void CredentialAndMemoryIsolationPreserveExistingCustomIdentity()
    {
        Assert.IsTrue(AiClient.IsZhipuEndpoint(AiClient.ZhipuEndpoint+"/"));Assert.IsFalse(AiClient.IsZhipuEndpoint("https://open.bigmodel.cn/api/coding/paas/v4"));Assert.IsFalse(AiClient.IsZhipuEndpoint("https://open.bigmodel.cn.evil.example/api/paas/v4"));
        Assert.AreEqual("custom:https://open.bigmodel.cn",AiClient.ProviderIdentity(AiClient.ZhipuEndpoint));
        string path=Path.Combine("artifacts","zhipu-tests",Guid.NewGuid().ToString("N"));using var store=new Store(path);var old=store.LoadPreferences();var session=store.NewSession(old.Endpoint);var secret=new Secret();var settings=old with{Endpoint=AiClient.ZhipuEndpoint,Model=AiClient.ZhipuModel};
        Assert.ThrowsExactly<InvalidDataException>(()=>store.SaveAiConfiguration(settings,session,secret,""));Assert.AreEqual(old.Endpoint,store.LoadPreferences().Endpoint);Assert.AreEqual("old-key",secret.Key);
        Assert.AreEqual("low",JsonSerializer.Deserialize<Preferences>("{}")!.ZhipuReasoningEffort);Assert.ThrowsExactly<InvalidDataException>(()=>(settings with{ZhipuReasoningEffort="medium"}).Validate());
        var next=store.SaveAiConfiguration(settings with{ZhipuReasoningEffort="high"},session,secret,"new-zhipu-key");Assert.AreNotEqual(session.Id,next!.Id);Assert.AreEqual("new-zhipu-key",secret.Key);Assert.AreEqual("high",store.LoadPreferences().ZhipuReasoningEffort);
        var now=DateTimeOffset.Now;store.SaveMemory(new("z","偏好","ZHIPU_MEMORY",AiClient.ProviderIdentity(settings.Endpoint),true,now,now));store.SaveMemory(new("d","旧服务","DEEPSEEK_PRIVATE","deepseek",true,now,now));
        var context=AiClient.BuildContext(settings with{MemoryEnabled=true},[],"你好",out _,store.Memories());Assert.IsTrue(context[0].Content.Contains("ZHIPU_MEMORY"));Assert.IsFalse(context[0].Content.Contains("DEEPSEEK_PRIVATE"));Store.ValidateExport(store.Export(true,false,false,false));
    }

    [TestMethod]
    public async Task WebpIsRejectedBeforeNetwork()
    {
        var webp=AiAttachment.FromBytes("sample.webp","RIFF0000WEBP"u8.ToArray());var settings=new Preferences{Endpoint=AiClient.ZhipuEndpoint,Model=AiClient.ZhipuModel};int calls=0;
        using var client=new AiClient(new FakeHandler((_,_)=>{calls++;throw new Exception("must not send");}));
        Assert.ThrowsExactly<InvalidDataException>(()=>AiClient.BuildContext(settings,[],"图片",out _,attachments:[webp]));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(()=>client.SendAsync(settings,"test",[new("user","图片",[webp])],_=>{},CancellationToken.None));Assert.AreEqual(0,calls);
    }

    [TestMethod]
    public async Task ReasoningProgressStillHasTotalTimeoutAndIsNeverDisplayed()
    {
        var settings=new Preferences{Endpoint=AiClient.ZhipuEndpoint,Model=AiClient.ZhipuModel};string visible="";
        using(var client=new AiClient(new FakeHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new DelayedReplyStream())})),new(TimeSpan.FromMilliseconds(80),TimeSpan.FromSeconds(2),TimeSpan.FromSeconds(3))))
        {var result=await client.SendAsync(settings,"test",[new("user","你好")],delta=>visible+=delta,CancellationToken.None);Assert.AreEqual("收到附件",result.Content);Assert.AreEqual(result.Content,visible);}
        using var stalled=new AiClient(new FakeHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new StallStream(Encoding.UTF8.GetBytes(Reasoning)))})),new(TimeSpan.FromMilliseconds(80),TimeSpan.FromSeconds(5),TimeSpan.FromMilliseconds(200)));
        await Assert.ThrowsExactlyAsync<TimeoutException>(()=>stalled.SendAsync(settings,"test",[new("user","你好")],_=>Assert.Fail("Reasoning must not be shown"),CancellationToken.None));
    }

    [TestMethod]
    public void ChatAndAttachmentTimeoutBudgetsRemainBounded()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(60),AiLimits.Default.First);
        Assert.AreEqual(TimeSpan.FromSeconds(180),AiLimits.Default.Total);
        Assert.AreEqual(TimeSpan.FromSeconds(90),AiLimits.ForAttachments.First);
        Assert.AreEqual(TimeSpan.FromSeconds(240),AiLimits.ForAttachments.Total);
    }

    [TestMethod]
    public async Task ErrorsDoNotRetryOrExposeProviderBody()
    {
        foreach(var status in new[]{HttpStatusCode.Unauthorized,HttpStatusCode.BadRequest,HttpStatusCode.TooManyRequests})
        {int calls=0;using var client=new AiClient(new FakeHandler((_,_)=>{calls++;return Task.FromResult(new HttpResponseMessage(status){Content=new StringContent("PRIVATE_SERVER_BODY")});}));var error=await Assert.ThrowsExactlyAsync<OperationFailureException>(()=>client.SendAsync(new(){Endpoint=AiClient.ZhipuEndpoint,Model=AiClient.ZhipuModel},"test",[new("user","你好")],_=>{},CancellationToken.None));Assert.IsFalse(error.Message.Contains("PRIVATE_SERVER_BODY"));Assert.AreEqual(1,calls);}
    }

    private sealed class Secret:ISecretStore{public string Key="old-key";public string Read()=>Key;public void Save(string key)=>Key=key;}
    private sealed class DelayedReplyStream:MemoryStream
    {
        private int _stage;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default)
        {if(_stage>=2)return 0;if(_stage==1)await Task.Delay(200,cancellationToken);var bytes=Encoding.UTF8.GetBytes(_stage++==0?Reasoning:Reply);bytes.CopyTo(buffer);return bytes.Length;}
    }
}
