#if FORME_DEVELOPMENT_TOOLS
using Forme.Core;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;

namespace Forme.App;

internal static class McpWebSourceChecks
{
    internal static async Task Run()
    {
        string? previous=Environment.GetEnvironmentVariable("FORME_MCP_TEST_SECRET");
        Environment.SetEnvironmentVariable("FORME_MCP_TEST_SECRET","not-for-child");McpWebSource.FixtureForChecks=true;
        try
        {
            var elapsed=Stopwatch.StartNew();var source=await McpWebSource.ReadAsync("https://mcp-check.invalid/article?token=private-marker",CancellationToken.None);elapsed.Stop();
            double milliseconds=elapsed.Elapsed.TotalMilliseconds;long privateBytes=McpWebSource.LastPrivateBytesForChecks,peakWorkingSet=McpWebSource.LastPeakWorkingSetForChecks;
            Require(source.Reference.SourceUrl=="https://mcp-check.invalid/article"&&source.Paragraphs.Contains("跨进程来源正文。"),"MCP round trip lost source metadata or body");AssertExited();
            for(int i=0;i<2;i++){await McpWebSource.ReadAsync("https://mcp-check.invalid/article",CancellationToken.None);AssertExited();}
            McpWebSource.ExtraToolForChecks=true;
            try{await McpWebSource.ReadAsync("https://mcp-check.invalid/article",CancellationToken.None);throw new Exception("Unexpected tool manifest accepted");}
            catch(OperationFailureException ex){Require(ex.Message.Contains("清单",StringComparison.Ordinal),"Unexpected tool manifest was not rejected before invocation");}finally{McpWebSource.ExtraToolForChecks=false;}AssertExited();
            try{await McpWebSource.ReadAsync("https://mcp-check.invalid/bad-hash",CancellationToken.None);throw new Exception("Corrupt source hash accepted");}catch(InvalidDataException){}AssertExited();
            try{await McpWebSource.ReadAsync("https://mcp-check.invalid/missing-source",CancellationToken.None);throw new Exception("Missing source accepted");}catch(InvalidDataException){}AssertExited();
            try{await McpWebSource.ReadAsync("https://mcp-check.invalid/http503",CancellationToken.None);throw new Exception("HTTP tool failure accepted");}
            catch(OperationFailureException ex){Require(ex.Message.Contains("503",StringComparison.Ordinal),"HTTP tool status was not preserved");}AssertExited();
            try{await McpWebSource.ReadAsync("http://127.0.0.1/private",CancellationToken.None);throw new Exception("Private URL accepted");}catch(InvalidDataException){}
            using(var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(1)))
            {
                try{await McpWebSource.ReadAsync("https://mcp-check.invalid/stall",cancellation.Token);throw new Exception("Cancelled worker returned source");}catch(OperationCanceledException){}AssertExited();
            }
            McpWebSource.StallForChecks=true;
            try{await McpWebSource.ReadAsync("https://mcp-check.invalid/article",CancellationToken.None,TimeSpan.FromMilliseconds(600));throw new Exception("Stalled MCP handshake escaped deadline");}catch(TimeoutException){}finally{McpWebSource.StallForChecks=false;}AssertExited();
            try{await McpWebSource.ReadAsync("https://mcp-check.invalid/exit",CancellationToken.None);throw new Exception("Crashed worker returned source");}catch(OperationFailureException){}AssertExited();
            using(var controller=new Controller(Path.GetFullPath(Path.Combine("artifacts","mcp-web-checks",Guid.NewGuid().ToString("N")))))
            {
                await controller.LoadProjectWebSource("https://mcp-check.invalid/article");
                Require(controller.ProjectSources.Count==1&&!controller.ProjectSharingAllowed&&!controller.Busy,"MCP source bypassed sharing or left project busy");
                try{await controller.GenerateProjectDraft();throw new Exception("MCP source sent without sharing consent");}catch(InvalidDataException){}
                var original=controller.CurrentProject;
                try{await controller.LoadProjectWebSource("https://mcp-check.invalid/http503");throw new Exception("Failed read replaced existing sources");}catch(OperationFailureException){}
                Require(controller.ProjectSources.Count==1&&controller.CurrentProject?.Id==original?.Id&&controller.CurrentProject?.Revision==original?.Revision&&!controller.Busy,"Failed MCP read changed project or left it busy");
                controller.AllowProjectSharing(false);Require(controller.ProjectSources.Count==0&&!controller.ProjectSharingAllowed,"Revocation retained MCP page source");
            }
            File.WriteAllText("artifacts/mcp-web-source-result.txt",$"PASS: official MCP client/server handshake, fixed read-only tool discovery, source/hash round trip, filtered child environment, repeated worker reclamation, HTTP tool errors, private URL rejection, cancellation, startup deadline, abrupt worker exit and project consent/failure/revocation. All page responses are in-child fixtures; no website or model calls.\nFirst fixture round trip: {milliseconds:F0} ms; worker private bytes before cleanup: {privateBytes}; peak worker working set: {peakWorkingSet}. These are local fixture observations, not website response times or total application resource guarantees.");
        }
        finally{McpWebSource.FixtureForChecks=false;McpWebSource.StallForChecks=false;McpWebSource.ExtraToolForChecks=false;Environment.SetEnvironmentVariable("FORME_MCP_TEST_SECRET",previous);}
    }
    private static void AssertExited()
    {
        Require(McpWebSource.LastExitedForChecks,"Worker shutdown not confirmed");
        try{using var process=Process.GetProcessById(McpWebSource.LastProcessForChecks);Require(process.HasExited,"Worker process survived request");}catch(ArgumentException){}
    }
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    internal sealed class FixtureHandler:HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(Environment.GetEnvironmentVariable("FORME_MCP_TEST_SECRET") is not null)throw new OperationFailureException("Parent secret environment leaked to worker");
            switch(request.RequestUri!.AbsolutePath)
            {
                case "/stall":await Task.Delay(Timeout.Infinite,token);break;
                case "/exit":Environment.Exit(23);break;
                case "/http503":return new(HttpStatusCode.ServiceUnavailable);
            }
            return new(HttpStatusCode.OK){Content=new StringContent("<html><head><title>MCP来源</title></head><body><article><p>跨进程来源正文。</p></article></body></html>",Encoding.UTF8,"text/html")};
        }
    }
}
#endif
