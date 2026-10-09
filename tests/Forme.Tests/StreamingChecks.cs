using Forme.Core;
using System.Net;
using System.Text;

internal static class StreamingChecks
{
    public static async Task Run(Action<bool,string> check,Func<Func<Task>,string,Task> throws)
    {
        foreach(int fragments in new[]{512,2000})
        {
            string body=string.Concat(Enumerable.Repeat("data: {\"choices\":[{\"delta\":{\"content\":\"中🙂\"}}]}\r\n\r\n",fragments))+"data: [DONE]\r\n\r\n";
            var chunks=new ChunkStream(Encoding.UTF8.GetBytes(body));
            using var ai=new AiClient(new FakeHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(chunks)})));
            var pending=new ReplyBuffer();int callbacks=0;
            var result=await ai.SendAsync(new(),"fake",[new("user","hello")],delta=>{callbacks++;if(delta!="中🙂")throw new Exception("Progress must contain only the new delta.");pending.Append(delta);},CancellationToken.None);
            check(callbacks==fragments&&result.Content==string.Concat(Enumerable.Repeat("中🙂",fragments)),"fragmented UTF8 preserves "+fragments+" fragments");
            check(pending.ReadChanges()==result.Content&&pending.ReadChanges() is null,"burst deltas merge into one UI sample");
            check(chunks.Reads<Encoding.UTF8.GetByteCount(body),"reader requests buffered chunks");
        }
        string multi=": comment\r\ndata: {\"choices\":\r\ndata: [{\"delta\":{\"content\":\"多行\"},\"finish_reason\":\"stop\"}]}\r\n\r\n";
        using(var ai=new AiClient(new FakeHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(multi)}))))
            check((await ai.SendAsync(new(),"fake",[new("user","hello")],_=>{},CancellationToken.None)).Content=="多行","multiline SSE data is assembled as one event");
        foreach(string bad in new[]{"data: "+new string('x',65537)+"\n\n","data: {\"error\":{}}\n\n"})
        {
            using var ai=new AiClient(new FakeHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(bad)})));
            await throws(()=>ai.SendAsync(new(),"fake",[new("user","hello")],_=>{},CancellationToken.None),"oversized or error SSE rejected");
        }
        using var cancel=new CancellationTokenSource();var partial=new ReplyBuffer();
        using(var ai=new AiClient(new FakeHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new StallStream(Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\"保留\"}}]}\n\n")))}))))
        {
            await throws(()=>ai.SendAsync(new(),"fake",[new("user","hello")],delta=>{partial.Append(delta);cancel.Cancel();},cancel.Token),"stream cancellation exits without replay");
            check(partial.Snapshot()=="保留","cancellation retains deltas before first UI sample");
        }
    }
    private sealed class ChunkStream(byte[] bytes):MemoryStream(bytes)
    {
        public int Reads;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default)
        {Reads++;return base.ReadAsync(buffer[..Math.Min(buffer.Length,7)],cancellationToken);}
    }
}
