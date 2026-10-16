using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace Forme.App;

internal sealed class FloatingChatWindow : Window
{
    private readonly Controller _c;
    private readonly Action<string> _openHouse;
    private readonly TextBlock _target=Ui.Text("",11,Ui.Muted);
    private readonly TextBlock _status=Ui.Text("",11,Ui.Muted);
    private readonly StackPanel _messages=new();
    private readonly ScrollViewer _scroll;
    private readonly TextBox _input;
    private readonly StackPanel _attachments=new();
    private readonly Button _pick;
    private readonly Button _send,_stop,_new,_choose;
    private readonly Expander _historyActions;
    private readonly ContentControl _taskCard=new();
    private bool _taskCardExpanded;
    private TextBlock? _live;
    private bool _transfer,_composing,_probeStreaming;
    public FloatingChatWindow(Controller c,PetWindow pet,Action<string> openHouse)
    {
        _c=c;_openHouse=openHouse;Title="Forme · 聊一会儿";Width=380;Height=500;MinWidth=300;MinHeight=360;
        WindowStyle=WindowStyle.ToolWindow;ShowInTaskbar=false;Topmost=c.Preferences.Topmost;Background=Ui.Cream;Owner=pet;
        var grid=new Grid{Margin=new Thickness(14)};
        foreach(var height in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto,GridLength.Auto})grid.RowDefinitions.Add(new(){Height=height});
        void Row(UIElement child,int row){Grid.SetRow(child,row);grid.Children.Add(child);}
        Row(_target,0);
        _new=Ui.Button("新对话",c.NewSession);
        Row(Ui.Row(_new,Ui.Button("完整聊天",()=>openHouse("chat")),Ui.Button("角色设置",()=>openHouse("settings")),Ui.Button("记忆",()=>openHouse("memory"))),1);
        Row(_taskCard,2);
        _historyActions=new(){Header="聊天记录管理",Margin=new Thickness(0,0,0,8)};System.Windows.Automation.AutomationProperties.SetName(_historyActions,"聊天记录管理");Row(_historyActions,3);
        _scroll=new(){Name="聊天记录滚动区",Content=_messages,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};var transcriptFrame=ChatTranscript.Frame(_scroll);transcriptFrame.Margin=new Thickness(0,0,0,8);Row(transcriptFrame,4);
        _scroll.SizeChanged+=(_,_)=>UpdateTaskCardSize();
        Row(_status,5);
        _input=Ui.Input(c.Draft,true);_input.MinHeight=70;_input.MaxHeight=110;_input.TextChanged+=(_,_)=>c.Draft=_input.Text;Row(Ui.Stack(_attachments,_input),6);
        _send=Ui.AsyncButton("发送",()=>ChatRequests.Send(c),true);_stop=Ui.Button("停止",c.StopReply);
        _pick=AttachmentControls.PickButton(c,this);_choose=ActivitySelectionUi.Button(c,this,Forme.Core.CompanionView.Desktop);
        Row(Ui.Stack(Ui.Row(_send,_stop,_pick,Ui.Button("发送预览",()=>ChatRequests.Preview(c,this)),_choose),Ui.Text("附件仅本次上传 · 图片/PDF用量另计 · 不执行动作",10,Ui.Muted)),7);
        Content=new Border{Background=Ui.Cream,Child=grid};
        _input.PreviewTextInput+=(_,_)=>_c.NotifyActivityInteraction();
        _input.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Back or Key.Delete)_c.NotifyActivityInteraction();};
        TextCompositionManager.AddPreviewTextInputStartHandler(_input,(_,_)=>{_c.NotifyActivityInteraction();_composing=true;});
        TextCompositionManager.AddPreviewTextInputHandler(_input,(_,_)=>_composing=false);
        _input.PreviewKeyDown+=async(_,e)=>
        {
            if(e.Key!=Key.Enter||Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)||_composing)return;
            e.Handled=true;
            if(!_send.IsEnabled)return;
            try{await ChatRequests.Send(c);}catch(Exception ex) when(Forme.Core.OperationErrors.Expected(ex)){Ui.Error(Forme.Core.OperationErrors.Message(ex));}
        };
        SourceInitialized+=(_,_)=>
        {
            var handle=new WindowInteropHelper(this).Handle;
            var petHandle=new WindowInteropHelper(pet).Handle;
            if(!ScreenCoordinateService.TryGetWindowRect(petHandle,out var anchor)||!ScreenCoordinateService.TryGetWindowRect(handle,out var rectangle))return;
            var area=ScreenCoordinateService.WorkAreaAt(anchor);
            int x=anchor.Right+12;if(x+rectangle.Width>area.Right)x=anchor.Left-rectangle.Width-12;
            x=Math.Clamp(x,area.Left,Math.Max(area.Left,area.Right-rectangle.Width));
            int y=Math.Clamp(anchor.Top,area.Top,Math.Max(area.Top,area.Bottom-rectangle.Height));
            ScreenCoordinateService.SetBounds(handle,x,y,Math.Min(rectangle.Width,area.Width),Math.Min(rectangle.Height,area.Height));
        };
        c.Changed+=Render;c.Tick+=UpdateLive;
        Closing+=(_,_)=>{if(!_transfer&&c.Busy)c.StopReply();};
        Closed+=(_,_)=>{c.Changed-=Render;c.Tick-=UpdateLive;};
        Render();Loaded+=(_,_)=>_input.Focus();
    }
    public void CloseForTransfer(){_transfer=true;Close();}
    internal void StartProbeStreamingMock()
    {
        _probeStreaming=true;_live=Ui.Text("模拟生成中的回复 · 本机测试，不发送网络请求",13);
        _messages.Children.Add(ChatTranscript.Entry(_c.Preferences.PetName,_live));_scroll.ScrollToEnd();
    }
    private void Render()
    {
        Topmost=_c.Preferences.Topmost;
        UpdateTaskCardSize();
        _taskCard.Content=TaskCardUi.Create(_c,_openHouse,_taskCardExpanded,value=>{_taskCardExpanded=value;UpdateTaskCardSize();},true);
        _target.Text="“小屋里走走”控制3D小屋伙伴；“桌面／屏幕上走一圈”控制桌面伙伴。停止/取消/召回与自定义快捷规则本地执行\nAI理解与聊天 · "+_c.Preferences.Endpoint+"\n"+_c.Preferences.Model+(_c.Preferences.MemoryEnabled?" · 记忆开启":" · 记忆关闭");
        _messages.Children.Clear();_live=null;
        var history=_c.Session is null?[]:_c.Store.Messages(_c.Session.Id,0,20);
        string RecordText()=>ChatTranscript.Text(history,_c.Preferences.PetName,_c.PendingChatSave);
        _historyActions.Content=ChatTranscript.Actions(RecordText,draft=>{_c.MemoryDraft=draft;_openHouse("memory");},RecordText().Length>0);
        foreach(var message in history)
        {
            if(message.Status=="streaming"&&_c.Busy)continue;
            string visible=ChatDisplay.RecordMessage(message);
            if(visible.Length==0)continue;
            _messages.Children.Add(ChatTranscript.Entry(message.Role=="user"?null:_c.Preferences.PetName,Ui.Text(visible,13),message.Status=="stopped"?"已停止":message.Status=="error"?"未完成":null,user:message.Role=="user"));
        }
        if(!_c.Secrets.Exists&&history.Count==0&&!_c.Busy)_messages.Children.Add(Ui.Text("明确动作可按本地规则执行，例如“走两步”“跳个舞”；先开启对应权限。自由对话需在设置连接AI，调用可能收费。",12,Ui.Muted));
        else if(history.Count==0&&!_c.Busy)_messages.Children.Add(Ui.Text("我在这里，听你说。分享范围可在发送预览中查看。",13,Ui.Muted));
        if(_c.PendingChatSave is {} unsaved)_messages.Children.Add(Ui.Stack(ChatTranscript.Entry(_c.Preferences.PetName,Ui.Text(ChatDisplay.Message(unsaved.Content),13),"回复还未保存到磁盘"),Ui.Row(Ui.Button("重试保存",_c.RetryChatSave),Ui.Button("放弃保存",()=>{if(Ui.Confirm("放弃这条暂存回复的保存？"))_c.DiscardPendingSave();}))));
        if(_c.Busy){_live=Ui.Text("…",13);_messages.Children.Add(ChatTranscript.Entry(_c.Preferences.PetName,_live));}
        if(_input.Text!=_c.Draft)_input.Text=_c.Draft;
        _attachments.Children.Clear();_attachments.Children.Add(AttachmentControls.List(_c,this));_pick.IsEnabled=!_c.Busy;
        _send.IsEnabled=!_c.ForegroundBusy;_choose.IsEnabled=!_c.ForegroundBusy;_stop.IsEnabled=_c.Busy;_new.IsEnabled=!_c.Busy;
        UpdateLive();_scroll.ScrollToEnd();
    }
    private void UpdateLive()
    {
        if(_live is not null&&(_probeStreaming||_c.Busy)&&IsVisible&&WindowState!=WindowState.Minimized){_live.Text=_probeStreaming?"模拟生成中的回复 · 本机测试，不发送网络请求":_c.LiveReply.Length==0?"…":ChatDisplay.Message(_c.LiveReply);_scroll.ScrollToEnd();}
        _status.Text=ChatDisplay.Status(_c.ChatStatus);if(_c.Waiting&&_status.Text.Length>0)_status.Text+=" 可随时停止。";
    }
    private void UpdateTaskCardSize()
    {
        double available=SystemParameters.WorkArea.Height;
        double needed=_c.TaskCard() is null?360:Math.Max(600,_scroll.IsLoaded?ActualHeight-_scroll.ActualHeight+100:600);
        double minimum=Math.Min(needed,available);
        if(Math.Abs(MinHeight-minimum)>.5)MinHeight=minimum;
    }
}
