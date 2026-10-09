using System.Net;
using System.Text;
using System.Text.Json;
using Forme.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class SiliconFlowTests
{
    [TestMethod,TestCategory("AI")]
    public async Task SiliconFlowStreamingAndProviderParameters()
    {
        foreach(var (endpoint,model,nonThinking) in new[]{
            ("https://api.siliconflow.cn/v1","deepseek-ai/DeepSeek-V3.2",true),
            ("https://api.siliconflow.cn/v1/","Pro/deepseek-ai/DeepSeek-V3.2",true),
            ("https://api.siliconflow.cn/v1","custom-model",false),
            ("https://example.com/v1","deepseek-ai/DeepSeek-V3.2",false)})
        {
            int calls=0;
            var handler=new FakeHandler(async(request,token)=>
            {
                calls++;
                Assert.AreEqual(endpoint.TrimEnd('/')+"/chat/completions",request.RequestUri!.AbsoluteUri);
                Assert.AreEqual("Bearer",request.Headers.Authorization!.Scheme);
                Assert.AreEqual("test-only-key",request.Headers.Authorization.Parameter);
                using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var root=body.RootElement;
                Assert.AreEqual(model,root.GetProperty("model").GetString());
                Assert.IsTrue(root.GetProperty("stream").GetBoolean());
                Assert.AreEqual(512,root.GetProperty("max_tokens").GetInt32());
                Assert.AreEqual(nonThinking,root.TryGetProperty("enable_thinking",out var thinking));
                if(nonThinking)Assert.IsFalse(thinking.GetBoolean());
                Assert.IsFalse(root.TryGetProperty("thinking",out _));
                Assert.IsFalse(root.GetRawText().Contains("test-only-key"));
                const string stream="data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"不应显示\"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"你好\"}}]}\n\ndata: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
                return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(stream,Encoding.UTF8,"text/event-stream")};
            });
            using var client=new AiClient(handler);string visible="";
            var result=await client.SendAsync(new(){Endpoint=endpoint,Model=model},"test-only-key",[new("user","你好")],delta=>visible+=delta,CancellationToken.None);
            Assert.AreEqual("你好",result.Content);Assert.AreEqual(result.Content,visible);
            Assert.IsFalse(result.LengthLimited);Assert.AreEqual(1,calls);
        }
    }
}
