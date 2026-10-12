using System.Net;
using System.Text;
using System.Text.Json;
using Forme.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class SiliconFlowTests
{
    [TestMethod,TestCategory("AI")]
    public async Task GlmThinkingBudgetsAreProviderScopedAndUserControlled()
    {
        foreach(var (endpoint,model,effort,budget) in new[]{
            ("https://api.siliconflow.cn/v1","zai-org/GLM-5.3-Flash","low",256),
            ("https://api.siliconflow.cn/v1/","zai-org/GLM-5.3-Flash","high",1024),
            ("https://api.siliconflow.cn/v1","zai-org/GLM-5.3","max",2048),
            ("https://api.siliconflow.cn/v1","Pro/zai-org/GLM-5.2","low",256),
            ("https://api.siliconflow.cn/v1","Pro/zai-org/GLM-5.2","max",2048),
            ("https://example.com/v1","zai-org/GLM-5.3-Flash","high",0),
            ("https://api.siliconflow.cn/v1","unknown-model","high",0)})
        {
            int calls=0;using var client=new AiClient(new FakeHandler(async(request,token)=>
            {
                calls++;Assert.AreEqual(endpoint.TrimEnd('/')+"/chat/completions",request.RequestUri!.AbsoluteUri);Assert.AreEqual("test-only-key",request.Headers.Authorization!.Parameter);
                using var doc=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));var root=doc.RootElement;
                Assert.AreEqual(512,root.GetProperty("max_tokens").GetInt32());Assert.AreEqual(budget>0,root.TryGetProperty("enable_thinking",out var enabled));
                if(budget>0){Assert.IsTrue(enabled.GetBoolean());Assert.AreEqual(budget,root.GetProperty("thinking_budget").GetInt32());}else Assert.IsFalse(root.TryGetProperty("thinking_budget",out _));
                if(model=="Pro/zai-org/GLM-5.2")Assert.AreEqual(effort=="max"?"max":"high",root.GetProperty("reasoning_effort").GetString());else Assert.IsFalse(root.TryGetProperty("reasoning_effort",out _));
                Assert.IsFalse(root.TryGetProperty("thinking",out _));
                return new(HttpStatusCode.OK){Content=new StringContent("data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"PRIVATE_REASONING\"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"连接成功\"}}]}\n\ndata: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n",Encoding.UTF8,"text/event-stream")};
            }));string visible="";
            var result=await client.SendAsync(new(){Endpoint=endpoint,Model=model,ZhipuReasoningEffort=effort},"test-only-key",[new("user","连接测试")],delta=>visible+=delta,CancellationToken.None);
            Assert.AreEqual("连接成功",result.Content);Assert.AreEqual(result.Content,visible);Assert.AreEqual(1,calls);
        }
        Assert.IsFalse(AiClient.IsSiliconFlowGlm("https://api.siliconflow.cn.evil.example/v1","zai-org/GLM-5.3-Flash"));
    }

    [TestMethod,TestCategory("AI")]
    public async Task GlmReasoningOnlyGivesAccurateFailureWithoutRetry()
    {
        int calls=0;using var client=new AiClient(new FakeHandler((_,_)=>{calls++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"PRIVATE_REASONING\"},\"finish_reason\":\"length\"}]}\n\n",Encoding.UTF8,"text/event-stream")});}));
        var error=await Assert.ThrowsExactlyAsync<OperationFailureException>(()=>client.SendAsync(new(){Endpoint="https://api.siliconflow.cn/v1",Model="zai-org/GLM-5.3-Flash"},"test",[new("user","测试")],_=>Assert.Fail("Reasoning must not be shown"),CancellationToken.None));
        Assert.IsTrue(error.Message.Contains("已返回思考内容"));Assert.IsFalse(error.Message.Contains("不支持非思考"));Assert.IsFalse(error.Message.Contains("PRIVATE_REASONING"));Assert.AreEqual(1,calls);
    }

    [TestMethod,TestCategory("AI")]
    public async Task GlmReasoningStillStopsAtTotalDeadline()
    {
        using var client=new AiClient(new FakeHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new StallStream(Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"PRIVATE_REASONING\"}}]}\n\n")))})),new(TimeSpan.FromMilliseconds(80),TimeSpan.FromSeconds(5),TimeSpan.FromMilliseconds(200)));
        await Assert.ThrowsExactlyAsync<TimeoutException>(()=>client.SendAsync(new(){Endpoint="https://api.siliconflow.cn/v1",Model="zai-org/GLM-5.3-Flash"},"test",[new("user","测试")],_=>Assert.Fail("Reasoning must not be shown"),CancellationToken.None));
    }

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
