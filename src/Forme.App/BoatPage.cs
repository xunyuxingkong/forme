using Forme.Core;
using System.Windows;
using System.Windows.Controls;

namespace Forme.App;

internal sealed partial class MainWindow
{
    private Action? _boatPageUpdate;
    private void DetachBoatPage(){if(_boatPageUpdate is not null){_room.BoatChanged-=_boatPageUpdate;_boatPageUpdate=null;}}
    private UIElement BoatPage()
    {
        _room.OpenBoats();var layout=_room.BoatSettings;
        var status=Ui.Text(_room.BoatStatus,13,Ui.Sage,true);
        var shape=Ui.Select(new[]{"小尖舟 · 轻快","宽叶舟 · 悠闲"},layout.Shape=="wide"?"宽叶舟 · 悠闲":"小尖舟 · 轻快","纸船船形");
        var color=Ui.Select(new[]{"晨光黄","花瓣粉","薄荷绿"},layout.Color=="rose"?"花瓣粉":layout.Color=="mint"?"薄荷绿":"晨光黄","纸船颜色");
        var wind=Ui.Select(new[]{"吹向柳树","平缓向前","吹向睡莲"},layout.Wind<0?"吹向柳树":layout.Wind>0?"吹向睡莲":"平缓向前","纸船风向");
        bool sync=false;
        void ApplyChoices(){if(sync)return;Ui.Guard(()=>_room.ConfigureBoat(_room.BoatSettings with{Shape=shape.SelectedIndex==1?"wide":"swift",Color=color.SelectedIndex==1?"rose":color.SelectedIndex==2?"mint":"sun",Wind=wind.SelectedIndex-1}));}
        shape.SelectionChanged+=(_,_)=>ApplyChoices();color.SelectionChanged+=(_,_)=>ApplyChoices();wind.SelectionChanged+=(_,_)=>ApplyChoices();
        var leaf=Ui.Select(new[]{"第1片叶子","第2片叶子","第3片叶子"},"第1片叶子","选择导流叶片");var leafInfo=Ui.Text("",12,Ui.Muted);
        void EditLeaf(double dx,double dz,double angle)
        {
            var current=_room.BoatSettings;var parts=current.Leaves.ToList();var old=parts[leaf.SelectedIndex];
            parts[leaf.SelectedIndex]=old with{X=Math.Clamp(old.X+dx,-2,2),Z=Math.Clamp(old.Z+dz,-1.35,1.35),Angle=Math.Clamp(old.Angle+angle,-70,70)};
            _room.ConfigureBoat(current with{Leaves=parts});
        }
        var edit=Ui.Stack(leaf,leafInfo,Ui.Row(Ui.Button("叶片左移",()=>EditLeaf(-.3,0,0)),Ui.Button("叶片右移",()=>EditLeaf(.3,0,0))),Ui.Row(Ui.Button("叶片向柳树",()=>EditLeaf(0,-.3,0)),Ui.Button("叶片向睡莲",()=>EditLeaf(0,.3,0))),Ui.Row(Ui.Button("导流转向柳树",()=>EditLeaf(0,0,-20)),Ui.Button("导流转向睡莲",()=>EditLeaf(0,0,20))));
        var start=Ui.Button("放船，开始旅行",_room.StartBoat,true);var pause=Ui.Button("暂停纸船",_room.PauseBoat);var retrieve=Ui.Button("捞回纸船",()=>{if(_c.Busy)_c.StopReply();_room.RetrieveBoat();});
        var undo=Ui.Button("撤销上次布置",_room.UndoBoat);var reset=Ui.Button("恢复初始路线",()=>_room.ConfigureBoat(BoatLayout.Default()));
        var title=Ui.Input(_room.LastBoat?.Title??"",max:40,automationName:"纪念船名字");string? titleRun=_room.LastBoat?.Id;
        var keep=Ui.Button("收藏这次旅行",()=>
        {
            if(_room.LastBoat is not {} completed)throw new OperationFailureException("先完成一次纸船旅行。");
            var item=completed with{Title=string.IsNullOrWhiteSpace(title.Text)?completed.Title:title.Text.Trim()};_c.Store.KeepBoat(item);_room.NameLastBoat(item.Title);_c.Refresh();Navigate("boat");Toast("纪念船已收藏并摆进小屋。配置保存在本机，可以重玩。");
        },true);
        _boatPageUpdate=()=>
        {
            var current=_room.BoatSettings;bool running=_room.BoatRunning;
            status.Text=_room.BoatStatus;shape.IsEnabled=color.IsEnabled=wind.IsEnabled=edit.IsEnabled=undo.IsEnabled=reset.IsEnabled=!running;
            start.IsEnabled=!running;pause.IsEnabled=retrieve.IsEnabled=running;pause.Content=_room.BoatPaused?"继续纸船":"暂停纸船";System.Windows.Automation.AutomationProperties.SetName(pause,(string)pause.Content);
            keep.IsEnabled=_room.LastBoat is not null;
            if(_room.LastBoat is {} completed&&titleRun!=completed.Id){titleRun=completed.Id;title.Text=completed.Title;}
            var selected=current.Leaves[leaf.SelectedIndex];leafInfo.Text=$"位置 {selected.X:0.0}, {selected.Z:0.0} · 导流角度 {selected.Angle:0}°";
            sync=true;shape.SelectedIndex=current.Shape=="wide"?1:0;color.SelectedIndex=current.Color=="rose"?1:current.Color=="mint"?2:0;wind.SelectedIndex=current.Wind+1;sync=false;
        };
        _room.BoatChanged+=_boatPageUpdate;leaf.SelectionChanged+=(_,_)=>_boatPageUpdate?.Invoke();_boatPageUpdate();
        var content=Ui.Stack(Ui.Text("雨天纸船",26,null,true),Ui.Text("折一只小船，决定它去哪里。伙伴会沿岸陪着你。",13,Ui.Muted),Ui.Card(Ui.Stack(status,Ui.Row(start,pause,retrieve))),
            Ui.Card(Ui.Stack(Ui.Text("1 · 船与风",17,null,true),shape,color,wind)),
            Ui.Card(Ui.Stack(Ui.Text("2 · 改变水流",17,null,true),Ui.Text("直接拖动池塘中的三片绿叶；叶片方向决定导流。也可以用下面的按钮布置。浅色虚线会立即预览新路线。试着经过粉色小花瓣，会有额外发现。",12,Ui.Muted),edit,Ui.Row(undo,reset))),
            Ui.Card(Ui.Stack(Ui.Text("3 · 留一件纪念",17,null,true),Ui.Text("完成后可为这次旅行命名并收藏；不会自动上传给 AI。最多保留60次旅行，可删除整理。",12,Ui.Muted),title,keep)));
        var album=_c.Store.Boats();content.Children.Add(Ui.Text($"旅行收藏 · {album.Entries.Count}/60",18,null,true));
        foreach(var item in album.Entries)
        {
            content.Children.Add(Ui.Card(Ui.Stack(Ui.Text(item.Title,16,null,true),Ui.Text($"{item.Created:MM-dd HH:mm} · {PaperBoat.DockName(item.Dock)}{(item.PetalFound?" · 带回小花瓣":"")}{(item.Id==album.DisplayedId?" · 正在小屋陈列":"")}",12,Ui.Muted),Ui.Row(
                Ui.Button("重玩这条路线",()=>{_room.RetrieveBoat();_room.ConfigureBoat(item.Layout);Toast("已恢复这次旅行的船形、颜色、风向和叶片。点击放船重玩。");}),
                Ui.Button("摆进小屋",()=>{_c.Store.DisplayBoat(item.Id);_c.Refresh();Navigate("boat");Toast("纪念船已摆进小屋。返回小屋可以点击查看。");}),
                Ui.Button("删除纪念船",()=>{if(Ui.Confirm("删除这次纪念船及应用恢复备份？外部导出的文件不在删除范围。")){_c.Store.DeleteBoat(item.Id);_c.Refresh();Navigate("boat");}})))));
        }
        content.Children.Add(Ui.Button("回到小屋",()=>{Navigate("home");_room.SwitchScene(false);}));
        return content;
    }
}
