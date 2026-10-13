using Forme.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Text;

[TestClass]
public class StreamingLatencyTests
{
    [TestMethod,TestCategory("AI")]
    public async Task CompleteSseEventAtBufferBoundaryDoesNotWaitForMoreNetworkData()
    {
        foreach(int size in new[]{1024,4096})
        {
            const string reply="data: {\"choices\":[{\"delta\":{\"content\":\"及时回答\"},\"finish_reason\":\"stop\"}]}\n\n";
            byte[] bytes=Encoding.UTF8.GetBytes(reply+":"+new string(' ',size-Encoding.UTF8.GetByteCount(reply)-1));
            using var client=new AiClient(new FakeHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new StallStream(bytes))})),new(TimeSpan.FromMilliseconds(300),TimeSpan.FromMilliseconds(300),TimeSpan.FromSeconds(2)));
            string visible="";var result=await client.SendAsync(new(),"test",[new("user","测试")],part=>visible+=part,CancellationToken.None);
            Assert.AreEqual("及时回答",result.Content);Assert.AreEqual(result.Content,visible);
        }
    }
    [TestMethod,TestCategory("AI")]
    public async Task ReasoningProgressFromCompatibleProviderExtendsIdleButRemainsPrivate()
    {
        using var client=new AiClient(new FakeHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new DelayedReasoningStream())})),new(TimeSpan.FromMilliseconds(100),TimeSpan.FromSeconds(2),TimeSpan.FromSeconds(3)));
        var stages=new List<AiRequestStage>();string visible="";
        var result=await client.SendAsync(new(){Endpoint="https://compatible.example/v1",Model="reasoning-model"},"test",[new("user","测试")],part=>visible+=part,CancellationToken.None,stageProgress:stages.Add);
        Assert.AreEqual("回答",visible);Assert.AreEqual(visible,result.Content);
        CollectionAssert.AreEqual(new[]{AiRequestStage.WaitingForResponse,AiRequestStage.WaitingForContent,AiRequestStage.Thinking,AiRequestStage.Responding},stages);
    }
    [TestMethod,TestCategory("AI")]
    public async Task TimeoutReportsStageWithoutRetryOrLeakingReasoning()
    {
        foreach(string mode in new[]{"headers","content","thinking","partial"})
        {
            int calls=0;string visible="";
            var handler=new FakeHandler(async(_,token)=>
            {
                calls++;if(mode=="headers")await Task.Delay(Timeout.Infinite,token);
                string data=mode=="thinking"?"data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"PRIVATE_REASONING\"}}]}\n\n":mode=="partial"?"data: {\"choices\":[{\"delta\":{\"content\":\"保留\"}}]}\n\n":"";
                return new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new StallStream(Encoding.UTF8.GetBytes(data)))};
            });
            using var client=new AiClient(handler,new(TimeSpan.FromMilliseconds(150),TimeSpan.FromMilliseconds(150),TimeSpan.FromSeconds(2)));
            var error=await Assert.ThrowsExactlyAsync<TimeoutException>(()=>client.SendAsync(new(),"test",[new("user","测试")],part=>visible+=part,CancellationToken.None));
            string expected=mode switch{"headers"=>"尚未收到响应","content"=>"服务已响应","thinking"=>"模型思考阶段超时",_=>"已保留部分内容"};
            Assert.IsTrue(error.Message.Contains(expected));Assert.IsFalse(error.Message.Contains("PRIVATE_REASONING"));Assert.AreEqual(1,calls);Assert.AreEqual(mode=="partial"?"保留":"",visible);
        }
    }
    private sealed class DelayedReasoningStream:Stream
    {
        private int _stage;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default)
        {
            if(_stage>=2)return 0;if(_stage==1)await Task.Delay(250,cancellationToken);
            byte[] bytes=Encoding.UTF8.GetBytes(_stage++==0?"data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"PRIVATE_REASONING\"}}]}\n\n":"data: {\"choices\":[{\"delta\":{\"content\":\"回答\"},\"finish_reason\":\"stop\"}]}\n\n");bytes.CopyTo(buffer);return bytes.Length;
        }
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}public override void Flush(){}public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
}
