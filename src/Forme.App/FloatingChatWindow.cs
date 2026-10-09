using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace Forme.App;

internal sealed class FloatingChatWindow : Window
{
    private readonly Controller _c;
    private readonly TextBlock _target=Ui.Text("",11,Ui.Muted);
    private readonly TextBlock _status=Ui.Text("",11,Ui.Muted);
    private readonly StackPanel _messages=new();
    private readonly ScrollViewer _scroll;
    private readonly TextBox _input;
    private readonly Button _send,_stop,_new;
    private TextBlock? _live;
    private bool _transfer,_composing,_probeStreaming;
    public FloatingChatWindow(Controller c,PetWindow pet,Action<string> openHouse)
    {
        _c=c;Title="Forme · 聊一会儿";Width=380;Height=500;MinWidth=300;MinHeight=360;
        WindowStyle=WindowStyle.ToolWindow;ShowInTaskbar=false;Topmost=c.Preferences.Topmost;Background=Ui.Cream;Owner=pet;
        var grid=new Grid{Margin=new Thickness(14)};
        foreach(var height in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto,GridLength.Auto})grid.RowDefinitions.Add(new(){Height=height});
        void Row(UIElement child,int row){Grid.SetRow(child,row);grid.Children.Add(child);}
        Row(_target,0);
        _new=Ui.Button("新对话",c.NewSession);
        Row(Ui.Row(_new,Ui.Button("完整聊天",()=>openHouse("chat")),Ui.Button("AI设置",()=>openHouse("settings"))),1);
        _scroll=new(){Content=_messages,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,0,0,8)};Row(_scroll,2);
        Row(_status,3);
        _input=Ui.Input(c.Draft,true);_input.MinHeight=70;_input.MaxHeight=110;_input.TextChanged+=(_,_)=>c.Draft=_input.Text;Row(_input,4);
        _send=Ui.AsyncButton("发送",()=>ChatRequests.Send(c),true);_stop=Ui.Button("停止",c.StopReply);
        Row(Ui.Row(_send,_stop,Ui.Text("Enter发送 · Shift+Enter换行",10,Ui.Muted)),5);
        Content=new Border{Background=Ui.Cream,Child=grid};
        TextCompositionManager.AddPreviewTextInputStartHandler(_input,(_,_)=>_composing=true);
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
        _messages.Children.Add(Ui.Card(Ui.Stack(Ui.Text("本机流式模拟",10,Ui.Muted),_live),new Thickness(10)));_scroll.ScrollToEnd();
    }
    private void Render()
    {
        Topmost=_c.Preferences.Topmost;
        _target.Text="发送至 "+_c.Preferences.Endpoint+"\n"+_c.Preferences.Model;
        _messages.Children.Clear();_live=null;
        var history=_c.Session is null?[]:_c.Store.Messages(_c.Session.Id,0,20);
        foreach(var message in history)
        {
            if(message.Status=="streaming"&&_c.Busy)continue;
            var text=Ui.Text(message.Content.Length==0?"未收到内容":message.Content,13);text.Margin=new Thickness(0);
            var label=message.Role=="user"?"你":_c.Preferences.PetName;
            if(message.Status is "stopped" or "error")label+=message.Status=="stopped"?" · 已停止":" · 未完成";
            _messages.Children.Add(Ui.Card(Ui.Stack(Ui.Text(label,10,Ui.Muted),text),new Thickness(10)));
        }
        if(!_c.Secrets.Exists)_messages.Children.Add(Ui.Text("还没有连接AI。在AI设置中填写密钥后即可聊天；调用可能收费。",12,Ui.Muted));
        else if(history.Count==0&&!_c.Busy)_messages.Children.Add(Ui.Text("我在这里，听你说。这里只分享你发送的内容。",13,Ui.Muted));
        if(_c.Busy){_live=Ui.Text("…",13);_messages.Children.Add(Ui.Card(Ui.Stack(Ui.Text(_c.Preferences.PetName,10,Ui.Muted),_live),new Thickness(10)));}
        if(_input.Text!=_c.Draft)_input.Text=_c.Draft;
        _send.IsEnabled=!_c.Busy&&_c.Secrets.Exists;_stop.IsEnabled=_c.Busy;_new.IsEnabled=!_c.Busy;
        UpdateLive();_scroll.ScrollToEnd();
    }
    private void UpdateLive()
    {
        if(_live is not null&&(_probeStreaming||_c.Busy)&&IsVisible&&WindowState!=WindowState.Minimized){_live.Text=_probeStreaming?"模拟生成中的回复 · 本机测试，不发送网络请求":_c.LiveReply.Length==0?"…":_c.LiveReply;_scroll.ScrollToEnd();}
        _status.Text=_c.Waiting?"正在等待服务，可随时停止。":_c.ChatStatus;
    }
}
