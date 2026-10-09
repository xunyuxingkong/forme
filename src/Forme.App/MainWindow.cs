using Forme.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Forme.App;

internal sealed partial class MainWindow : Window
{
    private readonly Controller _c;
    private readonly Action _settingsChanged;
    private readonly ContentControl _panel=new();
    private readonly TextBlock _status=Ui.Text("",12);
    private readonly TextBlock _petName=Ui.Text("",22,null,true);
    private readonly Room3DView _room;
    private readonly Grid _body=new();
    private readonly StackPanel _left=new();
    private readonly Button _sceneToggle;
    private bool _compactScene;
    private readonly TextBlock _toast=Ui.Text("",12,Ui.Sage);
    private readonly DispatcherTimer _toastTimer=new(){Interval=TimeSpan.FromSeconds(6)};
    private TextBlock? _clockText;
    private TextBlock? _liveText;
    private TextBlock? _chatState;
    private string _page="home";
    private int _focusOffset,_moodOffset,_chatOffset,_sessionOffset;
    private readonly Dictionary<string,Button> _navigation=new();
    private DispatcherTimer? _relaxTimer;
    private Pet3DView? _relaxPet;
    private bool _relaxFinished;
    public string CurrentPage=>_page;
    public MainWindow(Controller c,Action settingsChanged)
    {
        _c=c;_settingsChanged=settingsChanged;Title="Forme · 陪伴小屋";Width=Math.Min(1280,SystemParameters.WorkArea.Width-24);Height=Math.Min(850,SystemParameters.WorkArea.Height-24);MinWidth=Math.Min(650,SystemParameters.WorkArea.Width-24);MinHeight=Math.Min(480,SystemParameters.WorkArea.Height-24);WindowStartupLocation=WindowStartupLocation.CenterScreen;Background=Ui.Cream;
        var shell=new Grid();shell.RowDefinitions.Add(new(){Height=GridLength.Auto});shell.RowDefinitions.Add(new());shell.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new Grid{Margin=new Thickness(0,0,0,20)};header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        header.Children.Add(Ui.Stack(Ui.Text("FORME   /   陪伴小屋",11,Ui.Muted,true),_petName));
        var quiet=Ui.Button("安静模式",()=>c.SavePreferences(c.Preferences with{Quiet=!c.Preferences.Quiet}));quiet.ToolTip="关闭声音、主动招呼和自动动作；点击仍会回应";
        _sceneToggle=Ui.Button("查看3D小屋",()=>{_compactScene=!_compactScene;ApplyLayout();});_sceneToggle.Visibility=Visibility.Collapsed;
        var right=Ui.Stack(Ui.Row(quiet,Ui.Button("设置",()=>Navigate("settings")),_sceneToggle),_status);Grid.SetColumn(right,1);header.Children.Add(right);shell.Children.Add(header);
        _body.ColumnDefinitions.Add(new(){Width=new GridLength(0.56,GridUnitType.Star)});_body.ColumnDefinitions.Add(new(){Width=new GridLength(24)});_body.ColumnDefinitions.Add(new(){Width=new GridLength(0.44,GridUnitType.Star)});
        _room=new(c,Navigate){MaxHeight=560,HorizontalAlignment=HorizontalAlignment.Stretch};_left.Children.Add(_room);_left.Children.Add(Ui.Text("你的节奏，就是这里的节奏。",13,Ui.Muted));_left.Children.Add(_toast);
        _body.Children.Add(_left);Grid.SetColumn(_panel,2);_body.Children.Add(_panel);Grid.SetRow(_body,1);shell.Children.Add(_body);
        var footer=new WrapPanel{Margin=new Thickness(0,16,0,0)};
        foreach(var (id,label) in new[]{("home","⌂  小屋"),("play","成长"),("chat","聊一会儿"),("focus","专注"),("relax","放松"),("mood","心情"),("room","布置")})
        {var b=Ui.Button(label,()=>Navigate(id));_navigation[id]=b;footer.Children.Add(b);}
        Grid.SetRow(footer,2);shell.Children.Add(footer);Content=new Border{Background=Ui.Cream,Padding=new Thickness(26),Child=shell};
        SizeChanged+=(_,_)=>ApplyLayout();
        StateChanged+=(_,_)=>UpdateMotion();
        c.Changed+=OnChanged;c.Tick+=OnTick;c.Notice+=Toast;
        _toastTimer.Tick+=(_,_)=>{_toastTimer.Stop();_toast.Text="";};
        Closed+=(_,_)=>{c.Changed-=OnChanged;c.Tick-=OnTick;c.Notice-=Toast;_toastTimer.Stop();StopRelax();_room.Release();};
        Navigate(c.Preferences.Onboarded?"home":"welcome");
    }
    public void Toast(string text){_toast.Text=text;_toastTimer.Stop();_toastTimer.Start();}
    public void Navigate(string page)
        =>RenderPage(page,false);
    private void RenderPage(string page,bool preserveView)
    {
        if(_page=="relax" && page!="relax")StopRelax();
        if(_page=="room" && page!="room")_room.Preview=null;
        _page=page;_clockText=null;_liveText=null;_chatState=null;
        if(!preserveView)_compactScene=false;ApplyLayout();
        UpdateHeader();foreach(var (id,b) in _navigation)b.Background=id==page?Ui.Brush("#DDE8D7"):Ui.Brush("#EFF2EB");
        var content=page switch
        {
            "welcome"=>Welcome(),"chat"=>Chat(),"focus"=>FocusPage(),"relax"=>Relax(),"mood"=>Mood(),"plant"=>Plant(),"room"=>Room(),"play"=>Play(),"settings"=>Settings(),_=>Home()
        };
        _panel.Content=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,8,0)};
    }
    private void ApplyLayout()
    {
        if(ActualHeight>0)_room.Height=Math.Clamp(ActualHeight-230,220,560);
        bool narrow=ActualWidth>0&&ActualWidth<940;bool showScene=!narrow||_compactScene;
        _sceneToggle.Visibility=narrow?Visibility.Visible:Visibility.Collapsed;_sceneToggle.Content=_compactScene?"返回活动":"查看3D小屋";
        _left.Visibility=showScene?Visibility.Visible:Visibility.Collapsed;_panel.Visibility=narrow&&_compactScene?Visibility.Collapsed:Visibility.Visible;
        _body.ColumnDefinitions[0].Width=showScene?new GridLength(narrow?1:.56,GridUnitType.Star):new GridLength(0);
        _body.ColumnDefinitions[1].Width=new GridLength(narrow?0:24);_body.ColumnDefinitions[2].Width=narrow&&_compactScene?new GridLength(0):new GridLength(.44,GridUnitType.Star);
    }
    private void OnChanged()
    {
        UpdateHeader();_room.Refresh();
        UpdateMotion();
        if(_page is "home" or "chat" or "focus" or "plant")RenderPage(_page,true);
        _settingsChanged();
    }
    private void UpdateMotion()
    {
        _room.MotionVisible=WindowState!=WindowState.Minimized;
        _relaxPet?.MotionSettings(_c.Preferences.ReducedMotion,_c.Preferences.Quiet,_c.AnimationSuspended||WindowState==WindowState.Minimized);
    }
    private void UpdateHeader()
    {
        _petName.Text=_c.Preferences.PetName+"的小屋";
        _status.Text=_c.Preferences.Quiet?"安静陪伴中":_c.Clock.Active?(_c.Clock.Running?"一起"+(_c.Clock.Activity!.Kind=="focus"?"专注":"休息"):"计时已暂停"):_c.Busy?"正在回应":"今天也按自己的节奏来";
    }
    private void OnTick()
    {
        if(_clockText is not null)_clockText.Text=ClockLabel();
        if(_liveText is not null && _c.Busy)_liveText.Text=_c.LiveReply.Length==0?"…":_c.LiveReply;
        if(_chatState is not null)_chatState.Text=_c.Waiting?"仍在等待服务响应，可以随时停止。":_c.ChatStatus;
    }
    private string ClockLabel()=>TimeSpan.FromSeconds(Math.Ceiling(_c.Clock.Remaining)).ToString(_c.Clock.Remaining>=3600?@"hh\:mm\:ss":@"mm\:ss");
    private UIElement Home()
    {
        var p=Ui.Stack(Ui.Text("给自己留一点空隙",28,null,true),Ui.Text("不急着做完所有事。先从一件小事开始。",14,Ui.Muted));
        p.Children.Add(Ui.Card(Ui.Stack(Ui.Text("现在想做什么？",17,null,true),Ui.Row(Ui.Button("一起专注",()=>Navigate("focus"),true),Ui.Button("歇一会儿",()=>Navigate("relax"))),Ui.Row(Ui.Button("聊聊近况",()=>Navigate("chat")),Ui.Button("记下心情",()=>Navigate("mood"))))));
        if(_c.Clock.Active)p.Children.Add(Ui.Card(Ui.Stack(Ui.Text(_c.Clock.Running?"计时进行中":"上次的计时已暂停",16,null,true),Ui.Text("离开、锁屏和睡眠时间不会计入专注。",12,Ui.Muted),Ui.Button("查看计时",()=>Navigate("focus")))));
        var recent=_c.Store.Focus(0,3);var today=DateOnly.FromDateTime(DateTime.Now);
        p.Children.Add(Ui.Card(Ui.Stack(Ui.Text("小屋里的小变化",17,null,true),Ui.Text($"植物已收到 {_c.Store.PlantPoints} 次浇水。它会慢慢长大，也不会因为你忙碌而枯萎。",13,Ui.Muted),Ui.Button("看看植物",()=>Navigate("plant")))));
        if(recent.Count>0){var history=Ui.Stack(Ui.Text("最近的陪伴",16,null,true));foreach(var f in recent)history.Children.Add(Ui.Text($"{f.Started:MM-dd HH:mm}  ·  {(f.Kind=="focus"?"专注":"休息")} {Math.Floor(f.ElapsedSeconds/60)} 分钟",13,Ui.Muted));p.Children.Add(Ui.Card(history));}
        p.Children.Add(Ui.Text(_c.Secrets.Exists?"聊天使用你选择的 AI 服务；其他活动在本机完成。":"还没连接 AI 也没关系，小屋里的活动都能使用。",12,Ui.Muted));return p;
    }
    private UIElement Welcome()
    {
        var name=Ui.Input(_c.Preferences.PetName,max:20,automationName:"伙伴名字");var user=Ui.Input("",max:30,automationName:"你的称呼，可留空");
        var quiet=Ui.Check("默认安静陪伴",true);
        return Ui.Stack(Ui.Text("很高兴在这里遇见你",26,null,true),Ui.Text("一个陪你专注、休息和聊天的小伙伴。先给它起个名字吧。",14,Ui.Muted),Ui.Card(Ui.Stack(Ui.Text("伙伴名字"),name,Ui.Text("怎么称呼你（可选）"),user,quiet,Ui.Button("进入我的小屋",()=>
        {
            _c.SavePreferences(_c.Preferences with{PetName=name.Text.Trim(),UserName=user.Text.Trim(),Quiet=quiet.IsChecked==true,Onboarded=true});Navigate("home");
            Toast("点击书桌开始专注，点植物看看成长；底部按钮也能到达所有活动。AI 连接可在设置里完成。");
        },true))),Ui.Text("无需注册。本地活动离线可用；聊天需要你自行配置 AI 服务，可能产生接口费用。",12,Ui.Muted));
    }
    private UIElement FocusPage()
    {
        var p=Ui.Stack(Ui.Text("一起，做一件小事",25,null,true),Ui.Text("不用赶进度。开始一小段时间就好。",13,Ui.Muted));
        if(_c.Clock.Active)
        {
            var a=_c.Clock.Activity!;_clockText=Ui.Text(ClockLabel(),64,Ui.Sage,true);_clockText.HorizontalAlignment=HorizontalAlignment.Center;
            var info=Ui.Stack(Ui.Text(a.Kind=="focus"?"专注时间":"休息时间",15,Ui.Muted),_clockText,Ui.Text(_c.Clock.Running?"正在进行":"已暂停 · 离开时间不累计",13,Ui.Muted));
            if(!string.IsNullOrEmpty(a.Title))info.Children.Add(Ui.Text(a.Title,16,null,true));
            info.Children.Add(Ui.Row(Ui.Button(_c.Clock.Running?"暂停":"继续",()=>{if(_c.Clock.Running)_c.Pause();else _c.Resume();},true),Ui.Button("结束本次",()=>{if(Ui.Confirm("结束并保存实际时长？提前结束也没关系。")){_c.Finish();Toast("这一段时间已记下。谢谢你照顾自己的节奏。");}})));
            p.Children.Add(Ui.Card(info));
        }
        else
        {
            var title=Ui.Input(_c.FocusDraft,max:120,automationName:"本次专注内容");var duration=Ui.Input(_c.FocusDurationDraft??_c.Preferences.FocusMinutes.ToString(),max:3,automationName:"专注分钟数");
            title.TextChanged+=(_,_)=>_c.FocusDraft=title.Text;duration.TextChanged+=(_,_)=>_c.FocusDurationDraft=duration.Text;
            var presets=Ui.Row();foreach(int m in new[]{15,25,45})presets.Children.Add(Ui.Button(m+" 分钟",()=>duration.Text=m.ToString()));
            var restDuration=Ui.Input(_c.RestDurationDraft??_c.Preferences.RestMinutes.ToString(),max:3,automationName:"休息分钟数");restDuration.TextChanged+=(_,_)=>_c.RestDurationDraft=restDuration.Text;var restPresets=Ui.Row();foreach(int m in new[]{5,10,15})restPresets.Children.Add(Ui.Button(m+" 分钟",()=>restDuration.Text=m.ToString()));
            p.Children.Add(Ui.Card(Ui.Stack(Ui.Text("这次想做什么（可选）"),title,presets,Ui.Text("专注时长 · 1–180 分钟",12,Ui.Muted),duration,Ui.Button("开始专注",()=>_c.Start("focus",ReadMinutes(duration),title.Text),true),new Expander{Header="先休息一下",Content=Ui.Stack(restPresets,Ui.Text("休息时长 · 1–180 分钟",12,Ui.Muted),restDuration,Ui.Button("开始休息",()=>_c.Start("rest",ReadMinutes(restDuration),"")))})));
        }
        var records=_c.Store.Focus(_focusOffset,10);var list=Ui.Stack(Ui.Text("专注与休息记录",16,null,true));
        if(records.Count==0)list.Children.Add(Ui.Text("这里会留下你和伙伴一起度过的时间。",13,Ui.Muted));
        foreach(var f in records)list.Children.Add(Ui.Text($"{f.Started:MM-dd HH:mm} · {(f.Kind=="focus"?"专注":"休息")} {f.ElapsedSeconds/60:0.#} 分钟 · {(f.Result=="completed"?"完成":"提前结束")}\n{f.Title}",13));
        list.Children.Add(Pager(_focusOffset,records.Count,10,offset=>{_focusOffset=offset;Navigate("focus");}));p.Children.Add(Ui.Card(list));return p;
    }
    private static int ReadMinutes(TextBox input){if(!int.TryParse(input.Text,out int m)||m is <1 or >180)throw new OperationFailureException("时长需要为 1–180 分钟。");return m;}
    private UIElement Pager(int offset,int count,int size,Action<int> change)
    {
        var previous=Ui.Button("较新记录",()=>change(Math.Max(0,offset-size)));previous.IsEnabled=offset>0;
        var next=Ui.Button("较早记录",()=>change(offset+size));next.IsEnabled=count==size;return Ui.Row(previous,next);
    }
    private UIElement Mood()
    {
        var mood=Ui.Select(new[]{"愉快","平静","一般","低落","烦躁","不想分类"},"平静","当前心情");var note=Ui.Input("",true,1000,"心情备注");string? editId=null;DateTimeOffset created=DateTimeOffset.Now;
        var p=Ui.Stack(Ui.Text("今天的你，怎么样？",25,null,true),Ui.Text("没有正确答案，也不用每天记录。内容只保存在本机。",13,Ui.Muted));
        p.Children.Add(Ui.Card(Ui.Stack(mood,note,Ui.Button("保存心情",()=>
        {
            var now=DateTimeOffset.Now;_c.Store.SaveMood(new(editId??Guid.NewGuid().ToString("N"),mood.SelectedItem?.ToString()??"不想分类",note.Text,editId is null?now:created,now));Navigate("mood");Toast("记下了。愿意的话，可以聊聊，也可以先放松一下。");
        },true))));
        var entries=_c.Store.Moods(_moodOffset,10);
        foreach(var m in entries)
        {
            p.Children.Add(Ui.Card(Ui.Stack(Ui.Text($"{m.Created:MM-dd HH:mm}   {m.Mood}",14,null,true),Ui.Text(m.Note.Length==0?"没有备注":m.Note,13,Ui.Muted),Ui.Row(
                Ui.Button("聊聊这个",()=>{_c.Draft=$"我现在觉得{m.Mood}。{m.Note}";Navigate("chat");Toast("已放入输入框，点击发送才会分享给 AI。");}),
                Ui.Button("编辑",()=>{editId=m.Id;created=m.Created;mood.SelectedItem=m.Mood;note.Text=m.Note;note.Focus();}),
                Ui.Button("删除",()=>{if(Ui.Confirm("删除这条心情记录及含有它的应用恢复备份？外部导出副本需自行删除。")){_c.Store.DeleteMood(m.Id);Navigate("mood");}})))));
        }
        p.Children.Add(Pager(_moodOffset,entries.Count,10,offset=>{_moodOffset=offset;Navigate("mood");}));return p;
    }
    private UIElement Plant()
    {
        int points=_c.Store.PlantPoints;
        return Ui.Stack(Ui.Text("慢慢长大，也挺好",25,null,true),Ui.Text("你忙的时候，它会等你。不会枯萎，也没有连续签到。",13,Ui.Muted),Ui.Card(Ui.Stack(Ui.Text(points>=5?"已经开花了 ✿":points>=2?"正在舒展叶子":"一颗小小的芽",24,Ui.Sage,true),Ui.Text($"累计浇水 {points} 次。每天浇水一次，2 次长叶、5 次开花。",14),Ui.Button("浇一点水",()=>{bool watered=_c.Store.Water(DateOnly.FromDateTime(DateTime.Now));_c.Refresh();Toast(watered?"喝到水啦。谢谢你来看看它。":"今天已经浇过水了，陪它待一会儿就好。");},true))),Ui.Button("看看装饰",()=>Navigate("room")));
    }
    private UIElement Play()
    {
        var day=DateOnly.FromDateTime(DateTime.Now);var progress=_c.Store.GameProgress();
        var panel=Ui.Stack(Ui.Text("一起慢慢长大",25,null,true),Ui.Text($"Lv.{progress.Level}  ·  陪伴值 {progress.Experience}  ·  ⭐ {progress.Stars}",17,Ui.Sage,true),Ui.Text(progress.Level>=20?"已经到达当前最高等级。":"距离下一级还差 "+GameProgression.ExperienceToNext(progress.Experience)+" 点陪伴值。每天最多获得 20 点，不需要连续签到。",12,Ui.Muted));
        var tasks=Ui.Stack(Ui.Text("今天的小任务",17,null,true));
        foreach(var task in _c.Store.GameTasks(day))tasks.Children.Add(Ui.Card(Ui.Stack(Ui.Text((task.Completed?"✓  ":"")+task.Title,15,null,true),Ui.Text(task.Hint+"  ·  完成得 "+task.RewardXp+" 陪伴值和 "+task.RewardStars+" 颗星。",12,Ui.Muted),task.Completed?Ui.Text("已经记下啦",12,Ui.Sage):Ui.Button(task.Id switch{"ball"=>"抛小球","discover"=>"去户外发现","relax"=>"开始放松","focus"=>"开始专注",_=>"完成"},()=>RunGameTask(task.Id,day),true))));
        panel.Children.Add(tasks);
        var inventory=Ui.Stack(Ui.Text("玩具与布置",17,null,true));var owned=_c.Store.GameInventory().ToDictionary(x=>x.ItemId,x=>x.Quantity,StringComparer.Ordinal);
        foreach(var item in GameProgression.Catalog.Where(x=>x.Kind is "toy" or "decor" or "rug"))
        {
            int count=owned.GetValueOrDefault(item.Id);var row=Ui.Row(Ui.Text($"{item.Name}  ·  {count} 件  ·  {item.Price} 星",13),Ui.Button(count>=item.MaxOwned?"已拥有":"兑换",()=>{if(_c.Store.PurchaseGameItem(item.Id)){_c.Refresh();Navigate("play");Toast("物品已放入本机收藏。可在布置页选择摆放位置。");}else Toast("星星不足，慢慢来就好。");}));
            inventory.Children.Add(row);
        }
        panel.Children.Add(Ui.Card(inventory));
        var discoveries=_c.Store.GameDiscoveries();panel.Children.Add(Ui.Card(Ui.Stack(Ui.Text("户外收藏",17,null,true),Ui.Text(discoveries.Count==0?"还没有发现。去 20m × 20m 的户外场景逛逛吧。":string.Join("、",discoveries.Select(x=>GameProgression.Catalog.First(i=>i.Id==x.ItemId).Name)),13,Ui.Muted),Ui.Text("发现只保存在这台设备上。",11,Ui.Muted))));
        var ownedIds=owned.Keys.ToHashSet(StringComparer.Ordinal);var collection=Ui.Stack(Ui.Text("收藏图鉴",17,null,true));
        foreach(var item in GameProgression.Catalog.Where(x=>x.Kind is "discovery" or "decor" or "rug"))collection.Children.Add(Ui.Text((ownedIds.Contains(item.Id)?"✓  ":"○  ")+item.Name,12,ownedIds.Contains(item.Id)?Ui.Sage:Ui.Muted));
        panel.Children.Add(Ui.Card(collection));
        var achievements=_c.Store.GameAchievements().Select(x=>x.Id).ToHashSet(StringComparer.Ordinal);var unlockedAchievements=GameProgression.AchievementsCatalog.Where(x=>achievements.Contains(x.Id)).Select(x=>x.Name).ToArray();
        panel.Children.Add(Ui.Card(Ui.Stack(Ui.Text("小小纪念",17,null,true),unlockedAchievements.Length==0?Ui.Text("完成任务后会留下纪念。没有连续签到或错过惩罚。",12,Ui.Muted):Ui.Text(string.Join("、",unlockedAchievements),12,Ui.Sage))));
        return panel;
    }
    private void RunGameTask(string taskId,DateOnly day)
    {
        if((taskId is "ball" or "discover")&&!_room.IsVisible&&ActualWidth<940)
        {
            _compactScene=true;ApplyLayout();Dispatcher.BeginInvoke(new Action(()=>RunGameTask(taskId,day)));return;
        }
        if(taskId=="ball")
        {
            _room.SwitchScene(false);if(!_room.PlayBall()){_room.SwitchScene(true);if(!_room.PlayBall())return;}
            _c.Store.RecordBallPlay(day);Toast("小球滚出去啦，伙伴正跑去捡。");
        }
        else if(taskId=="discover")
        {
            _room.SwitchScene(true);bool newItem=_c.Store.GameDiscoveries().Count<3;
            if(!_c.Store.DiscoverOutdoor(day)){Toast("今天已经记下这个发现啦，明天再来看看。");return;}
            Toast(newItem?"发现了一件小东西，已经放进收藏里。":"伙伴记下了今天的户外散步。");
        }
        else {Navigate(taskId=="relax"?"relax":"focus");return;}
        _c.Refresh();if(_page=="play")RenderPage("play",true);
    }
    private UIElement Room()
    {
        var theme=Ui.Select(new[]{"跟随时间","白天","夜晚"},_c.Preferences.Theme=="auto"?"跟随时间":_c.Preferences.Theme=="day"?"白天":"夜晚","窗外明暗主题");
        var rug=Ui.Select(new[]{"奶油色","鼠尾草绿","柔和粉"},_c.Preferences.Rug=="cream"?"奶油色":_c.Preferences.Rug=="sage"?"鼠尾草绿":"柔和粉","地毯颜色");
        var names=new Dictionary<string,string>{{"none","无摆件"},{"star","小星星 · 完成一次至少5分钟专注"},{"cloud","小云朵 · 完成一次放松"},{"flower","小花 · 植物成熟"}};
        var ornament=Ui.Select(names.Where(x=>_c.Store.Unlocked(x.Key)).Select(x=>x.Value),names[_c.Preferences.Ornament],"桌面摆件");
        void Preview(){_room.Preview=_c.Preferences with{Theme=theme.SelectedIndex==0?"auto":theme.SelectedIndex==1?"day":"night",Rug=rug.SelectedIndex==0?"cream":rug.SelectedIndex==1?"sage":"rose",Ornament=names.FirstOrDefault(x=>x.Value==ornament.SelectedItem?.ToString()).Key??"none"};}
        theme.SelectionChanged+=(_,_)=>Preview();rug.SelectionChanged+=(_,_)=>Preview();ornament.SelectionChanged+=(_,_)=>Preview();
        var page=Ui.Stack(Ui.Text("把这里布置成你喜欢的样子",24,null,true),Ui.Text("基础功能一直开放，装饰只是相处留下的小纪念。",13,Ui.Muted),Ui.Card(Ui.Stack(Ui.Text("窗外"),theme,Ui.Text("地毯"),rug,Ui.Text("桌面摆件"),ornament,Ui.Row(Ui.Button("保存布置",()=>{Preview();var v=_room.Preview!;_c.SavePreferences(_c.Preferences with{Theme=v.Theme,Rug=v.Rug,Ornament=v.Ornament});Toast("布置已保存。");Navigate("home");},true),Ui.Button("取消",()=>Navigate("home"))))),Ui.Text("当前预览未保存。切换页面时将恢复保存的布置。",12,Ui.Muted));
        var inventory=_c.Store.GameInventory().Where(x=>x.Quantity>0).Select(x=>x.ItemId).ToHashSet(StringComparer.Ordinal);
        var place=Ui.Stack(Ui.Text("家具摆放",17,null,true),Ui.Text("从成长页兑换物品，再选一个位置摆放。",12,Ui.Muted));
        foreach(var (slot,label) in new[]{("desk","书桌"),("shelf","墙边"),("garden","花园")})
        {
            var choices=GameProgression.Catalog.Where(x=>inventory.Contains(x.Id)&&(x.Kind is "decor" or "rug")).ToArray();
            if(choices.Length==0){place.Children.Add(Ui.Text($"{label}：还没有可摆放的物品",12,Ui.Muted));continue;}
            var select=Ui.Select(choices.Select(x=>x.Name),choices[0].Name,label+"摆放物品");
            place.Children.Add(Ui.Row(Ui.Text(label,13),select,Ui.Button("摆放",()=>{var item=choices.First(x=>x.Name==select.SelectedItem?.ToString());if(_c.Store.PlaceGameItem(slot,item.Id)){_c.Refresh();Toast(item.Name+"已摆放。");Navigate("room");}})));
        }
        page.Children.Add(Ui.Card(place));return page;
    }
    private void StopRelax(){_relaxTimer?.Stop();_relaxTimer=null;_relaxPet?.Release();_relaxPet=null;}
    private UIElement Relax()
    {
        var p=Ui.Stack(Ui.Text("先松一口气",25,null,true),Ui.Text("没有分数，也没有必须完成的目标。随时结束。",13,Ui.Muted));
        var area=new ContentControl();
        void Begin(string type)
        {
            if(_c.Clock.Running){if(!Ui.Confirm("暂停当前计时，再放松一下？"))return;_c.Pause();}
            StopRelax();_relaxFinished=false;
            void Completed(){if(_relaxFinished)return;_relaxFinished=true;_c.Store.CompleteRelaxation();Toast("这段放松留下了一朵小云装饰。想继续或返回小屋都可以。");}
            if(type=="pet")
            {
                var pet=new Pet3DView{State="idle"};_relaxPet=pet;UpdateMotion();var count=0;var info=Ui.Text("按住或轻划，揉揉这个立体的小伙伴。",13,Ui.Muted);
                void Rub(){pet.Rub(_c.Preferences.ReducedMotion);info.Text=++count%2==0?"它眯起眼睛，像一团软软的云。":"收到啦，慢慢来就好。";if(_c.Preferences.Sounds&&!_c.Preferences.Quiet)System.Media.SystemSounds.Asterisk.Play();Completed();}
                DateTimeOffset lastRub=DateTimeOffset.MinValue;
                pet.MouseLeftButtonDown+=(_,_)=>{pet.CaptureMouse();Rub();};pet.MouseMove+=(_,e)=>{if(pet.IsMouseCaptured&&e.LeftButton==MouseButtonState.Pressed&&DateTimeOffset.UtcNow-lastRub>TimeSpan.FromMilliseconds(350)){lastRub=DateTimeOffset.UtcNow;pet.Turn(4);Rub();}};pet.MouseLeftButtonUp+=(_,_)=>pet.ReleaseMouseCapture();
                area.Content=Ui.Card(Ui.Stack(pet,info,Ui.Button("揉揉",Rub,true)));
            }
            if(type=="bubbles")
            {
                var bubbles=new WrapPanel();int popped=0;
                void Fill(){popped=0;bubbles.Children.Clear();for(int i=0;i<20;i++){var b=Ui.Button("○",()=>{});b.Width=52;b.Height=52;b.FontSize=28;b.Padding=new Thickness(0);b.Background=Ui.Brush("#DAE6D4");b.Click+=(_,_)=>{if(!b.IsEnabled)return;b.Content="·";b.IsEnabled=false;if(_c.Preferences.Sounds&&!_c.Preferences.Quiet)System.Media.SystemSounds.Asterisk.Play();if(++popped==20)Completed();};bubbles.Children.Add(b);}}
                Fill();area.Content=Ui.Card(Ui.Stack(bubbles,Ui.Button("换一批",Fill)));
            }
            if(type=="breath")
            {
                int elapsed=0;bool paused=false;var cue=Ui.Text("吸气",36,Ui.Sage,true);var hint=Ui.Text("跟着自己的舒适节奏。无需屏息，不舒服可直接结束。",13,Ui.Muted);
                var ring=new Border{Width=100,Height=100,CornerRadius=new CornerRadius(50),Background=Ui.Brush("#DAE6D4"),Margin=new Thickness(0,12,0,20),HorizontalAlignment=HorizontalAlignment.Left};
                _relaxTimer=new(){Interval=TimeSpan.FromSeconds(1)};_relaxTimer.Tick+=(_,_)=>{if(paused)return;elapsed++;cue.Text=elapsed%8<4?"吸气":"呼气";if(!_c.Preferences.ReducedMotion)ring.Width=ring.Height=elapsed%8<4?100+elapsed%4*8:124-(elapsed%4)*8;if(elapsed>=60){StopRelax();cue.Text="按自己的节奏就好";Completed();}};_relaxTimer.Start();
                area.Content=Ui.Card(Ui.Stack(cue,ring,hint,Ui.Button("暂停 / 继续",()=>paused=!paused)));
            }
        }
        p.Children.Add(Ui.Row(Ui.Button("揉揉伙伴",()=>Begin("pet"),true),Ui.Button("戳泡泡",()=>Begin("bubbles")),Ui.Button("呼吸一分钟",()=>Begin("breath"))));p.Children.Add(area);
        p.Children.Add(Ui.Row(Ui.Button("结束放松",()=>{StopRelax();area.Content=null;Toast("欢迎随时回来坐坐。");}),Ui.Button("返回小屋",()=>Navigate("home"))));return p;
    }
    private UIElement Chat()
    {
        var p=Ui.Stack(Ui.Text("我在这里，听你说",25,null,true),Ui.Text("发送至 "+_c.Preferences.Endpoint+" · "+_c.Preferences.Model,11,Ui.Muted));
        var sessions=_c.Store.Sessions(_sessionOffset,10);var select=new ComboBox{Margin=new Thickness(0,0,0,10),MinHeight=34};
        foreach(var s in sessions)select.Items.Add(new ComboBoxItem{Content=$"{s.Created:MM-dd HH:mm}  {s.Title}",Tag=s,IsSelected=s.Id==_c.Session?.Id});
        select.SelectionChanged+=(_,_)=>Ui.Guard(()=>{if(select.SelectedItem is ComboBoxItem{Tag:ChatSession s}){_chatOffset=0;_c.SelectSession(s);}});
        p.Children.Add(select);p.Children.Add(Ui.Row(Ui.Button("新对话",()=>{_chatOffset=0;_c.NewSession();}),Ui.Button("删除本次",()=>{if(Ui.Confirm("删除此会话和相关应用备份？已发送给服务商及外部导出副本不在删除范围内。"))_c.DeleteSession();})));
        if(sessions.Count==10 || _sessionOffset>0)p.Children.Add(Pager(_sessionOffset,sessions.Count,10,offset=>{_sessionOffset=offset;Navigate("chat");}));
        if(!_c.Secrets.Exists)p.Children.Add(Ui.Card(Ui.Stack(Ui.Text("还没有连接 AI",16,null,true),Ui.Text("本地互动可以直接使用。自由对话需要你提供密钥，调用可能收费。",13,Ui.Muted),Ui.Button("连接 AI",()=>Navigate("settings")),Ui.Button("给我一点鼓励（本地）",()=>Toast("今天不用做得完美。先照顾自己，再开始一件小事。")))));
        var messages=_c.Session is null?new List<ChatMessage>():_c.Store.Messages(_c.Session.Id,_chatOffset,20);
        var transcript=new StackPanel();var transcriptScroll=new ScrollViewer{Content=transcript,MaxHeight=230,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,0,0,10)};
        foreach(var m in messages)
        {
            if(m.Status=="streaming"&&_c.Busy)continue;
            var content=Ui.Stack(Ui.Text(m.Role=="user"?"你":_c.Preferences.PetName,11,Ui.Muted,true),Ui.Text(m.Content.Length==0?"未收到内容":m.Content,14));
            if(m.Status is "stopped" or "error")content.Children.Add(Ui.Text(m.Status=="stopped"?"已停止":"未完成",11,Ui.Muted));
            content.Children.Add(Ui.Button("复制",()=>Clipboard.SetText(m.Content)));transcript.Children.Add(Ui.Card(content,new Thickness(16)));
        }
        if(_c.Session is not null)p.Children.Add(Pager(_chatOffset,messages.Count,20,offset=>{_chatOffset=offset;Navigate("chat");}));
        if(_c.Busy){_liveText=Ui.Text(_c.LiveReply.Length==0?"…":_c.LiveReply,14);transcript.Children.Add(Ui.Card(Ui.Stack(Ui.Text(_c.Preferences.PetName,11,Ui.Muted,true),_liveText)));}
        p.Children.Add(transcriptScroll);if(_chatOffset==0)transcriptScroll.Loaded+=(_,_)=>transcriptScroll.ScrollToEnd();
        _chatState=Ui.Text(_c.ChatStatus,12,Ui.Muted);p.Children.Add(_chatState);
        var input=Ui.Input(_c.Draft,true,automationName:"聊天消息");input.TextChanged+=(_,_)=>_c.Draft=input.Text;
        async Task Send()
        {
            _chatOffset=0;await ChatRequests.Send(_c);
        }
        input.PreviewKeyDown+=async(_,e)=>
        {
            if(e.Key==Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && !InputMethod.GetIsInputMethodEnabled(input)) {e.Handled=true;try{await Send();}catch(Exception ex) when(OperationErrors.Expected(ex)){Ui.Error(OperationErrors.Message(ex));}}
        };
        // IME composition is tracked explicitly; Enter confirms a candidate before sending.
        bool composing=false;
        TextCompositionManager.AddPreviewTextInputStartHandler(input,(_,_)=>composing=true);
        TextCompositionManager.AddPreviewTextInputHandler(input,(_,_)=>composing=false);
        input.PreviewKeyDown+=async(_,e)=>{if(e.Handled)return;if(e.Key==Key.Enter&&!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)&&!composing&&InputMethod.GetIsInputMethodEnabled(input)){e.Handled=true;try{await Send();}catch(Exception ex) when(OperationErrors.Expected(ex)){Ui.Error(OperationErrors.Message(ex));}}};
        p.Children.Add(input);var send=Ui.AsyncButton("发送",Send,true);send.IsEnabled=!_c.Busy&&_c.Secrets.Exists;
        var stop=Ui.Button("停止",_c.StopReply);stop.IsEnabled=_c.Busy;
        var lastUser=messages.LastOrDefault(x=>x.Role=="user");var retry=Ui.AsyncButton("重试最后消息",async()=>{if(lastUser is not null)await _c.Send(lastUser.Id);});retry.IsEnabled=!_c.Busy&&lastUser is not null&&_c.Secrets.Exists&&messages.LastOrDefault()?.Status is "error" or "stopped";
        p.Children.Add(Ui.Row(send,stop,retry));p.Children.Add(Ui.Text("Enter 发送 · Shift+Enter 换行。这里只分享你发送的内容，不自动读取心情或任务。",11,Ui.Muted));return p;
    }
}
