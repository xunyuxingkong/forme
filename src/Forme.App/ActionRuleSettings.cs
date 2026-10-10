using Forme.Core;
using System.Windows;
using System.Windows.Controls;
namespace Forme.App;
internal sealed partial class MainWindow
{
    private UIElement ActionRuleSettings()
    {
        var enabled=Ui.Check("明确动作先由本地规则和意图解析处理（不调用AI）",_c.Preferences.LocalActionRules);
        enabled.Click+=(_,_)=>_c.SavePreferences(_c.Preferences with{LocalActionRules=enabled.IsChecked==true});
        var input=Ui.Input(_c.RulesText(),true,32768,"动作规则JSON");input.Height=240;input.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
        var status=Ui.Text(_c.ActionRulesStatus,12,Ui.Muted);
        return new Expander{Header="自然动作规则 · 可编辑",Content=Ui.Stack(enabled,Ui.Text("例：去水边、走到书柜那里、溜达一下。规则用于快捷命中和自定义；本地语义解析支持常见动作与目标组合，否定、引用和讨论不执行。无法完整理解的动作交给AI，只发送本次输入与固定意图说明；每次最多一个请求。整组先检查权限和路径，最多3步、30秒。规则不会上传，也不包含密钥。",12,Ui.Muted),Ui.Text(_c.ActionRulesPath,11,Ui.Muted),status,input,Ui.Row(Ui.Button("保存动作规则",()=>{_c.SaveRules(input.Text);status.Text=_c.ActionRulesStatus;Toast("本地动作规则已保存。");}),Ui.Button("重新加载文件",()=>{_c.LoadActionRules();input.Text=_c.RulesText();status.Text=_c.ActionRulesStatus;}),Ui.Button("恢复默认规则",()=>{_c.SaveRules(ActionRules.Default().Serialize());input.Text=_c.RulesText();status.Text=_c.ActionRulesStatus;})))};
    }
}
