using Forme.Core;
using System.Windows;
using System.Windows.Controls;
namespace Forme.App;
internal sealed partial class MainWindow
{
    private UIElement ActionRuleSettings()
    {
        var enabled=Ui.Check("AI未连接时启用本地动作快捷规则",_c.Preferences.LocalActionRules);
        enabled.Click+=(_,_)=>_c.SavePreferences(_c.Preferences with{LocalActionRules=enabled.IsChecked==true});
        var input=Ui.Input(_c.RulesText(),true,32768,"动作规则JSON");input.Height=240;input.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
        var status=Ui.Text(_c.ActionRulesStatus,12,Ui.Muted);
        return new Expander{Header="自然动作规则 · 可编辑",Content=Ui.Stack(enabled,Ui.Text("连接AI后，停止/取消/召回和已保存的自定义快捷规则在本地执行，不联网；其他自然语言交给AI识别。未配置AI时，可用内置本地语义解析。否定、引用和讨论不会执行。AI只发送当前消息及限量会话上下文，每次至多一个请求；动作整组预检权限与路径，最多6个展开动作、30秒。规则文件不上传，也不含密钥。",12,Ui.Muted),Ui.Text(_c.ActionRulesPath,11,Ui.Muted),status,input,Ui.Row(Ui.Button("保存动作规则",()=>{_c.SaveRules(input.Text);status.Text=_c.ActionRulesStatus;Toast("本地动作规则已保存。");}),Ui.Button("重新加载文件",()=>{_c.LoadActionRules();input.Text=_c.RulesText();status.Text=_c.ActionRulesStatus;}),Ui.Button("恢复默认规则",()=>{_c.SaveRules(ActionRules.Default().Serialize());input.Text=_c.RulesText();status.Text=_c.ActionRulesStatus;})))};
    }
}
