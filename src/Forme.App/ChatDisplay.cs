using Forme.Core;

namespace Forme.App;

internal static class ChatDisplay
{
    private const string LocalRuleProgress = "正在执行本地规则，不发送AI请求。";
    private const string LocalRuleResult = "本地规则匹配 · 未联网";
    private static readonly string[] ServiceErrors=[
        "网络或 TLS 连接失败，请检查网络和服务地址。",
        "认证失败，请检查密钥或服务权限。","服务余额不足，请到服务商处检查。",
        "额度或频率受限，请检查服务商账户，稍后手动重试。",
        "服务返回重定向，为保护凭据未跟随，请核对地址。",
        "服务返回流式错误，请手动重试。","回复连接中断，已有内容已保留。",
        "服务未返回文本，请检查模型是否支持非思考文本对话。",
        "等待服务超时，未自动重试。","回复超时或停滞，已保留部分内容；可手动重试。",
        "服务返回格式不兼容，已有内容已保留。"];

    public static string RecordMessage(ChatMessage message)
    {
        if(message.Role=="user")
        {
            string content=message.Content;
            if(message.Status=="attachment"&&(content.EndsWith("；原内容未保存，后续消息不自动重传]",StringComparison.Ordinal)||content.EndsWith("；原内容未保存]",StringComparison.Ordinal)))
            {
                int marker=content.LastIndexOf("\n[本次附件：",StringComparison.Ordinal);
                if(marker>=0)return content[..marker].TrimEnd();
                if(content.StartsWith("[本次附件：",StringComparison.Ordinal))return "";
            }
            return content;
        }
        string stored=message.Content.Trim();
        if(message.Status=="error"&&(ServiceErrors.Contains(stored,StringComparer.Ordinal)||IsHttpError(stored)))return "";
        string visible=Message(message.Content);
        if(message.Status=="local")visible=string.Join("\n",visible.Split('\n').Where(line=>line.Trim() is not "本地动作"&&line.Trim()!=LocalRuleResult)).Trim();
        return visible;
    }

    private static bool IsHttpError(string text)
    {
        const string prefix="服务返回 HTTP ",suffix="，请检查连接或模型设置。";
        return text.StartsWith(prefix,StringComparison.Ordinal)&&text.EndsWith(suffix,StringComparison.Ordinal)&&text.Length>=prefix.Length+suffix.Length&&int.TryParse(text.AsSpan(prefix.Length,text.Length-prefix.Length-suffix.Length),out int code)&&code is >=100 and <=599;
    }

    public static string Message(string content)
    {
        if(content==LocalRuleProgress||content=="正在理解动作请求…")return "正在陪你互动…";
        int report=content.IndexOf(CompanionCommands.ReportMarker,StringComparison.Ordinal);
        string visible=CompanionCommands.VisibleText(report>=0?content[..report]:content).Trim();
        if(report<0)return visible;
        return string.Join("\n",visible.Split('\n').Where(line=>
            !line.Trim().Equals("本地动作",StringComparison.Ordinal)&&
            !line.Trim().Equals(LocalRuleResult,StringComparison.Ordinal))).Trim();
    }

    public static string Status(string status)
    {
        if(status is "回复完成。" or "回复完成；上下文已按预算裁剪。" or "回复完成。 上下文已按预算裁剪。")return "";
        if(status.Contains(LocalRuleProgress,StringComparison.Ordinal))return "正在陪你互动…";
        int marker=status.IndexOf(CompanionCommands.ReportMarker,StringComparison.Ordinal);
        if(marker>=0)
        {
            string report=status[(marker+CompanionCommands.ReportMarker.Length)..];
            if(report.Contains("对应动作权限未开启",StringComparison.Ordinal))return "没有执行这项动作，请先在设置中开启对应权限。";
            if(report.Contains("超过30秒",StringComparison.Ordinal))return "动作超过时限，已停止。";
            if(report.Contains("打断",StringComparison.Ordinal))return "动作被打断，后续步骤已停止。";
            if(report.Contains("已停止",StringComparison.Ordinal)||report.Contains("已取消",StringComparison.Ordinal))return "伙伴动作已停止。";
            const string failed="未执行：";
            if(report.Contains(failed,StringComparison.Ordinal))return "伙伴动作没有完成："+report[(report.IndexOf(failed,StringComparison.Ordinal)+failed.Length)..].Trim();
            return "已处理伙伴动作请求。";
        }
        if(status.Contains("本地规则匹配",StringComparison.Ordinal)||status.Contains("本地动作",StringComparison.Ordinal))return "伙伴动作已处理。";
        return status;
    }
}
