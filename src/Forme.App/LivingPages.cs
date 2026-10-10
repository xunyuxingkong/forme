using Forme.Core;
using System.Windows;
using System.Windows.Controls;
namespace Forme.App;
internal sealed partial class MainWindow
{
    private Action? _worldPageUpdate;
    private void DetachWorldPage(){if(_worldPageUpdate is {} update)_room.WorldChanged-=update;_worldPageUpdate=null;}
    private UIElement FurniturePage()
    {
        _room.OpenFurniture();var select=new ComboBox{Margin=new Thickness(0,0,0,10),MinHeight=36};System.Windows.Automation.AutomationProperties.SetName(select,"选择家具");var status=Ui.Text("",12);bool sync=false;
        void adjust(Func<FurnitureItem,FurnitureItem> edit){var next=_room.CurrentWorld.Copy();int index=next.Items.FindIndex(i=>i.Id==_room.SelectedFurniture);if(index<0)return;next.Items[index]=edit(next.Items[index]);_room.ChangeFurniture(next);}
        select.SelectionChanged+=(_,_)=>{if(!sync&&select.SelectedItem is ComboBoxItem item)_room.SelectFurniture((string)item.Tag);};
        _worldPageUpdate=()=>{sync=true;select.Items.Clear();foreach(var item in _room.CurrentWorld.Items)select.Items.Add(new ComboBoxItem{Content=LivingWorld.Kind(item.Kind).Name+" · "+item.X.ToString("0.00")+", "+item.Z.ToString("0.00"),Tag=item.Id,IsSelected=item.Id==_room.SelectedFurniture});sync=false;status.Text=_room.CurrentWorld.PlacementProblem()??"通道和摆放检查通过。当前预览尚未保存。";};_room.WorldChanged+=_worldPageUpdate;_worldPageUpdate();
        void copy(){var next=_room.CurrentWorld.Copy();var item=next.Items.FirstOrDefault(i=>i.Id==_room.SelectedFurniture);if(item is null)return;if(next.Items.Count>=24)throw new InvalidDataException("最多24件家具。");var copied=item with{Id=Guid.NewGuid().ToString("N"),X=Math.Clamp(item.X+.5,-3.4,3.4),Z=Math.Clamp(item.Z+.5,-2.8,4.6)};next.Items.Add(copied);_room.ChangeFurniture(next);_room.SelectFurniture(copied.Id);}
        void reading(){var next=LivingWorld.Default() with{Discoveries=_room.CurrentWorld.Discoveries.ToList()};for(int i=0;i<next.Items.Count;i++)next.Items[i]=next.Items[i].Kind switch{"book"=>next.Items[i] with{X=.2,Z=0},"cushion"=>next.Items[i] with{X=-.8,Z=0},"lamp"=>next.Items[i] with{X=.9,Z=.5},_=>next.Items[i]};_room.ChangeFurniture(next);_room.SelectFurniture("cushion");}
        return Ui.Stack(Ui.Text("布置会产生生活",24,null,true),Ui.Text("拖动家具移动，按0.25m吸附；按钮也可操作。金色框为选中家具，红框表示摆放有问题。Esc取消拖动，Ctrl+Z撤销。",13,Ui.Muted),Ui.Card(Ui.Stack(select,status,
            Ui.Row(Ui.Button("家具向左",()=>adjust(i=>i with{X=i.X-.25})),Ui.Button("家具向右",()=>adjust(i=>i with{X=i.X+.25})),Ui.Button("家具向后",()=>adjust(i=>i with{Z=i.Z-.25})),Ui.Button("家具向前",()=>adjust(i=>i with{Z=i.Z+.25}))),
            Ui.Row(Ui.Button("旋转90°",()=>adjust(i=>i with{Rotation=(i.Rotation+90)%360})),Ui.Button("复制家具",copy),Ui.Button("撤销家具",_room.UndoFurniture),Ui.Button("收纳选中家具",()=>{var next=_room.CurrentWorld.Copy();next.Items.RemoveAll(i=>i.Id==_room.SelectedFurniture);_room.ChangeFurniture(next);_room.SelectFurniture(next.Items.FirstOrDefault()?.Id);})),
            Ui.Row(Ui.Button("保存家具布置",()=>{_room.SaveFurniture();Toast("家具布置已保存。");},true),Ui.Button("取消预览",()=>Navigate("living")),Ui.Button("恢复基础摆放",()=>_room.ChangeFurniture(LivingWorld.Default() with{Discoveries=_room.CurrentWorld.Discoveries.ToList()}))))),
            Ui.Row(Ui.Button("阅读角模板",reading)),Ui.Text("模板以基础6件家具替换当前草稿，组合成阅读角；可撤销，保存后生效。雨天开灯还可发现听雨读书。",12,Ui.Muted),Ui.Text("家具盒 · 点击放回场景",17,null,true),Ui.Row(LivingWorld.Catalog.Select(kind=>Ui.Button("添加"+kind.Name,()=>{var next=_room.CurrentWorld.Copy();next.Items.Add(new(Guid.NewGuid().ToString("N"),kind.Id,0,2.5,0));_room.ChangeFurniture(next);_room.SelectFurniture(next.Items.Last().Id);})).ToArray()),
            Ui.Text("保存前检查重叠和可达性。移走家具会中止当前活动，取消预览恢复上次保存。书桌、沙发、墙体和前庭固定设施暂不在此编辑器内。",12,Ui.Muted));
    }
    private UIElement LivingPage()
    {
        var world=_c.Store.World();var p=_c.Preferences;bool night=p.Theme=="night"||p.Theme=="auto"&&(DateTime.Now.Hour>=19||DateTime.Now.Hour<7);var events=LifeRules.Evaluate(world,p.Weather,night,p.RoomLamp);
        var panel=Ui.Stack(Ui.Text("小屋里的新生活",24,null,true),Ui.Text("摆放关系会改变可发生的事件。事件全部本地执行，没有签到和失败惩罚。",13,Ui.Muted),Ui.Row(Ui.Button("布置家具",()=>Navigate("furniture"),true),Ui.Button("伙伴藏物",()=>Navigate("hide")),Ui.Button("玩纸船",()=>Navigate("boat"))),Ui.Row(Ui.Button("叫回伙伴",_room.RecallFromUser),Ui.Button("停止当前活动",_room.StopFromUser)),Ui.Text("当前能发生的生活",18,null,true));
        foreach(var life in events)panel.Children.Add(Ui.Card(Ui.Stack(Ui.Text(life.Title,17,null,true),Ui.Text(life.Description,12,Ui.Muted),Ui.Button("体验"+life.Title,()=>{_room.SwitchScene(false);_room.RunLife(life);} ))));
        if(events.Count==0)panel.Children.Add(Ui.Text("把坐垫靠近书架或鱼缸，把小窝放到暖灯旁，试试新组合。",13,Ui.Muted));
        panel.Children.Add(Ui.Card(Ui.Stack(Ui.Text("生活图鉴 · "+world.Discoveries.Count+"/8",18,null,true),Ui.Text(string.Join("\n",new[]{"reading|阅读角：坐垫靠近书架","rain-reading|听雨读书：阅读角＋暖灯＋雨天","sun-nap|晒太阳：白天晴天，坐垫靠窗","snack-rest|零食小憩：零食碗靠近小窝","fish-watch|看小鱼：坐垫靠近鱼缸","warm-nap|暖灯晚安：小窝旁亮着灯","night-book|夜读：书架旁亮灯＋夜晚","garden-break|花园歇脚：坐垫靠近前庭"}.Select(text=>(world.Discoveries.Contains(text.Split('|')[0])?"✓ ":"○ ")+text.Split('|')[1])),12,Ui.Muted))));
        panel.Children.Add(Ui.Card(Ui.Stack(Ui.Text("抛接小球",18,null,true),Ui.Text("拿起后按住Shift拖动小球，松开抛出；金色点是落点。也可使用方向按钮。伙伴会等球落地后去捡。",12,Ui.Muted),Ui.Row(Ui.Button("拿起小球",_room.PickToy),Ui.Button("向左抛球",()=>_room.ThrowToy(new(-.8,1))),Ui.Button("向右抛球",()=>_room.ThrowToy(new(1,1))),Ui.Button("向前抛球",()=>_room.ThrowToy(new(0,2.5)))))));
        panel.Children.Add(Ui.Button("重置生活图鉴",()=>{if(Ui.Confirm("清除8条生活发现及应用恢复备份？家具和聊天保留，外部导出需自行删除。")){_c.Store.ResetLife();_c.Refresh();}}));
        return panel;
    }
    private UIElement HidePage()
    {
        if(_room.Outdoors)_room.SwitchScene(false);var select=new ComboBox{Margin=new Thickness(0,0,0,10),MinHeight=36};System.Windows.Automation.AutomationProperties.SetName(select,"藏物家具");foreach(var item in _room.CurrentWorld.Items)select.Items.Add(new ComboBoxItem{Content=LivingWorld.Kind(item.Kind).Name+" · "+item.X.ToString("0.0")+","+item.Z.ToString("0.0"),Tag=item.Id});select.SelectedIndex=0;string? chosen()=>select.SelectedItem is ComboBoxItem item?(string)item.Tag:null;
        var state=Ui.Text(_room.HideStatus,14);_worldPageUpdate=()=>state.Text=_room.HideStatus;_room.WorldChanged+=_worldPageUpdate;
        return Ui.Stack(Ui.Text("伙伴藏物",24,null,true),Ui.Text("家具位置决定线索与搜索路线。奶糖先去鱼缸看看，芽芽先检查坐垫；有通道才能找过去。可以随时结束，无次数限制。",13,Ui.Muted),Ui.Card(Ui.Stack(state,select,Ui.Row(Ui.Button("我藏，伙伴找",()=>_room.StartHide(false,chosen()),true),Ui.Button("伙伴藏，我来找",()=>_room.StartHide(true,null)),Ui.Button("检查选中家具",()=>{if(chosen() is {} id)_room.GuessHidden(id);})),Ui.Row(Ui.Button("结束藏物",()=>{if(_c.Busy)_c.StopReply();_room.EndHide();}),Ui.Button("叫回伙伴",_room.RecallFromUser)))),Ui.Button("调整家具再玩",()=>Navigate("furniture")));
    }
}
