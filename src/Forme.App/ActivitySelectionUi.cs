using Forme.Core;
using System.Windows;
using System.Windows.Controls;

namespace Forme.App;

internal static class ActivitySelectionUi
{
    public static Button Button(Controller c,Window owner,CompanionView view)
    {
        string Goal()=>string.IsNullOrWhiteSpace(c.Draft)?"你看现在适合做什么，自己选一个短活动。":c.Draft.Trim();
        var button=Ui.AsyncButton("自主选择",async()=>await c.ChooseActivity(view,Goal()));
        button.IsEnabled=!c.ForegroundBusy;
        button.ToolTip=(view==CompanionView.Room?"目标：当前小屋/户外伙伴。":"目标：桌面伙伴。")+"按输入的目标选择一个当前可用活动；输入为空时由伙伴自己选。需要开启游戏状态分享与所需动作权限。每次一次 AI 请求。";
        var menu=new ContextMenu();
        var preview=new MenuItem{Header="预览这次自主选择会发送什么"};
        preview.Click+=(_,_)=>Ui.Guard(()=>{
            var turns=c.PreviewActivity(view,Goal());
            var content=Ui.Input(string.Join("\n\n",turns.Select(x=>"["+x.Role+"]\n"+x.Preview)),true,100000);
            content.IsReadOnly=true;
            new Window{Title="自主选择发送预览 · 尚未联网",Owner=owner,Width=620,Height=520,WindowStartupLocation=WindowStartupLocation.CenterOwner,
                Content=Ui.Card(Ui.Stack(Ui.Text("只发送本次目标和允许的游戏状态；不附历史、记忆、附件或工作记录。",12,Ui.Muted),content))}.ShowDialog();
        });
        menu.Items.Add(preview);button.ContextMenu=menu;
        return button;
    }
}
