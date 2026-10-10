using Forme.Core;

namespace Forme.App;

internal static class ChatDisplay
{
    private const string LocalRuleProgress = "正在执行本地规则，不发送AI请求。";
    private const string LocalRuleResult = "本地规则匹配 · 未联网";

    public static string Message(string content)
    {
        if(content.Contains(LocalRuleProgress,StringComparison.Ordinal))return "正在陪你互动…";
        int report=content.IndexOf(CompanionCommands.ReportMarker,StringComparison.Ordinal);
        string visible=CompanionCommands.VisibleText(report>=0?content[..report]:content).Trim();
        if(report<0)return visible;
        return string.Join("\n",visible.Split('\n').Where(line=>
            !line.Trim().Equals("本地动作",StringComparison.Ordinal)&&
            !line.Trim().Equals(LocalRuleResult,StringComparison.Ordinal))).Trim();
    }

    public static string Status(string status)
    {
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
