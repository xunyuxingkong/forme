using Forme.Core;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Forme.App;

internal static class PdfAttachments
{
#if FORME_DEVELOPMENT_TOOLS
    internal static bool StallWorkerForSmoke;
    internal static int LastWorkerForSmoke;
    internal static bool LastWorkerExitedForSmoke;
#endif
    public static async Task<AiAttachment> Render(AiAttachment source,CancellationToken cancellation,TimeSpan? timeout=null)
    {
        if(source.MediaType!="application/pdf"||source.DataUrl is null)throw new InvalidDataException("请选择PDF文件。");
        using var limit=new CancellationTokenSource(timeout??TimeSpan.FromSeconds(30));
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellation,limit.Token);var token=linked.Token;
        string executable=Environment.ProcessPath??throw new OperationFailureException("无法启动本地PDF转换进程。");
        var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        info.Environment.Clear();
        foreach(string name in new[]{"SystemRoot","WINDIR","TEMP","TMP","PATH","DOTNET_ROOT","DOTNET_ROOT_X64"})if(Environment.GetEnvironmentVariable(name) is {} value)info.Environment[name]=value;
        if(Path.GetFileNameWithoutExtension(executable).Equals("dotnet",StringComparison.OrdinalIgnoreCase))info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"Forme.dll"));
        info.ArgumentList.Add("--render-pdf");
#if FORME_DEVELOPMENT_TOOLS
        if(StallWorkerForSmoke)info.ArgumentList.Add("--pdf-worker-stall");
#endif
        using var process=new Process{StartInfo=info};
        using var job=CreateJobObject(IntPtr.Zero,null);
        try
        {
            token.ThrowIfCancellationRequested();
            if(!process.Start())throw new OperationFailureException("无法启动本地PDF转换进程。");
            var limits=new JobLimits{Basic=new(){Flags=0x2108,ActiveProcesses=1},ProcessMemoryLimit=(UIntPtr)(256UL*1024*1024)};
            if(job.IsInvalid||!SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf<JobLimits>())||!AssignProcessToJobObject(job,process.SafeHandle))throw new OperationFailureException("无法为PDF转换进程设置资源限制，未添加。");
#if FORME_DEVELOPMENT_TOOLS
            LastWorkerForSmoke=process.Id;
            LastWorkerExitedForSmoke=false;
#endif
            string data=source.DataUrl;byte[] bytes=Convert.FromBase64String(data[(data.IndexOf(',')+1)..]);
            async Task Write()
            {
                byte[] length=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(length,bytes.Length);
                await process.StandardInput.BaseStream.WriteAsync(length,token);await process.StandardInput.BaseStream.WriteAsync(bytes,token);process.StandardInput.Close();
            }
            async Task<AiAttachment> Read()
            {
                var stream=process.StandardOutput.BaseStream;int count=await ReadInt(stream,token);
                if(count==-1)throw new InvalidDataException((await ReadInt(stream,token)) switch{2=>"PDF本地转换支持1至4页；请拆分较长PDF后添加，不会只发送前几页。",3=>"PDF页面转换后超过大小限制，请拆分后添加。",_=>"PDF无法在本机转换，可能已加密或格式损坏；请检查文件。未上传。"});
                if(count is <1 or >AiAttachment.MaxPdfPages)throw new InvalidDataException("PDF转换返回无效页数，未添加。");
                var pages=new List<AiAttachment>();long total=0;
                for(int index=0;index<count;index++)
                {
                    int length=await ReadInt(stream,token);total+=length;
                    if(length is <1 or >AiAttachment.MaxFileBytes||total>AiAttachment.MaxTotalBytes)throw new InvalidDataException("PDF页面转换后超过大小限制，未添加。");
                    var png=new byte[length];await stream.ReadExactlyAsync(png,token);pages.Add(AiAttachment.FromBytes($"page-{index+1}.png",png));
                }
                return AiAttachment.FromPdfPages(source.Name,source.ByteCount,pages);
            }
            var writing=Write();var reading=Read();await Task.WhenAll(writing,reading).WaitAsync(token);
            await process.WaitForExitAsync(token).WaitAsync(token);
            if(process.ExitCode!=0)throw new InvalidDataException("PDF转换进程未成功结束，未添加。");
            return await reading;
        }
        catch(OperationCanceledException) when(limit.IsCancellationRequested&&!cancellation.IsCancellationRequested){throw new TimeoutException("PDF本地转换超过30秒，已终止转换进程；未上传。");}
        catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or EndOfStreamException){throw new OperationFailureException("PDF本地转换进程无法启动或意外退出，未添加。");}
        finally
        {
            try
            {
                if(process.Id>0&&!process.HasExited){process.Kill();await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));}
#if FORME_DEVELOPMENT_TOOLS
                LastWorkerExitedForSmoke=process.HasExited;
