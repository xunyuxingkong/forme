using Forme.Core;
using System.Diagnostics;
using System.Text.Json;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace Forme.App;

internal sealed partial class MainWindow
{
    private UIElement Settings()
    {
        var p=_c.Preferences;
        var content=Ui.Stack(Ui.Text("让陪伴更合你的习惯",24,null,true),Ui.Text("设置在本机保存。恢复偏好不会清空你的记录。",13,Ui.Muted));
        var name=Ui.Input(p.PetName,max:20,automationName:"伙伴名字");var user=Ui.Input(p.UserName,max:30,automationName:"你的称呼");
        var appearance=Ui.Select(PetModels.Catalog.Select(x=>x.Name),PetModels.Catalog.First(x=>x.Id==p.PetModel).Name,"伙伴模型");
        var performance=Ui.Select(new[]{"平衡 · 动作30FPS","省电 · 动作20FPS"},p.PerformanceMode=="economy"?"省电 · 动作20FPS":"平衡 · 动作30FPS","性能模式");
        var backgroundPresets=new[]{("暖白","#F7F5EE"),("浅绿","#EAF1E7"),("淡蓝","#EAF2F7"),("浅粉","#F7ECEE"),("淡紫","#F0ECF7"),("暖黄","#F7F1DF"),("自定义颜色","自定义")};
        var backgroundChoice=Ui.Select(backgroundPresets.Select(x=>x.Item1),backgroundPresets.FirstOrDefault(x=>x.Item2.Equals(p.AppBackground,StringComparison.OrdinalIgnoreCase)).Item1??"自定义颜色","软件背景颜色预设");
        var backgroundHex=Ui.Input(p.AppBackground,max:7,automationName:"自定义背景色 HEX");var backgroundSample=new Border{Height=34,CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),BorderBrush=Ui.Line,Background=Ui.Brush(p.AppBackground),Margin=new Thickness(0,0,0,12)};
        backgroundChoice.SelectionChanged+=(_,_)=>{if(backgroundChoice.SelectedIndex>=0&&backgroundChoice.SelectedIndex<6)backgroundHex.Text=backgroundPresets[backgroundChoice.SelectedIndex].Item2;};
        backgroundHex.TextChanged+=(_,_)=>
        {
            var preset=Array.FindIndex(backgroundPresets,x=>x.Item2.Equals(backgroundHex.Text.Trim(),StringComparison.OrdinalIgnoreCase));
            backgroundChoice.SelectedIndex=preset is >=0 and <6?preset:6;
            try{backgroundSample.Background=Ui.Brush(backgroundHex.Text);}catch(FormatException){}
        };
        var quiet=Ui.Check("安静模式（关闭声音、主动招呼和自动动作）",p.Quiet);var reduced=Ui.Check("减少动效",p.ReducedMotion);var top=Ui.Check("桌面伙伴置顶",p.Topmost);
        var sound=Ui.Check("互动音效",p.Sounds);var timerSound=Ui.Check("计时完成提示音",p.TimerSounds);var notifications=Ui.Check("计时完成通知（不含私人内容）",p.Notifications);
        var greetings=Ui.Check("每日最多两次本地招呼",p.Greetings);var from=Ui.Input(p.GreetingStart.ToString(),max:2);var until=Ui.Input(p.GreetingEnd.ToString(),max:2);
        var scale=Ui.Select(new[]{"小","标准","大"},p.PetScale<.9?"小":p.PetScale>1.1?"大":"标准","桌宠大小");
        var focus=Ui.Input(p.FocusMinutes.ToString(),max:3);var rest=Ui.Input(p.RestMinutes.ToString(),max:3);
        var startup=Ui.Check("登录 Windows 时启动",StartupLink.Exists);
        content.Children.Add(Ui.Card(Ui.Stack(Ui.Text("伙伴与显示",17,null,true),Ui.Text("伙伴名字"),name,Ui.Text("你的称呼（可选）"),user,Ui.Text("伙伴大小"),scale,Ui.Text("软件背景色"),backgroundChoice,backgroundHex,backgroundSample,quiet,reduced,top,sound,timerSound,notifications,greetings,new Expander{Header="主动招呼时段（本地时间）",Content=Ui.Stack(Ui.Text("开始小时 0–23"),from,Ui.Text("结束小时 1–24"),until)},startup)));
        content.Children.Add(Ui.Card(Ui.Stack(Ui.Text("专注与休息",17,null,true),Ui.Text("默认专注时长 · 分钟"),focus,Ui.Text("默认休息时长 · 分钟"),rest)));
        content.Children.Add(Ui.Card(Ui.Stack(Ui.Text("伙伴模型与资源占用",17,null,true),appearance,performance,Ui.Text("4种3D模型共用动作与寻路。平衡待机10FPS，省电待机5FPS；睡眠2FPS。静态网格共享，隐藏窗口和减少动效时停止动画。",12,Ui.Muted))));
        content.Children.Add(Ui.Button("保存偏好",()=>
        {
            if(!int.TryParse(from.Text,out int start)||!int.TryParse(until.Text,out int end))throw new OperationFailureException("请输入有效小时。");
            var next=_c.Preferences with{PetName=name.Text.Trim(),UserName=user.Text.Trim(),AppBackground=backgroundHex.Text.Trim().ToUpperInvariant(),Quiet=quiet.IsChecked==true,ReducedMotion=reduced.IsChecked==true,Topmost=top.IsChecked==true,Sounds=sound.IsChecked==true,TimerSounds=timerSound.IsChecked==true,Notifications=notifications.IsChecked==true,Greetings=greetings.IsChecked==true,GreetingStart=start,GreetingEnd=end,PetScale=scale.SelectedIndex==0?.8:scale.SelectedIndex==2?1.3:1,FocusMinutes=ReadMinutes(focus),RestMinutes=ReadMinutes(rest)};
            next=next with{PetModel=PetModels.Catalog[appearance.SelectedIndex].Id,PerformanceMode=performance.SelectedIndex==1?"economy":"balanced"};next.Validate();StartupLink.Set(startup.IsChecked==true);_c.SavePreferences(next);Toast("偏好已保存，伙伴模型已同步。");
        },true));
        content.Children.Add(Ui.Button("恢复常用偏好默认值",()=>
        {
            if(!Ui.Confirm("恢复称呼、大小、动效、声音和计时默认值？历史、房间和 AI 凭据保留；开机启动将关闭。"))return;
            var d=new Preferences();StartupLink.Set(false);_c.SavePreferences(_c.Preferences with{PetName=d.PetName,UserName="",Quiet=d.Quiet,ReducedMotion=d.ReducedMotion,Topmost=false,Sounds=false,TimerSounds=false,Notifications=true,Greetings=false,GreetingStart=9,GreetingEnd=21,PetScale=1,FocusMinutes=25,RestMinutes=5});Navigate("settings");
        }));
        var endpoint=Ui.Input(p.Endpoint,max:500,automationName:"AI 服务地址");var model=Ui.Input(p.Model,max:100,automationName:"AI 模型名称");
        var key=new PasswordBox{Padding=new Thickness(12),Margin=new Thickness(0,0,0,12),MinHeight=40,MaxLength=512};
        var connectionStatus=Ui.Text(_c.Secrets.Exists?"密钥已保存；切换服务商需要填写新密钥。":"尚未保存密钥。",12,Ui.Muted);
        const string siliconEndpoint="https://api.siliconflow.cn/v1";
        const string siliconModel="deepseek-ai/DeepSeek-V3.2";
        var defaults=new Preferences();
        var providers=new[]{"DeepSeek 官方","硅基流动 · DeepSeek","自定义兼容服务"};
        var provider=Ui.Select(providers,p.Endpoint.TrimEnd('/')==siliconEndpoint?providers[1]:p.Endpoint.TrimEnd('/')==defaults.Endpoint?providers[0]:providers[2],"AI 服务预设");
        provider.SelectionChanged+=(_,_)=>
        {
            if(provider.SelectedIndex==2)return;
            endpoint.Text=provider.SelectedIndex==1?siliconEndpoint:defaults.Endpoint;
            model.Text=provider.SelectedIndex==1?siliconModel:defaults.Model;
            key.Clear();connectionStatus.Text="预设已填入，尚未保存。请填写该服务的 API Key；切换预设不会联网。";
        };
        var advanced=new Expander{Header="高级：服务地址与模型",Content=Ui.Stack(Ui.Text("基础地址（不要包含 /chat/completions 或密钥）",12),endpoint,Ui.Text("模型名称",12),model),Margin=new Thickness(0,0,0,14)};
        var aiPanel=Ui.Stack(Ui.Text("AI 连接",17,null,true),Ui.Text("自由对话会联网，可能产生费用。房间、专注和放松一直在本地运行。",12,Ui.Muted),Ui.Text("服务预设"),provider,Ui.Text("硅基流动预设：DeepSeek-V3.2，关闭深度思考。使用硅基流动平台的密钥，可在高级设置修改模型；可用性和价格以平台为准。",11,Ui.Muted),Ui.Text("API Key"),key,connectionStatus,advanced);
        aiPanel.Children.Add(Ui.Row(Ui.AsyncButton("测试连接",async()=>
        {
            if(!Forme.Core.AiClient.SameEndpoint(endpoint.Text.Trim(),_c.Preferences.Endpoint)&&key.Password.Length==0&&
                !Forme.Core.AiClient.ProviderIdentity(endpoint.Text.Trim()).Equals(Forme.Core.AiClient.ProviderIdentity(_c.Preferences.Endpoint),StringComparison.OrdinalIgnoreCase))
                throw new OperationFailureException("测试新服务商前请填写该服务的 API Key，旧服务密钥不会自动发送。先前配置未改变。");
            if(!Ui.Confirm("将向填写的服务发送固定测试文字，不含私人记录。可能产生少量费用。继续？"))return;
            connectionStatus.Text="正在测试，可以点击停止。";
            try {await _c.TestConnection(endpoint.Text.Trim(),model.Text.Trim(),key.Password.Length>0?key.Password:_c.Secrets.Read());connectionStatus.Text="连接成功。点击保存后用于聊天。";}
            catch(Exception ex) when(OperationErrors.Expected(ex)){connectionStatus.Text=OperationErrors.Message(ex);}
        }),Ui.Button("停止测试",_c.StopReply)));
        aiPanel.Children.Add(Ui.Button("保存 AI 配置",()=>
        {
            if(_c.Busy)throw new OperationFailureException("请先停止并等待当前请求结束。");
            var next=_c.Preferences with{Endpoint=Forme.Core.AiClient.NormalizeEndpoint(endpoint.Text.Trim()),Model=model.Text.Trim()};next.Validate();
            var previousKey=_c.Secrets.Read();string newKey=key.Password;
            if(!Forme.Core.AiClient.SameEndpoint(next.Endpoint,_c.Preferences.Endpoint) && !Ui.Confirm("更换服务地址会新建对话，不自动分享旧历史。"))return;
            _c.SaveAiConfiguration(next,newKey);
            key.Clear();connectionStatus.Text=_c.Secrets.Exists?"配置已保存，密钥由 Windows 保护。":"配置已保存，请填写密钥后聊天。";Toast("AI 配置已保存。未自动发起请求。");
        },true));
        aiPanel.Children.Add(Ui.Button("删除已保存密钥",()=>{if(_c.Busy)throw new OperationFailureException("请先停止并等待请求结束。");if(Ui.Confirm("删除本机保存的 AI 密钥？历史记录保留。")){_c.Secrets.Delete();key.Clear();connectionStatus.Text="密钥已删除。";}}));
        aiPanel.Children.Add(Ui.Text("上下文、输出与角色参数可在下面配置。不自动重试，不后台推理或自动总结记忆。账单以服务商为准，请在服务商处设置额度。",11,Ui.Muted));content.Children.Add(Ui.Card(aiPanel));
        content.Children.Add(PersonaSettings());
        content.Children.Add(DataSettings());
        string version=typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion??"未知";
        content.Children.Add(Ui.Card(Ui.Stack(Ui.Text("关于 Forme",17,null,true),Ui.Text($"v{version} · Windows x64\n没有账号、云同步或遥测。连接预设依据官方协议；真实服务可用性由你的测试连接确认。",12,Ui.Muted),Ui.Text("本地数据："+_c.Store.DirectoryPath,11,Ui.Muted),Ui.Button("打开数据目录",()=>Process.Start(new ProcessStartInfo{FileName=_c.Store.DirectoryPath,UseShellExecute=true})))));
        return content;
    }
    private UIElement DataSettings()
    {
        var chat=Ui.Check("聊天、记忆与摘要",true);var moods=Ui.Check("心情记录",true);var focus=Ui.Check("专注记录",true);var room=Ui.Check("伙伴与房间",true);
        var panel=Ui.Stack(Ui.Text("隐私与个人数据",17,null,true),Ui.Text("普通记录存于本机，未额外加密；密钥由 Windows 保护。我们不上传日志、不扫描磁盘，也不后台读取剪贴板或屏幕。",12,Ui.Muted),chat,moods,focus,room);
        CancellationTokenSource? exportRequest=null;var exportStatus=Ui.Text("",12,Ui.Muted);
        panel.Children.Add(Ui.Row(Ui.AsyncButton("导出所选数据",async()=>
        {
            if(exportRequest is not null)throw new OperationFailureException("请先等待或取消当前数据操作。");
            if(!(chat.IsChecked==true||moods.IsChecked==true||focus.IsChecked==true||room.IsChecked==true))throw new OperationFailureException("至少选择一类数据。");
            if(!Ui.Confirm("导出文件可能包含私人内容，文件不会包含密钥。请选择安全的保存位置。"))return;
            var dialog=new Microsoft.Win32.SaveFileDialog{Filter="Forme 数据 (*.json)|*.json",FileName=$"Forme-{DateTime.Now:yyyyMMdd}.json"};
            if(dialog.ShowDialog(this)==true)
            {
                bool includeChat=chat.IsChecked==true,includeMood=moods.IsChecked==true,includeFocus=focus.IsChecked==true,includeRoom=room.IsChecked==true;
                var request=new CancellationTokenSource();exportRequest=request;exportStatus.Text="正在导出，可以取消…";string directory=_c.Store.DirectoryPath;
                try
                {
                    await Task.Run(()=>
                    {
                        using var reader=new Store(directory,true);
                        reader.ExportFile(dialog.FileName,includeChat,includeMood,includeFocus,includeRoom,request.Token);
                    },request.Token);
                    exportStatus.Text="导出完成，不包含密钥。";
                }
                catch(OperationCanceledException){exportStatus.Text="已取消导出。";}
                finally{exportRequest=null;request.Dispose();}
            }
        }),Ui.AsyncButton("导入数据",async()=>
        {
            RequireIdle();var dialog=new Microsoft.Win32.OpenFileDialog{Filter="Forme 数据 (*.json)|*.json"};if(dialog.ShowDialog(this)!=true)return;
            await ImportFile(dialog.FileName);
        })));
        async Task ImportFile(string path)
        {
            if(exportRequest is not null)throw new OperationFailureException("请先等待或取消当前数据操作。");
            RequireIdle();var request=new CancellationTokenSource();exportRequest=request;exportStatus.Text="正在检查导入文件，可以取消…";
            try
            {
                using var plan=await Task.Run(()=>Store.PrepareImport(path,request.Token),request.Token);
                string summary=$"聊天：{(plan.HasChat?$"替换为{plan.Sessions}个会话 / {plan.Messages}条消息":"保留")}\n心情：{(plan.HasMoods?$"替换为{plan.Moods}条":"保留")}\n专注：{(plan.HasFocus?$"替换为{plan.Focus}条":"保留")}\n房间：{(plan.Room is null?"保留":"替换")}\n\n原内容将备份到 {_c.Store.BackupPath}。只替换文件包含的类别，不覆盖密钥。确认导入？";
                RequireIdle();request.Token.ThrowIfCancellationRequested();
                if(!Ui.Confirm(summary)){exportStatus.Text="未导入。";return;}
                _c.Store.ApplyImport(plan);_c.AfterImport();Navigate("settings");Toast("导入完成，原数据恢复备份在数据目录中。");
            }
            catch(OperationCanceledException){exportStatus.Text="已取消导入。";}
            finally{exportRequest=null;request.Dispose();}
        }
        panel.Children.Add(Ui.Row(Ui.Button("取消导入/导出",()=>exportRequest?.Cancel()),exportStatus));
        panel.Children.Add(Ui.AsyncButton("从恢复备份恢复",async()=>
        {
            RequireIdle();if(!File.Exists(_c.Store.BackupPath))throw new InvalidDataException("没有可恢复的导入备份。");
            await ImportFile(_c.Store.BackupPath);
        }));
        panel.Children.Add(Ui.Button("删除应用恢复备份",()=>{if(Ui.Confirm("删除应用保留的导入与数据库升级备份？")){_c.Store.RemoveBackup();Toast("应用备份已删除。");}}));
        panel.Children.Add(Ui.Row(Ui.Button("清空聊天",()=>Clear("chat")),Ui.Button("清空心情",()=>Clear("moods")),Ui.Button("清空专注记录",()=>Clear("focus"))));
        panel.Children.Add(Ui.Button("清除全部个人数据",()=>
        {
            RequireIdle();if(!Ui.Confirm("清除本机全部聊天、心情、专注、房间、应用备份与密钥？外部导出及服务商留存需自行处理。此操作无法撤销。"))return;
            StartupLink.Set(false);_c.ClearAll();Navigate("settings");Toast("本机个人数据已清除。");
        }));
        panel.Children.Add(Ui.Text("删除是应用层清除，不承诺存储介质取证级擦除。卸载保留数据，可先在这里清除。",11,Ui.Muted));return Ui.Card(panel);
    }
    private void RequireIdle(){if(_c.Busy||_c.Clock.Active)throw new OperationFailureException("请先结束当前计时，并停止和等待 AI 请求结束。");}
    private void Clear(string category)
    {
        RequireIdle();if(!Ui.Confirm(category=="chat"?"清空全部本机聊天、长期记忆、会话摘要和应用恢复备份？外部副本不受影响。":"清空此类本地记录和应用恢复备份？外部副本不受影响。"))return;_c.Store.ClearCategory(category);_c.AfterImport();Navigate("settings");Toast("记录已清空。");
    }
}
