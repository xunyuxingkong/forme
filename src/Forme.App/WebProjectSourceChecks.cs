#if FORME_DEVELOPMENT_TOOLS
using Forme.Core;
using System.Net;
using System.Net.Http;
using System.Text;

namespace Forme.App;

internal static class WebProjectSourceChecks
{
    internal static async Task Run()
    {
        const string address="https://source.invalid/article?token=private-url-marker#section";
        using(var handler=new Handler((_,_,_)=>Task.FromResult(Html("<html><head><title>示例来源</title></head><body><nav>navigation-marker</nav><article><h1>标题</h1><p>真实 &amp; 资料。</p><script>script-marker</script><form>form-marker</form><p hidden>hidden-marker</p></article></body></html>"))))
        using(var client=new HttpClient(handler))
        {
            var source=await WebProjectSourceReader.ReadAsync(address,client,CancellationToken.None);
            Require(source.Reference.SourceUrl=="https://source.invalid/article"&&source.Reference.Name=="示例来源","Web source metadata leaked query or lost title");
            Require(source.Paragraphs.SequenceEqual(new[]{"标题","真实 & 资料。"}),"Non-body content entered source paragraphs");
            Require(handler.Calls==1,"Successful page read retried");
        }
        foreach(string blocked in new[]{"http://127.0.0.1/","http://[::1]/","http://192.168.1.1/","http://169.254.169.254/","file:///C:/secret.txt","https://user:password@source.invalid/"})
        {
            using var handler=new Handler((_,_,_)=>throw new Exception("Blocked address reached transport"));using var client=new HttpClient(handler);
            await Expect<InvalidDataException>(()=>WebProjectSourceReader.ReadAsync(blocked,client,CancellationToken.None));Require(handler.Calls==0,"Blocked initial URL was requested");
        }
        foreach(string location in new[]{"http://127.0.0.1/private","file:///C:/secret.txt"})
        {
            using var handler=new Handler((_,_,_)=>Task.FromResult(Redirect(location)));using var client=new HttpClient(handler);
            await Expect<InvalidDataException>(()=>WebProjectSourceReader.ReadAsync(address,client,CancellationToken.None));Require(handler.Calls==1,"Blocked redirect target was requested");
        }
        using(var handler=new Handler((_,_,_)=>Task.FromResult(Redirect("/again"))))
        using(var client=new HttpClient(handler))
        {
            await Expect<InvalidDataException>(()=>WebProjectSourceReader.ReadAsync(address,client,CancellationToken.None));Require(handler.Calls==5,"Redirect request budget was not enforced");
        }
        foreach(int status in new[]{401,404,429,503})
        {
            using var handler=new Handler((_,_,_)=>Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));using var client=new HttpClient(handler);
            var error=await Expect<OperationFailureException>(()=>WebProjectSourceReader.ReadAsync(address,client,CancellationToken.None));
            Require(OperationErrors.Expected(error)&&error.Message.Contains(status.ToString(),StringComparison.Ordinal)&&handler.Calls==1,"HTTP failure was not recoverable or retried");
        }
        using(var handler=new Handler((_,_,_)=>throw new HttpRequestException("private-url-marker certificate/connection failure")))
        using(var client=new HttpClient(handler))
        {
            var error=await Expect<OperationFailureException>(()=>WebProjectSourceReader.ReadAsync(address,client,CancellationToken.None));
            Require(OperationErrors.Expected(error)&&!error.Message.Contains("private-url-marker",StringComparison.Ordinal)&&handler.Calls==1,"Network failure escaped UI handling or exposed request details");
        }
        foreach(bool transferFailure in new[]{false,true})
        {
            using var handler=new Handler((_,_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new UnknownLengthContent(transferFailure)}));using var client=new HttpClient(handler);
            if(transferFailure)await Expect<OperationFailureException>(()=>WebProjectSourceReader.ReadAsync(address,client,CancellationToken.None));
            else await Expect<InvalidDataException>(()=>WebProjectSourceReader.ReadAsync(address,client,CancellationToken.None));
            Require(handler.Calls==1,"Transfer failure or oversized body retried");
        }
        using(var cancellation=new CancellationTokenSource())
        using(var handler=new Handler((_,_,token)=>{cancellation.Cancel();return Task.FromCanceled<HttpResponseMessage>(token);}))
        using(var client=new HttpClient(handler))
        {
            await Expect<OperationCanceledException>(()=>WebProjectSourceReader.ReadAsync(address,client,cancellation.Token));Require(handler.Calls==1,"Cancelled read retried");
        }
        foreach(string ip in new[]{"0.1.2.3","10.0.0.1","100.64.0.1","127.0.0.1","169.254.1.1","172.31.2.1","192.168.1.1","198.18.0.1","203.0.113.1","224.0.0.1","::1","::ffff:192.168.1.1","fe80::1","fc00::1","2001:db8::1","2002:7f00:1::"})
            Require(!WebProjectSourceReader.IsPublicAddress(IPAddress.Parse(ip)),"Non-public IP passed connection guard: "+ip);
        foreach(string ip in new[]{"8.8.8.8","1.1.1.1","2606:4700:4700::1111"})Require(WebProjectSourceReader.IsPublicAddress(IPAddress.Parse(ip)),"Public IP rejected: "+ip);
        File.WriteAllText("artifacts/web-source-smoke-result.txt","PASS: static body extraction and sanitized source URL; initial and redirected private/file/credential targets; bounded redirects; recoverable HTTP/network/stream failures; unknown-length size limit; cancellation; public/private IP ranges. Offline handlers only, no actual website or AI requests.");
    }

    private static HttpResponseMessage Html(string html)=>new(HttpStatusCode.OK){Content=new StringContent(html,Encoding.UTF8,"text/html")};
    private static HttpResponseMessage Redirect(string location){var response=new HttpResponseMessage(HttpStatusCode.Redirect);response.Headers.Location=new Uri(location,UriKind.RelativeOrAbsolute);return response;}
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    private static async Task<T> Expect<T>(Func<Task> action) where T:Exception
    {try{await action();}catch(T error){return error;}throw new Exception("Expected "+typeof(T).Name);}
    private sealed class Handler(Func<HttpRequestMessage,int,CancellationToken,Task<HttpResponseMessage>> respond):HttpMessageHandler
    {
        internal int Calls {get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>respond(request,++Calls,token);
    }
    private sealed class UnknownLengthContent:HttpContent
    {
        private readonly bool _fail;
        internal UnknownLengthContent(bool fail){_fail=fail;Headers.ContentType=new("text/html");}
        protected override bool TryComputeLength(out long length){length=0;return false;}
        protected override async Task SerializeToStreamAsync(Stream stream,TransportContext? context)
        {if(_fail)throw new IOException("private-url-marker");await stream.WriteAsync(new byte[1_000_001]);}
    }
}
#endif
