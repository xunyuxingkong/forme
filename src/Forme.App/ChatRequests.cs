namespace Forme.App;

// Both chat surfaces must enforce the same consent before calling the shared controller.
internal static class ChatRequests
{
    public static void Preview(Controller c,System.Windows.Window owner)
    {
        if(c.LocalRulePreview() is {} local){var dialog=new System.Windows.Window{Title="本地动作预览 · 不联网",Owner=owner,Width=560,Height=300,WindowStartupLocation=System.Windows.WindowStartupLocation.CenterOwner,Content=Ui.Card(Ui.Text(local,13))};dialog.ShowDialog();return;}
        var history=c.Session is {} session?c.Store.ContextMessages(session.Id):[];
        bool trimmed=false;
        var turns=c.Attachments.Count==0&&Forme.Core.IntentResolver.LooksLikeRequest(c.Draft.Trim(),c.Preferences.PetName)?Forme.Core.AiIntentProtocol.BuildContext(c.Preferences,c.Draft.Trim()):Forme.Core.AiClient.BuildContext(c.Preferences,history,c.Draft.Trim(),out trimmed,c.Preferences.MemoryEnabled?c.Store.Memories():null,c.Preferences.MemoryEnabled&&c.Session is {} s?c.Store.Note(s.Id):null,attachments:c.Attachments.ToArray());
        var text=Ui.Input(string.Join("\n\n",turns.Select(x=>$"[{x.Role}]\n{x.Preview}")),true,100000,"实际发送上下文预览");text.IsReadOnly=true;text.MaxHeight=420;text.VerticalScrollBarVisibility=System.Windows.Controls.ScrollBarVisibility.Auto;
        var preview=new System.Windows.Window{Title="本次发送内容 · 尚未联网",Owner=owner,Width=680,Height=600,WindowStartupLocation=System.Windows.WindowStartupLocation.CenterOwner,Content=Ui.Card(Ui.Stack(Ui.Text($"文字估算 {turns.Sum(Forme.Core.AiClient.Estimate)} / {c.Preferences.ContextBudget} 字节预算 · 输出上限 {c.Preferences.MaxReplyTokens} tokens",13),Ui.Text("图片/PDF用量由服务商另计，不包含在文字预算中。附件只随本次发送，原内容不存聊天记录。",12,Ui.Muted),Ui.Text(trimmed?"历史或记忆已按预算裁剪。":"已包含预算内的上下文。",12,Ui.Muted),text))};preview.ShowDialog();
    }
    public static async Task Send(Controller c)
    {
        if(c.Busy)return;
        if(await c.TrySendLocal())return;
        if(!c.Secrets.Exists)throw new Forme.Core.OperationFailureException("此表达未能在本地完整理解，AI识别或自由对话需要先配置AI密钥。");
        if(c.Session is { } session&&!Forme.Core.AiClient.SameEndpoint(session.Endpoint,c.Preferences.Endpoint))
        {
            if(!Ui.Confirm("此会话曾发送给另一地址。继续将把当前消息和有限历史发送给新服务："+c.Preferences.Endpoint+"。确认转移？"))return;
            c.Store.RebindSession(session.Id,c.Preferences.Endpoint);c.SelectSession(session with{Endpoint=c.Preferences.Endpoint});
        }
        if(c.Store.Get<string>("ai-consent")!=c.Preferences.Endpoint)
        {
            if(!Ui.Confirm("自由对话发送消息、你本次选择的附件、有限会话上下文、角色设定和已启用的确认记忆；动作识别只发送本次输入和固定意图说明，不发送历史、记忆或场景。服务地址："+c.Preferences.Endpoint+"。图片/PDF也可能收费；删除本地记录无法删除服务商留存。确认使用此服务？"))return;
            c.Store.Set("ai-consent",c.Preferences.Endpoint);
        }
        await c.Send();
    }
}