#endif
            }
            catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException){}
        }
    }
    private static async Task<int> ReadInt(Stream stream,CancellationToken token)
    {var bytes=new byte[4];await stream.ReadExactlyAsync(bytes,token);return BinaryPrimitives.ReadInt32LittleEndian(bytes);}
    [StructLayout(LayoutKind.Sequential)] private struct BasicJobLimits
    {public long ProcessTime,JobTime;public uint Flags;public UIntPtr MinimumWorkingSet,MaximumWorkingSet;public uint ActiveProcesses;public UIntPtr Affinity;public uint Priority,Scheduling;}
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters
    {public ulong ReadOperations,WriteOperations,OtherOperations,ReadBytes,WriteBytes,OtherBytes;}
    [StructLayout(LayoutKind.Sequential)] private struct JobLimits
    {public BasicJobLimits Basic;public IoCounters Io;public UIntPtr ProcessMemoryLimit,JobMemoryLimit,PeakProcessMemory,PeakJobMemory;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes,string? name);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeFileHandle job,int kind,ref JobLimits limits,uint size);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(SafeFileHandle job,SafeProcessHandle process);
    public static async Task<int> Worker()
    {
        using var input=Console.OpenStandardInput();using var output=Console.OpenStandardOutput();
        try
        {
            int size=await ReadInt(input,CancellationToken.None);
            if(size is <1 or >AiAttachment.MaxFileBytes)throw new InvalidDataException("大小限制");
            var bytes=new byte[size];await input.ReadExactlyAsync(bytes);
            var rendered=await RenderInProcess(AiAttachment.FromBytes("document.pdf",bytes),CancellationToken.None);
            using var writer=new BinaryWriter(output);writer.Write(rendered.Pages.Count);
            foreach(var page in rendered.Pages){string data=page.DataUrl!;var png=Convert.FromBase64String(data[(data.IndexOf(',')+1)..]);writer.Write(png.Length);writer.Write(png);}writer.Flush();return 0;
        }
        catch(Exception ex) when(ex is not OutOfMemoryException)
        {using var writer=new BinaryWriter(output);writer.Write(-1);writer.Write(ex.Message.Contains("拆分")?2:ex.Message.Contains("超过")||ex.Message.Contains("过大")?3:1);writer.Flush();return 1;}
    }
    private static async Task<AiAttachment> RenderInProcess(AiAttachment source,CancellationToken token)
    {
        try
        {
            string data=source.DataUrl!;byte[] bytes=Convert.FromBase64String(data[(data.IndexOf(',')+1)..]);
            using var input=new InMemoryRandomAccessStream();
            using(var writer=new DataWriter(input.GetOutputStreamAt(0))){writer.WriteBytes(bytes);await writer.StoreAsync().AsTask(token);await writer.FlushAsync().AsTask(token);writer.DetachStream();}
            input.Seek(0);var document=await PdfDocument.LoadFromStreamAsync(input).AsTask(token);
            if(document.PageCount is <1 or >AiAttachment.MaxPdfPages)throw new InvalidDataException("PDF本地转换支持1至4页；请拆分较长PDF后添加，不会只发送前几页。");
            var pages=new List<AiAttachment>();
            for(uint index=0;index<document.PageCount;index++)
            {
                token.ThrowIfCancellationRequested();using var page=document.GetPage(index);var size=page.Size;
                if(!double.IsFinite(size.Width)||!double.IsFinite(size.Height)||size.Width<=0||size.Height<=0)throw new InvalidDataException("PDF页面尺寸无效。");
                double scale=Math.Min(2,1536/Math.Max(size.Width,size.Height));
                using var output=new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(output,new PdfPageRenderOptions{DestinationWidth=(uint)Math.Max(1,Math.Ceiling(size.Width*scale)),DestinationHeight=(uint)Math.Max(1,Math.Ceiling(size.Height*scale)),BitmapEncoderId=BitmapEncoder.PngEncoderId}).AsTask(token);
                if(output.Size is <1 or >AiAttachment.MaxFileBytes)throw new InvalidDataException("PDF页面转换后超过5 MB，请使用较小页面文件。");
                using var reader=new DataReader(output.GetInputStreamAt(0));uint length=(uint)output.Size;
                if(await reader.LoadAsync(length).AsTask(token)!=length)throw new InvalidDataException("PDF页面读取不完整，未添加。");
                var png=new byte[length];reader.ReadBytes(png);
                pages.Add(AiAttachment.FromBytes($"page-{index+1}.png",png));
                if(pages.Sum(p=>(long)p.ByteCount)>AiAttachment.MaxTotalBytes)throw new InvalidDataException("PDF转换后的页面合计超过10 MB，请拆分后添加。");
            }
            token.ThrowIfCancellationRequested();return AiAttachment.FromPdfPages(source.Name,source.ByteCount,pages);
        }
        catch(Exception ex) when(ex is COMException or ArgumentException){throw new InvalidDataException("PDF无法在本机转换，可能已加密或格式损坏；请检查文件。未上传。");}
    }
}
