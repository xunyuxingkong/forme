using Forme.Core;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Forme.App;

// Only this bundled, trusted worker is launchable. A job limits lifetime/resources, not OS access rights.
internal static class McpWebSource
{
    private const string ToolName="forme_read_public_page",ServerName="Forme.ReadOnlyWeb";
    private static readonly JsonSerializerOptions Json=new(){Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
#if FORME_DEVELOPMENT_TOOLS
    internal static bool FixtureForChecks,StallForChecks,ExtraToolForChecks;
    internal static int LastProcessForChecks;
    internal static bool LastExitedForChecks;
    internal static long LastPrivateBytesForChecks,LastPeakWorkingSetForChecks;
#endif
    internal static async Task<ProjectSource> ReadAsync(string address,CancellationToken cancellation,TimeSpan? timeout=null)
    {
        if(!Uri.TryCreate(address.Trim(),UriKind.Absolute,out var uri))throw new InvalidDataException("请输入完整网页地址（http:// 或 https://）。");
        WebProjectSourceReader.ValidateAddress(uri);
        using var deadline=new CancellationTokenSource(timeout??TimeSpan.FromSeconds(20));
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellation,deadline.Token);var token=linked.Token;
        string executable=Environment.ProcessPath??throw new OperationFailureException("无法启动本机网页工具。");
        var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=AppContext.BaseDirectory,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        info.Environment.Clear();
        foreach(string name in new[]{"SystemRoot","WINDIR","TEMP","TMP","DOTNET_ROOT","DOTNET_ROOT_X64"})if(Environment.GetEnvironmentVariable(name) is {} value)info.Environment[name]=value;
        if(Path.GetFileNameWithoutExtension(executable).Equals("dotnet",StringComparison.OrdinalIgnoreCase))info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"Forme.dll"));
        info.ArgumentList.Add("--mcp-web-source");
#if FORME_DEVELOPMENT_TOOLS
        if(FixtureForChecks)info.ArgumentList.Add("--mcp-fixture");
        if(StallForChecks)info.ArgumentList.Add("--mcp-stall");
        if(ExtraToolForChecks)info.ArgumentList.Add("--mcp-extra-tool");
#endif
        using var process=new Process{StartInfo=info};using var job=CreateJobObject(IntPtr.Zero,null);
        McpClient? client=null;bool started=false;Task? stderr=null;
        try
        {
            token.ThrowIfCancellationRequested();
            if(!process.Start())throw new OperationFailureException("无法启动本机网页工具。");started=true;
#if FORME_DEVELOPMENT_TOOLS
            LastProcessForChecks=process.Id;LastExitedForChecks=false;
#endif
            var limits=new JobLimits{Basic=new(){Flags=0x2108,ActiveProcesses=1},ProcessMemoryLimit=(UIntPtr)(256UL*1024*1024)};
            if(job.IsInvalid||!SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf<JobLimits>())||!AssignProcessToJobObject(job,process.SafeHandle))throw new OperationFailureException("无法设置网页工具的进程限制，未开始读取。");
            using var stop=token.Register(()=>Kill(process));
            stderr=process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            // Release worker startup only after the lifetime/resource job has been attached.
            await process.StandardInput.BaseStream.WriteAsync(new byte[]{0x46},token).ConfigureAwait(false);
            await process.StandardInput.BaseStream.FlushAsync(token).ConfigureAwait(false);
            client=await McpClient.CreateAsync(new StreamClientTransport(process.StandardInput.BaseStream,process.StandardOutput.BaseStream),cancellationToken:token).ConfigureAwait(false);
            var tools=await client.ListToolsAsync(cancellationToken:token).ConfigureAwait(false);
            if(client.ServerInfo.Name!=ServerName||tools.Count!=1||tools[0].Name!=ToolName)throw new OperationFailureException("网页工具身份或能力清单不匹配，未执行读取。");
            var result=await client.CallToolAsync(ToolName,new Dictionary<string,object?>{{"url",address}},cancellationToken:token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if(result.Content is not {Count:1}||result.Content[0] is not TextContentBlock {Text:{} body}||body.Length>8_000_000)throw new InvalidDataException("网页工具返回格式无效，未接收来源。");
            if(result.IsError==true)throw new OperationFailureException(body.Length<=300&&!string.IsNullOrWhiteSpace(body)?body:"网页工具未能完成读取。");
            var reply=JsonSerializer.Deserialize<WebReply>(body,Json)??throw new InvalidDataException("网页工具没有返回来源。");
            var source=reply.Source;
            if(reply.Version!="1"||source?.Reference is not {SourceUrl:{} finalUrl,Name:{} name}||source.Paragraphs is null||source.Paragraphs.Any(p=>p is null)||!Uri.TryCreate(finalUrl,UriKind.Absolute,out var finalUri))throw new InvalidDataException("网页工具来源版本或结构无效。");
            WebProjectSourceReader.ValidateAddress(finalUri);
            var verified=ProjectSource.FromWebPage(name,finalUrl,string.Join('\n',source.Paragraphs));
            if(verified.Reference!=source.Reference)throw new InvalidDataException("网页工具来源内容与哈希不一致，未接收。");
#if FORME_DEVELOPMENT_TOOLS
            process.Refresh();LastPrivateBytesForChecks=process.PrivateMemorySize64;LastPeakWorkingSetForChecks=process.PeakWorkingSet64;
#endif
            return verified;
        }
        catch(Exception) when(deadline.IsCancellationRequested&&!cancellation.IsCancellationRequested){throw new TimeoutException("网页工具超过读取期限，已停止；未发送给AI。");}
        catch(Exception) when(token.IsCancellationRequested){token.ThrowIfCancellationRequested();throw;}
        catch(Exception ex) when(ex is McpException or System.ComponentModel.Win32Exception or IOException or JsonException or InvalidOperationException)
        {throw new OperationFailureException("本机网页工具无法启动、连接中断或返回异常；未添加来源，也没有自动重试。");}
        finally
        {
            if(started)Kill(process);job.Dispose();
            bool exited=!started;
            if(started)try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);exited=true;}catch(TimeoutException){}
            if(client is not null)try{await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);}catch(Exception ex) when(ex is McpException or IOException or TimeoutException or ObjectDisposedException){}
            if(stderr is not null)try{await stderr.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);}catch(Exception ex) when(ex is IOException or TimeoutException or ObjectDisposedException){}
