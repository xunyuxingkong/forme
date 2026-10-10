using System.Text.RegularExpressions;

namespace Forme.Core;

public static class ConversationText
{
    public static string Visible(string text)
    {
        string visible=CompanionCommands.VisibleText(text);
        // A normal chat turn has no executor. Do not display explicit virtual-body completion claims.
        if(text.Contains(CompanionCommands.Marker,StringComparison.Ordinal)||Regex.IsMatch(visible,"(?:我|伙伴|宠物)(?:已经|已|正在|这就|马上)(?:到(?:了)?(?:池塘|水边|书架|窗边|户外|小屋)|走到|跑到|去(?:池塘|水边|书架|窗边)|跳(?:舞|完)|切换|打开(?:灯|壁炉)|关(?:灯|闭)|执行)",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50)))
            return "本次只进行了对话，没有执行伙伴动作。需要操作时，请直接提出明确动作请求。";
        return visible;
    }
}
