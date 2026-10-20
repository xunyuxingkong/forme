using Forme.Core;
using Microsoft.Win32;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Forme.App;

internal sealed partial class MainWindow
{
    private bool _goalHistoryExpanded,_cooperativeHistoryExpanded;
    private Action? _cooperativeHistoryRefresh;
    private UIElement GoalHistory(bool cooperative=false)
    {
        var entries=Ui.Stack();string? sessionId=_c.Session?.Id;
        IReadOnlyList<GoalRun> Read()=>cooperative?_c.Store.GoalRuns().Where(x=>x.Origin==GoalOrigin.Cooperative).ToArray():sessionId is null?[]:_c.Store.GoalRuns(sessionId);
        void Update()
        {
          entries.Children.Clear();var runs=Read();foreach(var run in runs)
          {
            var text=Ui.Stack(Ui.Text(run.Created.ToLocalTime().ToString("MM-dd HH:mm")+" · "+Controller.GoalSummary(run),12));
            if(cooperative)text.Children.Add(Ui.Text("AI请求 "+run.Requests+" 次 · "+run.Model,11,Ui.Muted));
            foreach(var step in run.Steps.Where(x=>x.ArtifactId is not null))
            {
                text.Children.Add(Ui.Text(GoalProtocol.Label(step.Capability)+"："+(_c.Store.GoalArtifactExists(step)?"本机产物仍存在":"本机产物尚未生成或已删除"),11,Ui.Muted));
                if(_c.Store.CanReconcileGoalArtifact(run,step))text.Children.Add(Ui.Button("核对并确认此步已保存",()=>
                {
                    if(_c.Store.ReconcileGoalArtifact(run.Id,step.Index,DateTimeOffset.Now))_c.Refresh();
                    else Ui.Error("执行记录或本机产物状态已变化，请刷新后核对。");
                }));
            }
            if(_c.CanReconcileGoalBoat(run))text.Children.Add(Ui.Button("核对并确认纸船本局已到岸",()=>
            {
                try{_c.ReconcileGoalBoat(run.Id);}catch(Exception ex) when(OperationErrors.Expected(ex)){Ui.Error(OperationErrors.Message(ex));}
            }));
            if(!cooperative&&_c.CanPrepareGoalReplan(run))text.Children.Add(Ui.Button("准备AI重规划草稿",()=>_c.PrepareGoalReplan(run.Id)));
            var delete=Ui.Button("删除执行记录，保留产物",()=>{if(Ui.Confirm(cooperative?"删除本条合作执行记录？游戏作品保留，不会重新执行。":"删除本条执行记录？已生成的记事和待办保留；原消息不会重新执行。")){_c.Store.DeleteGoal(run.Id);_c.Refresh();}});
            delete.IsEnabled=!_c.Busy&&run.State is not (GoalRunState.Ready or GoalRunState.Running or GoalRunState.WaitingForPlay or GoalRunState.WaitingForUser);text.Children.Add(delete);entries.Children.Add(Ui.Card(text));
          }
        if(runs.Count==0)entries.Children.Add(Ui.Text(cooperative?"还没有合作玩法执行记录。主动开始AI合作后，阶段、调用次数和真实结果会在这里保留。":"没有目标执行记录。开启跨组件计划后，执行过程和每步结果会在这里保留。",12,Ui.Muted));
        var export=Ui.Button(cooperative?"导出合作玩法执行记录":"导出当前会话执行记录",()=>
        {
            if(!cooperative&&sessionId is null)return;
            var picker=new SaveFileDialog{Title="导出执行元信息 · 不含操作正文与本机产物",FileName="Forme-目标执行记录.json",Filter="JSON|*.json"};
            if(picker.ShowDialog(this)==true)File.WriteAllText(picker.FileName,JsonSerializer.Serialize(new{Format="FormeGoalLedger-1",Runs=Read()},new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false));
        });export.IsEnabled=runs.Count>0;
        entries.Children.Add(cooperative?export:Ui.Row(export,Ui.Button("查看记事",()=>Navigate("notes")),Ui.Button("查看待办和提醒",()=>Navigate("today"))));
        }
        Update();if(cooperative)_cooperativeHistoryRefresh=Update;
        var panel=new Expander{Header=cooperative?"合作玩法执行记录 · 本机":"目标执行记录 · 本机",IsExpanded=cooperative?_cooperativeHistoryExpanded:_goalHistoryExpanded,Content=entries};
        panel.Expanded+=(_,_)=>{if(cooperative){_cooperativeHistoryExpanded=true;Update();}else _goalHistoryExpanded=true;};
        panel.Collapsed+=(_,_)=>{if(cooperative)_cooperativeHistoryExpanded=false;else _goalHistoryExpanded=false;};return panel;
    }
}