#if FORME_DEVELOPMENT_TOOLS
            LastExitedForChecks=exited;
#endif
            if(!exited)throw new OperationFailureException("网页工具尚未确认停止，未接收返回来源；请退出应用后检查。");
        }
    }

    private sealed record WebReply(string Version,ProjectSource Source);
    internal static async Task<int> Worker(string[] args)
    {
        using var lifetime=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var input=Console.OpenStandardInput();using var output=Console.OpenStandardOutput();
        try
        {
            var gate=new byte[1];await input.ReadExactlyAsync(gate,lifetime.Token).AsTask().WaitAsync(lifetime.Token).ConfigureAwait(false);
            if(gate[0]!=0x46)return 1;
#if FORME_DEVELOPMENT_TOOLS
            if(args.Contains("--mcp-stall"))await Task.Delay(Timeout.Infinite,lifetime.Token);
#endif
            var tool=McpServerTool.Create((string url,CancellationToken token)=>ReadTool(url,args,token),new(){Name=ToolName,ReadOnly=true,Destructive=false,Description="Read a single explicitly selected public HTTP(S) page as static text. No scripts, cookies, files or system input."});
            var options=new McpServerOptions{ServerInfo=new(){Name=ServerName,Version="1"},ToolCollection=[tool]};
#if FORME_DEVELOPMENT_TOOLS
            if(args.Contains("--mcp-extra-tool"))options.ToolCollection.Add(McpServerTool.Create(()=>"unexpected",new(){Name="fixture_unexpected_tool"}));
#endif
            await using var server=McpServer.Create(new StreamServerTransport(input,output),options);
            await server.RunAsync(lifetime.Token).ConfigureAwait(false);return 0;
        }
        catch(Exception){return 1;} // No raw protocol/input/URL data is written to stderr or local logs.
    }
    private static async Task<CallToolResult> ReadTool(string url,string[] args,CancellationToken token)
    {
        try
        {
            ProjectSource source;
#if FORME_DEVELOPMENT_TOOLS
            if(args.Contains("--mcp-fixture"))
            {
                using var client=new System.Net.Http.HttpClient(new McpWebSourceChecks.FixtureHandler());source=await WebProjectSourceReader.ReadAsync(url,client,token).ConfigureAwait(false);
                if(new Uri(url).AbsolutePath=="/bad-hash")source=source with{Reference=source.Reference with{Hash=new string('0',64)}};
                if(new Uri(url).AbsolutePath=="/missing-source")return new(){Content=[new TextContentBlock{Text="{\"Version\":\"1\",\"Source\":null}"}]};
            }
            else
#endif
            source=await WebProjectSourceReader.ReadAsync(url,token).ConfigureAwait(false);
            return new(){Content=[new TextContentBlock{Text=JsonSerializer.Serialize(new WebReply("1",source),Json)}]};
        }
        catch(Exception ex) when(OperationErrors.Expected(ex)){return new(){IsError=true,Content=[new TextContentBlock{Text=OperationErrors.Message(ex)}]};}
    }
    private static void Kill(Process process){try{if(!process.HasExited)process.Kill(true);}catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception){} }
    [StructLayout(LayoutKind.Sequential)] private struct BasicJobLimits{public long ProcessTime,JobTime;public uint Flags;public UIntPtr MinimumWorkingSet,MaximumWorkingSet;public uint ActiveProcesses;public UIntPtr Affinity;public uint Priority,Scheduling;}
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters{public ulong ReadOperations,WriteOperations,OtherOperations,ReadBytes,WriteBytes,OtherBytes;}
    [StructLayout(LayoutKind.Sequential)] private struct JobLimits{public BasicJobLimits Basic;public IoCounters Io;public UIntPtr ProcessMemoryLimit,JobMemoryLimit,PeakProcessMemory,PeakJobMemory;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes,string? name);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeFileHandle job,int kind,ref JobLimits limits,uint size);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(SafeFileHandle job,SafeProcessHandle process);
}
