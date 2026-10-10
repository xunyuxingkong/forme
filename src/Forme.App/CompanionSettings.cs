using Forme.Core;
using System.Windows;
using System.Windows.Controls;

namespace Forme.App;

internal sealed partial class MainWindow
{
    private UIElement PersonaSettings()
    {
        var p=_c.Preferences;var presets=new[]{"温柔朋友","专注搭子","故事伙伴","完全自定义"};var ids=new[]{"companion","focus","story","custom"};
        var preset=Ui.Select(presets,presets[Array.IndexOf(ids,p.CharacterPreset)],"AI 角色预设");
        var role=Ui.Input(p.RoleDescription,true,1200,"AI 角色设定");var style=Ui.Input(p.ReplyStyle,true,400,"语气与回复偏好");
        var memory=Ui.Check("发送我已启用、且属于当前服务商的记忆与会话摘要",p.MemoryEnabled);
        var pet=Ui.Check("允许 AI 执行宠物动作（舞蹈、待机、小屋走动与去物件旁、模型切换、叫回）",p.AllowPetControl);
        var scene=Ui.Check("允许 AI 改变小屋（切换场景、天气、灯、壁炉和物件互动）",p.AllowSceneControl);
        var play=Ui.Check("允许 AI 调用玩法（组合事件、藏物、抛球、纸船与旅行命名）",p.AllowPlayControl);
        var layout=Ui.Check("允许 AI 预览家具布置（发送家具种类、位置和编号，手动保存）",p.AllowLayoutPreview);
        foreach(var check in new[]{memory,pet,scene,play,layout}){var label=Ui.Text(check.Content.ToString()!,13);label.Margin=new Thickness(0);label.MaxWidth=330;check.Content=label;}
        var budget=Ui.Select(new[]{"2048 · 节省","4096 · 标准","8192 · 较长"},p.ContextBudget==2048?"2048 · 节省":p.ContextBudget==8192?"8192 · 较长":"4096 · 标准","上下文预算");
        var output=Ui.Input(p.MaxReplyTokens.ToString(),max:4,automationName:"最大输出 tokens");
        var temperature=Ui.Select(new[]{"0.3 · 稳定","0.7 · 自然","1.0 · 活泼"},p.Temperature<.5?"0.3 · 稳定":p.Temperature>.85?"1.0 · 活泼":"0.7 · 自然","回复随机性");
        return Ui.Card(Ui.Stack(Ui.Text("伙伴角色、记忆与动作权限",18,null,true),Ui.Text("这些设定在你发送消息时才提供给服务商。更改设置不发起 AI 请求。",12,Ui.Muted),Ui.Text("角色预设"),preset,Ui.Text("补充角色设定（身份、性格、背景和称呼）"),role,Ui.Text("语气和回答习惯"),style,memory,pet,scene,play,layout,Ui.Text("权限默认关闭。开启玩法会发送可用事件编号、藏物公开线索和纸船配置／当前旅行名字；不发送藏物答案、收藏历史或图鉴。开启家具预览会发送最多24件家具的种类、坐标和编号。每次最多3步，整组最多30秒；随时停止。家具只能生成草稿，保存由你操作；旅行命名不会自动收藏。AI不能运行程序、读取屏幕或文件、修改记忆。",12,Ui.Muted),ActionRuleSettings(),new Expander{Header="费用与输出控制",Content=Ui.Stack(Ui.Text("上下文保守预算（UTF-8 字节估算，非账单 token 数）",11,Ui.Muted),budget,Ui.Text("最大输出 · 128–1024 tokens"),output,Ui.Text("回复随机性"),temperature)},Ui.Row(Ui.Button("保存角色与权限",()=>
        {
            if(!int.TryParse(output.Text,out int tokens))throw new InvalidDataException("请输入有效输出上限。");
            _c.SavePreferences(_c.Preferences with{CharacterPreset=ids[preset.SelectedIndex],RoleDescription=role.Text.Trim(),ReplyStyle=style.Text.Trim(),MemoryEnabled=memory.IsChecked==true,AllowPetControl=pet.IsChecked==true,AllowSceneControl=scene.IsChecked==true,AllowPlayControl=play.IsChecked==true,AllowLayoutPreview=layout.IsChecked==true,ContextBudget=budget.SelectedIndex==0?2048:budget.SelectedIndex==2?8192:4096,MaxReplyTokens=tokens,Temperature=temperature.SelectedIndex==0?.3:temperature.SelectedIndex==2?1:.7});Toast("角色与权限已保存，下次消息使用新设定。关闭的权限会立即阻止动作执行。");
        },true),Ui.Button("管理长期记忆",()=>Navigate("memory")))));
    }
    private UIElement MemorySettings()
    {
        string provider=AiClient.ProviderIdentity(_c.Preferences.Endpoint);string? editId=null;DateTimeOffset created=DateTimeOffset.Now;
        var title=Ui.Input("",max:60,automationName:"记忆标题");var detail=Ui.Input(_c.MemoryDraft,true,400,"记忆内容");var enabled=Ui.Check("启用这条记忆",true);
        _c.MemoryDraft="";
        var content=Ui.Stack(Ui.Text("由你决定，记住什么",24,null,true),Ui.Text($"最多30条。当前服务：{provider}。记忆不会自动从聊天、心情或任务中提取；只有启用角色设置中的记忆开关，才会随聊天发送。每次最多8段摘要/记忆，并受总预算限制。",12,Ui.Muted));
        content.Children.Add(Ui.Card(Ui.Stack(Ui.Text("新建 / 编辑记忆",17,null,true),Ui.Text("标题"),title,Ui.Text("内容（最多400字）"),detail,enabled,Ui.Row(Ui.Button("保存到当前服务商",()=>
        {
            var now=DateTimeOffset.Now;_c.Store.SaveMemory(new(editId??Guid.NewGuid().ToString("N"),title.Text.Trim(),detail.Text.Trim(),provider,enabled.IsChecked==true,editId is null?now:created,now));Navigate("memory");Toast("记忆已保存到本机；没有发起 AI 请求。");
        },true),Ui.Button("取消编辑",()=>Navigate("memory"))))));
        foreach(var item in _c.Store.Memories())
        {
            content.Children.Add(Ui.Card(Ui.Stack(Ui.Text(item.Title,16,null,true),Ui.Text(item.Content,13),Ui.Text($"{item.Provider} · {(item.Enabled?"启用":"停用")} · {(item.Provider==provider?"可用于当前服务":"不会发送到当前服务")}",11,Ui.Muted),Ui.Row(Ui.Button("编辑 / 复制到当前服务",()=>{editId=item.Provider==provider?item.Id:null;created=item.Created;title.Text=item.Title;detail.Text=item.Content;enabled.IsChecked=item.Enabled;detail.Focus();}),Ui.Button(item.Enabled?"停用":"启用",()=>{_c.Store.SaveMemory(item with{Enabled=!item.Enabled,Updated=DateTimeOffset.Now});Navigate("memory");}),Ui.Button("删除",()=>{if(Ui.Confirm("删除这条本机记忆及含有它的应用恢复备份？服务商和外部导出副本不在删除范围内。")){_c.Store.DeleteMemory(item.Id);Navigate("memory");}})))));
        }
        content.Children.Add(Ui.Row(Ui.Button("返回聊天",()=>Navigate("chat")),Ui.Button("角色和权限设置",()=>Navigate("settings"))));return content;
    }
    private UIElement WorldControls()
    {
        var weather=Ui.Select(new[]{"晴朗","雨天 · 静态雨景","雪天 · 静态雪景"},_c.Preferences.Weather=="rain"?"雨天 · 静态雨景":_c.Preferences.Weather=="snow"?"雪天 · 静态雪景":"晴朗","场景天气");
        var lamp=Ui.Check("点亮落地灯",_c.Preferences.RoomLamp);var fire=Ui.Check("点亮壁炉",_c.Preferences.Fireplace);
        var controls=Ui.Stack(Ui.Text("小屋和花园里的生活",17,null,true),Ui.Text("点击书架、鱼缸、零食碗、睡垫、灯或壁炉；户外可到池塘、野餐毯、凉亭和风铃旁互动。天气是静态布景，避免常驻粒子开销。",12,Ui.Muted),weather,lamp,fire,Ui.Button("保存场景氛围",()=>{var next=_c.Preferences with{Weather=weather.SelectedIndex==1?"rain":weather.SelectedIndex==2?"snow":"clear",RoomLamp=lamp.IsChecked==true,Fireplace=fire.IsChecked==true};_c.SavePreferences(next);if(_room.Preview is {} preview)_room.Preview=preview with{Weather=next.Weather,RoomLamp=next.RoomLamp,Fireplace=next.Fireplace};Toast("场景氛围已保存。");},true));
        controls.Children.Add(Ui.Row(Ui.Button("小屋",()=>_room.SwitchScene(false)),Ui.Button("户外",()=>_room.SwitchScene(true))));
        foreach(var (id,name) in new[]{("ball","抛小球"),("feed","喂零食"),("book","一起读书"),("fish","看小鱼"),("sleep","去小窝"),("pond","看池塘"),("picnic","去野餐"),("rest","坐凉亭"),("bell","听风铃"),("water","浇水")})
            controls.Children.Add(Ui.Button(name,()=>{if(ActualWidth<940){_compactScene=true;ApplyLayout();Dispatcher.BeginInvoke(new Action(()=>Toast(_room.Interact(id))));}else Toast(_room.Interact(id));}));
        return Ui.Card(controls);
    }
}
