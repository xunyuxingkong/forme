using Forme.Core;
using System.Windows;
using System.Windows.Controls;
namespace Forme.App;

internal sealed partial class MainWindow
{
    private Action? _lifeRefresh;
    private void DetachCompanionLifePage(){if(_lifeRefresh is not null)_c.Tick-=_lifeRefresh;_lifeRefresh=null;}
    private UIElement CompanionLifePage()
    {
        var p=_c.Preferences;var data=_c.Store.CompanionLife();var status=Ui.Text(_c.CompanionLifeStatus,13,Ui.Sage);
        _lifeRefresh=()=>status.Text=_c.CompanionLifeStatus;_c.Tick+=_lifeRefresh;
        var game=Ui.Check("分享当前游戏状态（绑定当前服务）",p.ShareGameState&&p.GameStateEndpoint==p.Endpoint);
        var sharePlay=Ui.Check("允许 AI 合作藏物与纸船建议（仅点击时发送本局公开线索和候选模拟）",p.SharePlayState);
        var remember=Ui.Check("在本机记录共同游戏经历（最多200条）",p.RememberPlayExperiences);
        var share=Ui.Check("让AI参考我启用的游戏经历、偏好与示范组合",p.ShareExperiences);
        var invitations=Ui.Check("允许轻量邀请（可拒绝、今天不再邀请）",p.AllowAutonomyInvitations);
        var pet=Ui.Check("允许伙伴动作",p.AllowPetControl);var scene=Ui.Check("允许场景物件互动",p.AllowSceneControl);var playGrant=Ui.Check("允许玩法",p.AllowPlayControl);
        var view=Ui.Select(new[]{"小屋/户外伙伴","桌面伙伴"},p.AutonomyView==CompanionView.Desktop?"桌面伙伴":"小屋/户外伙伴","自主生活主体");
        var daily=Ui.Input(p.AutonomyDailyLimit.ToString(),max:3,automationName:"每日自主调用额度");
        var favorites=new[]{"不固定偏好","走一小段","跑一小段","阅读","看小鱼","休息"};var ids=new[]{"","walk","run","use-book","use-fish","use-sleep"};
        int preferred=Array.IndexOf(ids,data.FavoriteActivity);var favorite=Ui.Select(favorites,favorites[Math.Max(0,preferred)],"伙伴活动偏好");
        void Save()
        {
            if(!int.TryParse(daily.Text,out int limit)||limit is <1 or >100)throw new InvalidDataException("每日额度为1–100次。");
            _c.SavePreferences(_c.Preferences with{ShareGameState=game.IsChecked==true,GameStateEndpoint=game.IsChecked==true?p.Endpoint:"",SharePlayState=sharePlay.IsChecked==true,
                RememberPlayExperiences=remember.IsChecked==true,ShareExperiences=share.IsChecked==true,
                AllowAutonomyInvitations=invitations.IsChecked==true,AutonomyView=view.SelectedIndex==1?CompanionView.Desktop:CompanionView.Room,
                AutonomyDailyLimit=limit,AllowPetControl=pet.IsChecked==true,AllowSceneControl=scene.IsChecked==true,AllowPlayControl=playGrant.IsChecked==true});
            var current=_c.Store.CompanionLife();_c.Store.SaveCompanionLife(current with{FavoriteActivity=ids[favorite.SelectedIndex]});
            Toast("设置与偏好已保存；没有后台调用。");Navigate("companion");
        }
        var panel=Ui.Stack(Ui.Text("伙伴生活与共同经历",24,null,true),status,
            Ui.Text("AI负责选择与策略，本机负责真实执行。外形与经历分别保存，换模型不清空这些记录。",12,Ui.Muted),
            Ui.Card(Ui.Stack(Ui.Text("分享、权限与偏好",18,null,true),game,sharePlay,Ui.Text("合作藏物会发送家具候选区域、已检查项和你输入的线索；纸船建议会发送目标码头、叶片布局和本机模拟候选结果。只有你点击合作按钮才会发送，藏物答案保留在本机；关闭此项或游戏状态分享会禁止 AI 合作玩法。",12,Ui.Muted),remember,share,invitations,pet,scene,playGrant,
                Ui.Text("自主生活主体"),view,Ui.Text("活动偏好"),favorite,Ui.Text("每日自动调用上限（滚动24小时）"),daily,
                Ui.Text("后台最多6次/小时，新活动至少间隔10分钟；隐藏、专注、锁屏、安静与用户聊天时暂停。取消/失败计次，连续两次失败暂停30分钟。额度不是金额上限。",12,Ui.Muted),
                Ui.Button("保存伙伴生活设置",Save,true))),
            Ui.Card(Ui.Stack(Ui.Text(p.AutonomyEnabled?"后台自主生活：已开启":"后台自主生活：关闭",17,null,true),
                Ui.Text("验证会发送一次当前游戏状态并实际运行返回的短活动，伙伴可能移动；请求可能收费。只有当前接口在15秒内返回合法选择，后台自主才会启用。不会自动更换模型或重试。",12,Ui.Muted),
                Ui.Row(Ui.AsyncButton("运行一次并验证后启用",async()=>{
                    await _c.ChooseActivity(_c.Preferences.AutonomyView,"选择一个适合当前环境的短活动。");
                    if(!_c.AutonomyVerified())throw new OperationFailureException("当前接口未通过响应验证，自主生活保持关闭。");
                    _c.SavePreferences(_c.Preferences with{AutonomyEnabled=true});Toast("自主生活已启用；首次后台检查至少等待一分钟。");Navigate("companion");
                },true),Ui.Button("暂停自主生活",()=>{_c.NotifyActivityInteraction();_c.SavePreferences(_c.Preferences with{AutonomyEnabled=false});Navigate("companion");})))));
        if(_c.CompanionInvitation is {} invite)
            panel.Children.Add(Ui.Card(Ui.Stack(Ui.Text(invite,15,null,true),Ui.Row(
                Ui.AsyncButton("一起玩",async()=>{if(_c.InvitationIsWorkBreak){_c.ClearInvitation();Navigate("relax");}else await _c.AcceptInvitation();},true),
                Ui.Button("这次不了",()=>_c.DeclineInvitation(false)),Ui.Button("今天不再邀请",()=>_c.DeclineInvitation(true))))));
        panel.Children.Add(Ui.Card(Ui.Stack(Ui.Text("一起玩",18,null,true),Ui.Row(Ui.Button("伙伴藏物",()=>Navigate("hide")),Ui.Button("玩纸船",()=>Navigate("boat"))),
            Ui.Text("本机裁判藏物答案和纸船真实结果。开启合作玩法分享后，可在藏物页请求AI规划搜索顺序，或在纸船页请求AI根据本机模拟给出布置建议；发送前均需主动点击。",12,Ui.Muted))));
        var demoView=Ui.Select(new[]{"小屋/户外","桌面"},"小屋/户外","示范主体");
        var option=new ComboBox{MinHeight=36,Margin=new Thickness(0,0,0,8)};
        void Choices(){option.Items.Clear();var v=demoView.SelectedIndex==1?CompanionView.Desktop:CompanionView.Room;
            foreach(var o in _c.ActivityWorld?.Observe(v).Options.Where(x=>x.Steps.All(s=>s.Action is "go" or "interact" or "stroll" or "dance"))??[])
                option.Items.Add(new ComboBoxItem{Content=o.Title,Tag=o.Id});if(option.Items.Count>0)option.SelectedIndex=0;}
        demoView.SelectionChanged+=(_,_)=>Choices();Choices();
        var title=Ui.Input("",max:40,automationName:"示范组合名称");
        panel.Children.Add(Ui.Card(Ui.Stack(Ui.Text("教会伙伴一个动作组合",18,null,true),
            Ui.Text("每一步先在本机真实完成，最多3步。组合可离线重放；只有开启经历分享才交给AI选择。",12,Ui.Muted),demoView,option,
            Ui.Text("已示范 "+_c.DemonstrationSteps.Count+" 步"),Ui.Row(Ui.AsyncButton("示范这一步",async()=>{
                if(option.SelectedItem is not ComboBoxItem selected)throw new OperationFailureException("当前没有可示范动作。");
                await _c.Demonstrate(demoView.SelectedIndex==1?CompanionView.Desktop:CompanionView.Room,(string)selected.Tag);
            }),Ui.Button("清空未保存示范",()=>{_c.DemonstrationSteps.Clear();Navigate("companion");})),title,
            Ui.Button("保存示范组合",()=>{_c.SaveDemonstration(title.Text);Navigate("companion");},true))));
        foreach(var template in data.Templates)
            panel.Children.Add(Ui.Card(Ui.Stack(Ui.Text(template.Title,16,null,true),Ui.Text(template.View==CompanionView.Desktop?"桌面组合":"小屋/户外组合",12,Ui.Muted),
                Ui.Row(Ui.AsyncButton("本地重放",()=>_c.PlayTemplate(template.Id)),Ui.Button("删除组合",()=>{_c.NotifyActivityInteraction();_c.Store.DeleteDemonstration(template.Id);Navigate("companion");})))));
        panel.Children.Add(Ui.Text("共同经历 · "+data.Experiences.Count+" 条（显示最近30条）",18,null,true));
        foreach(var item in data.Experiences.TakeLast(30).Reverse())
            panel.Children.Add(Ui.Card(Ui.Stack(Ui.Text(ExperienceTitle(item),15),Ui.Text(item.Created.ToLocalTime().ToString("MM-dd HH:mm"),11,Ui.Muted),
                Ui.Row(Ui.Button(item.Enabled?"停用这段经历":"启用这段经历",()=>{_c.NotifyActivityInteraction();var current=_c.Store.CompanionLife();current.Experiences.RemoveAll(x=>x.Id==item.Id);current.Experiences.Add(item with{Enabled=!item.Enabled});_c.Store.SaveCompanionLife(current);Navigate("companion");}),
                    Ui.Button("删除经历",()=>{_c.NotifyActivityInteraction();_c.Store.DeletePlayExperience(item.Id);Navigate("companion");})))));
        panel.Children.Add(Ui.Text("设置中的小屋导出/导入包含经历、偏好与示范组合；导入后分享和自动调用保持关闭。旧聊天或服务商已收到的内容需分别处理。",12,Ui.Muted));
        panel.Children.Add(Ui.Button("清空共同经历与示范",()=>{_c.NotifyActivityInteraction();_c.Store.ClearCompanionLife();Navigate("companion");}));
        return panel;
    }
    private static string ExperienceTitle(PlayExperience item)=>item.Kind switch{
        "hide"=>"合作藏物 · "+(item.Outcome=="found"?"找到了小球":"这次未找到"),
        "boat"=>"纸船旅行 · "+PaperBoat.DockName(item.Outcome),
        "demonstration"=>"完成了一个示范组合",
        "life"=>"体验了小屋生活事件",
        _=>"伙伴活动 · "+(item.Outcome=="completed"?"已完成":"已结束")
    };
}
