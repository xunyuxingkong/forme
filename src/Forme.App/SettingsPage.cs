using Forme.Core;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Forme.App;

internal sealed partial class MainWindow
{
    private UIElement Settings()
    {
        var p=_c.Preferences;
        var content=Ui.Stack(Ui.Text("让陪伴更合你的习惯",24,null,true),Ui.Text("设置在本机保存。恢复偏好不会清空你的记录。",13,Ui.Muted));
        var name=Ui.Input(p.PetName,max:20);var user=Ui.Input(p.UserName,max:30);var style=Ui.Input(p.ReplyStyle,true,160);
        var quiet=Ui.Check("安静模式（关闭声音、主动招呼和自动动作）",p.Quiet);var reduced=Ui.Check("减少动效",p.ReducedMotion);var top=Ui.Check("桌面伙伴置顶",p.Topmost);
        var sound=Ui.Check("互动音效",p.Sounds);var timerSound=Ui.Check("计时完成提示音",p.TimerSounds);var notifications=Ui.Check("计时完成通知（不含私人内容）",p.Notifications);
        var greetings=Ui.Check("每日最多两次本地招呼",p.Greetings);var from=Ui.Input(p.GreetingStart.ToString(),max:2);var until=Ui.Input(p.GreetingEnd.ToString(),max:2);
        var scale=Ui.Select(new[]{"小","标准","大"},p.PetScale<.9?"小":p.PetScale>1.1?"大":"标准");
        var focus=Ui.Input(p.FocusMinutes.ToString(),max:3);var rest=Ui.Input(p.RestMinutes.ToString(),max:3);
        var startup=Ui.Check("登录 Windows 时启动",StartupLink.Exists);
        content.Children.Add(Ui.Card(Ui.Stack(Ui.Text("伙伴与显示",17,null,true),Ui.Text("伙伴名字"),name,Ui.Text("你的称呼（可选）"),user,Ui.Text("伙伴大小"),scale,quiet,reduced,top,sound,timerSound,notifications,greetings,new Expander{Header="主动招呼时段（本地时间）",Content=Ui.Stack(Ui.Text("开始小时 0–23"),from,Ui.Text("结束小时 1–24"),until)},startup)));
        content.Children.Add(Ui.Card(Ui.Stack(Ui.Text("专注与休息",17,null,true),Ui.Text("默认专注时长 · 分钟"),focus,Ui.Text("默认休息时长 · 分钟"),rest)));
        content.Children.Add(Ui.Button("保存偏好",()=>
        {
            if(!int.TryParse(from.Text,out int start)||!int.TryParse(until.Text,out int end))throw new InvalidOperationException("请输入有效小时。");
            var next=_c.Preferences with{PetName=name.Text.Trim(),UserName=user.Text.Trim(),Quiet=quiet.IsChecked==true,ReducedMotion=reduced.IsChecked==true,Topmost=top.IsChecked==true,Sounds=sound.IsChecked==true,TimerSounds=timerSound.IsChecked==true,Notifications=notifications.IsChecked==true,Greetings=greetings.IsChecked==true,GreetingStart=start,GreetingEnd=end,PetScale=scale.SelectedIndex==0?.8:scale.SelectedIndex==2?1.3:1,FocusMinutes=ReadMinutes(focus),RestMinutes=ReadMinutes(rest)};
            next.Validate();StartupLink.Set(startup.IsChecked==true);_c.SavePreferences(next);Toast("偏好已保存。");
        },true));
        content.Children.Add(Ui.Button("恢复常用偏好默认值",()=>
        {
            if(!Ui.Confirm("恢复称呼、大小、动效、声音和计时默认值？历史、房间和 AI 凭据保留；开机启动将关闭。"))return;
            var d=new Preferences();StartupLink.Set(false);_c.SavePreferences(_c.Preferences with{PetName=d.PetName,UserName="",Quiet=d.Quiet,ReducedMotion=d.ReducedMotion,Topmost=false,Sounds=false,TimerSounds=false,Notifications=true,Greetings=false,GreetingStart=9,GreetingEnd=21,PetScale=1,FocusMinutes=25,RestMinutes=5});Navigate("settings");
        }));
        var endpoint=Ui.Input(p.Endpoint,max:500);var model=Ui.Input(p.Model,max:100);
        var key=new PasswordBox{Padding=new Thickness(12),Margin=new Thickness(0,0,0,12),MinHeight=40,MaxLength=512};
        var connectionStatus=Ui.Text(_c.Secrets.Exists?"密钥已保存。留空保留现有密钥。":"尚未保存密钥。",12,Ui.Muted);
        var advanced=new Expander{Header="高级：服务地址与模型",Content=Ui.Stack(Ui.Text("基础地址（不要包含 /chat/completions 或密钥）",12),endpoint,Ui.Text("模型名称",12),model),Margin=new Thickness(0,0,0,14)};
        var aiPanel=Ui.Stack(Ui.Text("AI 连接 · DeepSeek 预设",17,null,true),Ui.Text("自由对话会联网，可能产生费用。房间、专注和放松一直在本地运行。",12,Ui.Muted),Ui.Text("API Key"),key,connectionStatus,advanced,Ui.Text("回复偏好（发送消息时一起提供给 AI）",12),style);
        aiPanel.Children.Add(Ui.Row(Ui.AsyncButton("测试连接",async()=>
        {
            if(!Ui.Confirm("将向填写的服务发送固定测试文字，不含私人记录。可能产生少量费用。继续？"))return;
            connectionStatus.Text="正在测试，可以点击停止。";
            try {await _c.TestConnection(endpoint.Text.Trim(),model.Text.Trim(),key.Password.Length>0?key.Password:_c.Secrets.Read());connectionStatus.Text="连接成功。点击保存后用于聊天。";}
            catch(Exception ex){connectionStatus.Text=ex.Message;}
        }),Ui.Button("停止测试",_c.StopReply)));
        aiPanel.Children.Add(Ui.Button("保存 AI 配置",()=>
        {
            if(_c.Busy)throw new InvalidOperationException("请先停止并等待当前请求结束。");
            var next=_c.Preferences with{Endpoint=endpoint.Text.Trim().TrimEnd('/'),Model=model.Text.Trim(),ReplyStyle=style.Text.Trim()};next.Validate();
            var previousKey=_c.Secrets.Read();string newKey=key.Password;
            if(next.Endpoint!=_c.Preferences.Endpoint && !Ui.Confirm("更换服务地址会新建对话，不自动分享旧历史。"+(newKey.Length==0&&previousKey.Length>0?"当前密钥将保留，请确认它适用于新服务；也可以取消后填写新密钥。":"")))return;
            try{if(newKey.Length>0)_c.Secrets.Save(newKey);var changed=next.Endpoint!=_c.Preferences.Endpoint;_c.SavePreferences(next);if(changed)_c.NewSession();}
            catch{_c.Secrets.Save(previousKey);throw;}
            key.Clear();connectionStatus.Text=_c.Secrets.Exists?"配置已保存，密钥由 Windows 保护。":"配置已保存，请填写密钥后聊天。";Toast("AI 配置已保存。未自动发起请求。");
        },true));
        aiPanel.Children.Add(Ui.Button("删除已保存密钥",()=>{if(_c.Busy)throw new InvalidOperationException("请先停止并等待请求结束。");if(Ui.Confirm("删除本机保存的 AI 密钥？历史记录保留。")){_c.Secrets.Delete();key.Clear();connectionStatus.Text="密钥已删除。";}}));
        aiPanel.Children.Add(Ui.Text("每次请求输出最多512 tokens，上下文受保守预算限制。不自动重试，不后台推理。账单以服务商为准，请在服务商处设置额度。",11,Ui.Muted));content.Children.Add(Ui.Card(aiPanel));
        content.Children.Add(DataSettings());
        content.Children.Add(Ui.Card(Ui.Stack(Ui.Text("关于 Forme",17,null,true),Ui.Text("v0.1.0 · Windows x64\n没有账号、云同步或遥测。连接预设依据官方协议；真实服务可用性由你的测试连接确认。",12,Ui.Muted),Ui.Text("本地数据："+_c.Store.DirectoryPath,11,Ui.Muted),Ui.Button("打开数据目录",()=>Process.Start(new ProcessStartInfo{FileName=_c.Store.DirectoryPath,UseShellExecute=true})))));
        return content;
    }
    private UIElement DataSettings()
    {
        var chat=Ui.Check("聊天记录",true);var moods=Ui.Check("心情记录",true);var focus=Ui.Check("专注记录",true);var room=Ui.Check("伙伴与房间",true);
        var panel=Ui.Stack(Ui.Text("隐私与个人数据",17,null,true),Ui.Text("普通记录存于本机，未额外加密；密钥由 Windows 保护。我们不上传日志、不扫描磁盘，也不后台读取剪贴板或屏幕。",12,Ui.Muted),chat,moods,focus,room);
        CancellationTokenSource? exportRequest=null;var exportStatus=Ui.Text("",12,Ui.Muted);
        panel.Children.Add(Ui.Row(Ui.AsyncButton("导出所选数据",async()=>
        {
            if(!(chat.IsChecked==true||moods.IsChecked==true||focus.IsChecked==true||room.IsChecked==true))throw new InvalidOperationException("至少选择一类数据。");
            if(!Ui.Confirm("导出文件可能包含私人内容，文件不会包含密钥。请选择安全的保存位置。"))return;
            var dialog=new Microsoft.Win32.SaveFileDialog{Filter="Forme 数据 (*.json)|*.json",FileName=$"Forme-{DateTime.Now:yyyyMMdd}.json"};
            if(dialog.ShowDialog(this)==true)
            {
                bool includeChat=chat.IsChecked==true,includeMood=moods.IsChecked==true,includeFocus=focus.IsChecked==true,includeRoom=room.IsChecked==true;
                var request=new CancellationTokenSource();exportRequest=request;exportStatus.Text="正在导出，可以取消…";string directory=_c.Store.DirectoryPath;
                var temp=dialog.FileName+"."+Guid.NewGuid().ToString("N")+".tmp";
                try
                {
                    await Task.Run(async()=>
                    {
                        using var reader=new Store(directory,true);request.Token.ThrowIfCancellationRequested();
                        var data=reader.Export(includeChat,includeMood,includeFocus,includeRoom);request.Token.ThrowIfCancellationRequested();
                        using var output=File.Create(temp);await JsonSerializer.SerializeAsync(output,data,Store.JsonOptions,request.Token);
                    },request.Token);
                    request.Token.ThrowIfCancellationRequested();File.Move(temp,dialog.FileName,true);exportStatus.Text="导出完成，不包含密钥。";
                }
                catch(OperationCanceledException){exportStatus.Text="已取消导出。";}
                finally{if(File.Exists(temp))File.Delete(temp);exportRequest=null;request.Dispose();}
            }
        }),Ui.Button("导入数据",()=>
        {
            RequireIdle();var dialog=new Microsoft.Win32.OpenFileDialog{Filter="Forme 数据 (*.json)|*.json"};if(dialog.ShowDialog(this)!=true)return;
            var d=Store.ReadExport(dialog.FileName);string summary=$"聊天：{(d.Sessions is null?"保留":$"替换为{d.Sessions.Count}个会话")}\n心情：{(d.Moods is null?"保留":$"替换为{d.Moods.Count}条")}\n专注：{(d.Focus is null?"保留":$"替换为{d.Focus.Count}条")}\n房间：{(d.Room is null?"保留":"替换")}\n\n原内容将备份到 {_c.Store.BackupPath}。只替换文件包含的类别，不覆盖密钥。确认导入？";
            if(!Ui.Confirm(summary))return;_c.Store.Import(d);_c.AfterImport();Navigate("settings");Toast("导入完成，原数据恢复备份在数据目录中。");
        })));
        panel.Children.Add(Ui.Row(Ui.Button("取消导出",()=>exportRequest?.Cancel()),exportStatus));
        panel.Children.Add(Ui.Button("删除应用恢复备份",()=>{if(File.Exists(_c.Store.BackupPath)&&Ui.Confirm("删除应用保留的恢复备份？")){File.Delete(_c.Store.BackupPath);Toast("恢复备份已删除。");}}));
        panel.Children.Add(Ui.Row(Ui.Button("清空聊天",()=>Clear("chat")),Ui.Button("清空心情",()=>Clear("moods")),Ui.Button("清空专注记录",()=>Clear("focus"))));
        panel.Children.Add(Ui.Button("清除全部个人数据",()=>
        {
            RequireIdle();if(!Ui.Confirm("清除本机全部聊天、心情、专注、房间、应用备份与密钥？外部导出及服务商留存需自行处理。此操作无法撤销。"))return;
            StartupLink.Set(false);_c.ClearAll();Navigate("settings");Toast("本机个人数据已清除。");
        }));
        panel.Children.Add(Ui.Text("删除是应用层清除，不承诺存储介质取证级擦除。卸载保留数据，可先在这里清除。",11,Ui.Muted));return Ui.Card(panel);
    }
    private void RequireIdle(){if(_c.Busy||_c.Clock.Active)throw new InvalidOperationException("请先结束当前计时，并停止和等待 AI 请求结束。");}
    private void Clear(string category)
    {
        RequireIdle();if(!Ui.Confirm("清空此类本地记录和应用恢复备份？外部副本不受影响。"))return;_c.Store.ClearCategory(category);_c.AfterImport();Navigate("settings");Toast("记录已清空。");
    }
}
