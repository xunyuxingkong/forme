using Forme.Core;
using HtmlAgilityPack;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;

namespace Forme.App;

internal static class WebProjectSourceReader
{
    private const int MaximumBytes=1_000_000;
    private static readonly TimeSpan Timeout=TimeSpan.FromSeconds(20);

    internal static async Task<ProjectSource> ReadAsync(string address,CancellationToken cancellationToken)
    {
        if(!Uri.TryCreate(address.Trim(),UriKind.Absolute,out var current))throw new InvalidDataException("请输入完整网页地址（http:// 或 https://）。");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);deadline.CancelAfter(Timeout);
        using var handler=new SocketsHttpHandler{AllowAutoRedirect=false,UseProxy=false,AutomaticDecompression=DecompressionMethods.All,ConnectCallback=ConnectPublicHostAsync,ConnectTimeout=TimeSpan.FromSeconds(8),PooledConnectionLifetime=TimeSpan.Zero};
        using var client=new HttpClient(handler){Timeout=Timeout};
        for(int redirects=0;redirects<=4;redirects++)
        {
            ValidateAddress(current);
            using var request=new HttpRequestMessage(HttpMethod.Get,current);
            request.Headers.UserAgent.ParseAdd("Forme-WebSource/1.0");
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token).ConfigureAwait(false);
            if(IsRedirect(response.StatusCode))
            {
                if(redirects==4||response.Headers.Location is null)throw new InvalidDataException("网页重定向次数过多或目标无效。");
                try{current=response.Headers.Location.IsAbsoluteUri?response.Headers.Location:new Uri(current,response.Headers.Location);}
                catch(UriFormatException ex){throw new InvalidDataException("网页重定向目标无效。",ex);}
                continue;
            }
            response.EnsureSuccessStatusCode();
            string mediaType=response.Content.Headers.ContentType?.MediaType??"";
            if(!mediaType.Equals("text/html",StringComparison.OrdinalIgnoreCase)&&!mediaType.Equals("application/xhtml+xml",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("指定网页没有返回 HTML 正文。");
            if(response.Content.Headers.ContentLength>MaximumBytes)throw new InvalidDataException("网页超过 1 MB 读取上限。");
            byte[] bytes=await ReadLimitedAsync(await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false),deadline.Token).ConfigureAwait(false);
            string html=Decode(bytes,response.Content.Headers.ContentType?.CharSet);
            return Extract(html,current);
        }
        throw new InvalidDataException("无法读取指定网页。");
    }

    private static ProjectSource Extract(string html,Uri finalAddress)
    {
        var document=new HtmlDocument{OptionFixNestedTags=true,OptionAutoCloseOnEnd=true};document.LoadHtml(html);
        foreach(var node in document.DocumentNode.SelectNodes("//script|//style|//noscript|//nav|//header|//footer|//aside|//form|//button|//svg|//canvas|//iframe|//template|//*[@hidden or @aria-hidden='true']")??Enumerable.Empty<HtmlNode>())node.Remove();
        string title=HtmlEntity.DeEntitize(document.DocumentNode.SelectSingleNode("//title")?.InnerText??"").Trim();
        var root=document.DocumentNode.SelectSingleNode("//article")??document.DocumentNode.SelectSingleNode("//main")??document.DocumentNode.SelectSingleNode("//body")??document.DocumentNode;
        var blocks=root.SelectNodes(".//h1|.//h2|.//h3|.//h4|.//h5|.//h6|.//p|.//li[not(descendant::p)]|.//blockquote[not(descendant::p)]|.//pre")??new HtmlNodeCollection(root);
        var paragraphs=blocks.Select(n=>HtmlEntity.DeEntitize(n.InnerText).Replace('\u00a0',' ')).Select(x=>string.Join(' ',x.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries))).Where(x=>x.Length>0).ToArray();
        if(paragraphs.Length==0)
        {
            string fallback=HtmlEntity.DeEntitize(root.InnerText).Replace('\u00a0',' ');
            paragraphs=fallback.Split('\n').Select(x=>string.Join(' ',x.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries))).Where(x=>x.Length>0).ToArray();
        }
        if(paragraphs.Length==0)throw new InvalidDataException("网页没有可提取的静态正文；动态加载页面暂不支持。");
        return ProjectSource.FromWebPage(title,finalAddress.ToString(),string.Join('\n',paragraphs));
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream stream,CancellationToken cancellationToken)
    {
        using var inputStream=stream;using var output=new MemoryStream();var buffer=new byte[32*1024];int read;
        while((read=await inputStream.ReadAsync(buffer.AsMemory(),cancellationToken).ConfigureAwait(false))>0)
        {
            if(output.Length+read>MaximumBytes)throw new InvalidDataException("网页超过 1 MB 读取上限。");
            await output.WriteAsync(buffer.AsMemory(0,read),cancellationToken).ConfigureAwait(false);
        }
        return output.ToArray();
    }

    private static string Decode(byte[] bytes,string? charset)
    {
        Encoding encoding=Encoding.UTF8;
        if(!string.IsNullOrWhiteSpace(charset))try{encoding=Encoding.GetEncoding(charset.Trim(' ','\"','\''));}catch(ArgumentException){}catch(NotSupportedException){}
        using var input=new MemoryStream(bytes);using var reader=new StreamReader(input,encoding,true);return reader.ReadToEnd();
    }

    private static bool IsRedirect(HttpStatusCode code)=>code is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static void ValidateAddress(Uri address)
    {
        if(address.Scheme is not ("http" or "https")||!string.IsNullOrEmpty(address.UserInfo)||string.IsNullOrWhiteSpace(address.Host)||address.IsLoopback)throw new InvalidDataException("仅支持无账号信息的公网 HTTP(S) 网页。");
        if(IPAddress.TryParse(address.Host,out var literal)&&!IsPublicAddress(literal))throw new InvalidDataException("网页地址指向本机、内网或保留网络，已拒绝读取。");
    }

    private static async ValueTask<Stream> ConnectPublicHostAsync(SocketsHttpConnectionContext context,CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        if(IPAddress.TryParse(context.DnsEndPoint.Host,out var literal))addresses=[literal];
        else try{addresses=await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host,cancellationToken).ConfigureAwait(false);}
        catch(SocketException ex){throw new HttpRequestException("无法解析网页主机地址。",ex);}
        if(addresses.Length==0||addresses.Any(x=>!IsPublicAddress(x)))throw new HttpRequestException("网页主机解析到非公网地址，已拒绝连接。");
        Exception? last=null;
        foreach(var address in addresses)
        {
            var socket=new Socket(address.AddressFamily,SocketType.Stream,ProtocolType.Tcp);
            try{await socket.ConnectAsync(new IPEndPoint(address,context.DnsEndPoint.Port),cancellationToken).ConfigureAwait(false);return new NetworkStream(socket,true);}
            catch(Exception ex) when(ex is SocketException or OperationCanceledException){socket.Dispose();last=ex;if(ex is OperationCanceledException)throw;}
        }
        throw new HttpRequestException("无法连接网页主机。",last);
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if(address.IsIPv4MappedToIPv6)address=address.MapToIPv4();
        var bytes=address.GetAddressBytes();
        if(address.AddressFamily==AddressFamily.InterNetwork)
        {
            return !(bytes[0]==0||bytes[0]==10||bytes[0]==127||bytes[0]>=224||bytes[0]==169&&bytes[1]==254||bytes[0]==172&&(bytes[1]&0xf0)==16||bytes[0]==192&&bytes[1]==168||bytes[0]==100&&(bytes[1]&0xc0)==64||bytes[0]==192&&bytes[1]==0&&bytes[2]==0||bytes[0]==192&&bytes[1]==0&&bytes[2]==2||bytes[0]==192&&bytes[1]==88&&bytes[2]==99||bytes[0]==198&&(bytes[1]==18||bytes[1]==19||bytes[1]==51&&bytes[2]==100)||bytes[0]==203&&bytes[1]==0&&bytes[2]==113);
        }
        if(address.AddressFamily!=AddressFamily.InterNetworkV6||address.ScopeId!=0)return false;
        bool globalUnicast=(bytes[0]&0xe0)==0x20;
        bool documentation=bytes[0]==0x20&&bytes[1]==0x01&&bytes[2]==0x0d&&bytes[3]==0xb8;
        bool transition=bytes[0]==0x20&&bytes[1]==0x01&&(bytes[2]&0xfe)==0||bytes[0]==0x20&&bytes[1]==0x02;
        bool documentation3fff=bytes[0]==0x3f&&bytes[1]==0xff;
        return globalUnicast&&!documentation&&!transition&&!documentation3fff;
    }
}
