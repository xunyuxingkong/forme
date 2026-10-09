namespace Forme.App;

// Both chat surfaces must enforce the same consent before calling the shared controller.
internal static class ChatRequests
{
    public static async Task Send(Controller c)
    {
        if(c.Busy)return;
        if(c.Session is { } session&&session.Endpoint!=c.Preferences.Endpoint)
        {
            if(!Ui.Confirm("此会话曾发送给另一地址。继续将把当前消息和有限历史发送给新服务："+c.Preferences.Endpoint+"。确认转移？"))return;
            c.Store.RebindSession(session.Id,c.Preferences.Endpoint);c.SelectSession(session with{Endpoint=c.Preferences.Endpoint});
        }
        if(c.Store.Get<string>("ai-consent")!=c.Preferences.Endpoint)
        {
            if(!Ui.Confirm("消息、有限会话上下文、称呼和回复偏好将发送至 "+c.Preferences.Endpoint+"。调用可能收费；删除本地记录无法删除服务商留存。确认使用此服务？"))return;
            c.Store.Set("ai-consent",c.Preferences.Endpoint);
        }
        await c.Send();
    }
}
