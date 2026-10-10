using Forme.Core;
using System.Windows;
using System.Windows.Controls;
namespace Forme.App;
internal sealed partial class MainWindow
{
    private UIElement ActionRuleSettings()
    {
        var enabled=Ui.Check("明确动作先匹配本地规则（不调用AI）",_c.Preferences.LocalActionRules);
        enabled.Click+=(_,_)=>_c.SavePreferences(_c.Preferences with{LocalActionRules=enabled.IsChecked==true});
        var input=Ui.Input(_c.RulesText(),true,32768,"动作规则JSON");input.Height=240;input.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
        var status=Ui.Text(_c.ActionRulesStatus,12,Ui.Muted);
        return new Expander{Header="自然动作规则 · 可编辑",Content=Ui.Stack(enabled,Ui.Text("例：你能走两步吗、睡觉吧、跳个舞、随机走动、陪我玩藏物。只匹配完整短句及明确连接的最多3步；不从普通聊天中扫描关键词。未匹配则使用AI。宠物/玩法/场景权限仍需开启。规则不会上传，也不包含密钥。",12,Ui.Muted),Ui.Text(_c.ActionRulesPath,11,Ui.Muted),status,input,Ui.Row(Ui.Button("保存动作规则",()=>{_c.SaveRules(input.Text);status.Text=_c.ActionRulesStatus;Toast("本地动作规则已保存。");}),Ui.Button("重新加载文件",()=>{_c.LoadActionRules();input.Text=_c.RulesText();status.Text=_c.ActionRulesStatus;}),Ui.Button("恢复默认规则",()=>{_c.SaveRules(ActionRules.Default().Serialize());input.Text=_c.RulesText();status.Text=_c.ActionRulesStatus;})))};
    }
}
