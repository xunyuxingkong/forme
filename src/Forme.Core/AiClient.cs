using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Forme.Core;

public sealed record AiTurn(string Role, string Content);
public sealed record AiResult(string Content, bool LengthLimited, bool HistoryTrimmed);
public sealed record AiLimits(TimeSpan First, TimeSpan Idle, TimeSpan Total)
{
    public static AiLimits Default { get; } = new(TimeSpan.FromSeconds(30),TimeSpan.FromSeconds(20),TimeSpan.FromSeconds(90));
}
public sealed class AiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1,1);
    private readonly AiLimits _limits;
    public AiClient(HttpMessageHandler? handler = null, AiLimits? limits = null)
    {
        _http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        _http.Timeout = Timeout.InfiniteTimeSpan; _limits=limits??AiLimits.Default;
    }
    public static Uri ValidateEndpoint(string endpoint)
    {
        if(!Uri.TryCreate(endpoint,UriKind.Absolute,out var uri) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            !(uri.Scheme=="https" || uri.Scheme=="http" && uri.IsLoopback))
            throw new InvalidDataException("服务地址需要 HTTPS；仅本机地址允许 HTTP。不要在地址中填写密钥或查询参数。");
        return uri;
    }
    // UTF-8 byte count is deliberately conservative for byte-based text tokenizers.
    // Includes a framing allowance per turn; not an exact provider billing token count.
    public static int Estimate(AiTurn turn) => Encoding.UTF8.GetByteCount(turn.Content)+32;
    public static List<AiTurn> BuildContext(Preferences p, IReadOnlyList<ChatMessage> history, string input, out bool trimmed)
    {
        if(input.EnumerateRunes().Count()>2000 || string.IsNullOrWhiteSpace(input)) throw new InvalidDataException("请输入内容，最多 2,000 个字符。");
        var system = new AiTurn("system",$"你是 Windows 桌面伙伴 {p.PetName}。用户称呼：{p.UserName}。风格：{p.ReplyStyle}。中文简短回应，优先在约150字内回答。不要责备、施压、诊断或假装执行工具。你没有文件、系统、提醒或屏幕访问能力。不虚构用户的情绪和经历。用户表达明显自伤危机时认真回应，鼓励联系现实中可信赖的人和当地紧急支持。不要用玩笑或奖励淡化危机。");
        var current = new AiTurn("user",input);
        int budget=4096-Estimate(system)-Estimate(current);
        if(budget<0) throw new InvalidDataException("当前内容超过保守上下文预算，请缩短消息或回复偏好。内容未发送。");
        // Complete exchanges only, so interrupted replies cannot produce orphaned context.
        var pairs = new List<List<AiTurn>>();
        for(int i=0;i<history.Count;i++)
        {
            if(history[i].Role!="user")continue;
            ChatMessage? reply=null;int next=i+1;
            for(;next<history.Count&&history[next].Role!="user";next++)
                if(history[next].Role=="assistant"&&history[next].Status=="complete")reply=history[next];
            if(reply is not null)pairs.Add([new("user",history[i].Content),new("assistant",reply.Content)]);
            i=next-1;
        }
        var selected=new List<AiTurn>(); int used=0; int kept=0;
        for(int i=pairs.Count-1;i>=0;i--)
        {
            int size=pairs[i].Sum(Estimate); if(used+size>budget) break;
            selected.InsertRange(0,pairs[i]); used+=size; kept++;
        }
        trimmed=kept<pairs.Count || history.Count>pairs.Count*2;
        selected.Insert(0,system); selected.Add(current); return selected;
    }
    public async Task<AiResult> SendAsync(Preferences settings, string key, List<AiTurn> turns, Action<string> progress, CancellationToken cancellation, bool trimmed=false)
    {
        if(!await _gate.WaitAsync(0,cancellation).ConfigureAwait(false)) throw new InvalidOperationException("已有请求正在处理，请稍后再试。");
        try
        {
            var baseUri=ValidateEndpoint(settings.Endpoint);
            if(string.IsNullOrWhiteSpace(key) || key.Contains('\r') || key.Contains('\n')) throw new InvalidDataException("请先填写有效密钥。");
            if(turns.Sum(Estimate)>4096) throw new InvalidDataException("上下文超过预算，未发送。");
            var uri=new Uri(baseUri.AbsoluteUri.TrimEnd('/')+"/chat/completions");
            var payload=new Dictionary<string,object> { ["model"]=settings.Model,["messages"]=turns.Select(t=>new {role=t.Role,content=t.Content}),["stream"]=true,["max_tokens"]=512 };
            if(baseUri.Host.Equals("api.deepseek.com",StringComparison.OrdinalIgnoreCase)) payload["thinking"]=new {type="disabled"};
            using var request=new HttpRequestMessage(HttpMethod.Post,uri);
            request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
            request.Content=new StringContent(JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json");
            using var total=CancellationTokenSource.CreateLinkedTokenSource(cancellation); total.CancelAfter(_limits.Total);
            using var inactivity=CancellationTokenSource.CreateLinkedTokenSource(total.Token); inactivity.CancelAfter(_limits.First);
            bool gotContent=false, length=false, done=false; var content=new StringBuilder();
            try
            {
                using var response=await _http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,inactivity.Token).ConfigureAwait(false);
                if(!response.IsSuccessStatusCode) throw new InvalidOperationException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden=>"认证失败，请检查密钥或服务权限。",
                    HttpStatusCode.PaymentRequired=>"服务余额不足，请到服务商处检查。",
                    HttpStatusCode.TooManyRequests=>"额度或频率受限，请检查服务商账户，稍后手动重试。",
                    _ when (int)response.StatusCode>=300 && (int)response.StatusCode<400=>"服务返回重定向，为保护凭据未跟随，请核对地址。",
                    _=>$"服务返回 HTTP {(int)response.StatusCode}，请检查连接或模型设置。"
                });
                using var stream=await response.Content.ReadAsStreamAsync(inactivity.Token).ConfigureAwait(false);
                using var reader=new StreamReader(stream,Encoding.UTF8);
                int received=0;
                while(await ReadBoundedLine(reader,inactivity.Token).ConfigureAwait(false) is { } line)
                {
                    received+=line.Length; if(received>256000) throw new InvalidDataException("服务响应过大，已停止接收。");
                    if(!line.StartsWith("data:",StringComparison.Ordinal)) continue;
                    var data=line[5..].Trim(); if(data=="[DONE]") {done=true;break;} if(data.Length==0) continue;
                    using var doc=JsonDocument.Parse(data);
                    if(doc.RootElement.TryGetProperty("error",out _)) throw new InvalidOperationException("服务返回流式错误，请手动重试。");
                    if(!doc.RootElement.TryGetProperty("choices",out var choices) || choices.GetArrayLength()==0) continue;
                    var choice=choices[0];
                    if(choice.TryGetProperty("delta",out var delta) && delta.TryGetProperty("content",out var c) && c.ValueKind==JsonValueKind.String)
                    {
                        var fragment=c.GetString();
                        if(!string.IsNullOrEmpty(fragment)) {gotContent=true;content.Append(fragment); if(content.Length>32000) throw new InvalidDataException("回复超出本地长度限制。"); progress(content.ToString()); inactivity.CancelAfter(_limits.Idle);}
                    }
                    if(choice.TryGetProperty("finish_reason",out var reason) && reason.ValueKind==JsonValueKind.String)
                    {length=reason.GetString()=="length";done=true;break;}
                }
                if(!done) throw new InvalidOperationException("回复连接中断，已有内容已保留。");
                if(!gotContent) throw new InvalidOperationException("服务未返回文本，请检查模型是否支持非思考文本对话。");
                return new(content.ToString(),length,trimmed);
            }
            catch(OperationCanceledException) when(!cancellation.IsCancellationRequested)
            {throw new TimeoutException(gotContent?"回复超时或停滞，已保留部分内容；可手动重试。":"等待服务超时，未自动重试。");}
            catch(HttpRequestException) {throw new InvalidOperationException("网络或 TLS 连接失败，请检查网络和服务地址。");}
            catch(JsonException) {throw new InvalidDataException("服务返回格式不兼容，已有内容已保留。");}
        }
        finally {_gate.Release();}
    }
    private static async Task<string?> ReadBoundedLine(StreamReader reader, CancellationToken token)
    {
        var result=new StringBuilder(); var buffer=new char[1];
        while(await reader.ReadAsync(buffer.AsMemory(),token).ConfigureAwait(false)>0)
        {
            if(buffer[0]=='\n') return result.ToString().TrimEnd('\r');
            result.Append(buffer[0]); if(result.Length>65536) throw new InvalidDataException("服务单行响应过大。");
        }
        return result.Length==0?null:result.ToString();
    }
    public void Dispose() {_http.Dispose();_gate.Dispose();}
}
