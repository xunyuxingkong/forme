using Forme.Core;
using Microsoft.Win32;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Forme.App;

internal sealed partial class MainWindow
{
    private Action? _projectPageUpdate;
    private Window? _projectPreviewWindow;
    private void DetachProjectPage(){_projectPreviewWindow?.Close();_projectPreviewWindow=null;if(_projectPageUpdate is {} update)_c.ProjectChanged-=update;_projectPageUpdate=null;}
    private UIElement ProjectPage()
    {
        bool refreshing=false;
        var goal=Ui.Input(_c.ProjectGoalDraft,true,2000,"项目目标");goal.TextChanged+=(_,_)=>{if(!refreshing)_c.ProjectGoalDraft=goal.Text;};
        var count=Ui.Select(new[]{"不生成待办建议","1条待办建议","2条待办建议","3条待办建议"},new[]{"不生成待办建议","1条待办建议","2条待办建议","3条待办建议"}[_c.ProjectTaskCount],"待办建议数量");
        count.SelectionChanged+=(_,_)=>{if(!refreshing)_c.ProjectTaskCount=count.SelectedIndex;};
        var history=new ComboBox{MinHeight=36,Margin=new Thickness(0,0,0,12)};
        history.SelectionChanged+=(_,_)=>{if(!refreshing&&history.SelectedItem is ComboBoxItem {Tag:string id})Ui.Guard(()=>_c.SelectProject(id));};
        var sources=Ui.Stack();var taskSuggestions=Ui.Stack();var preview=Ui.Input("",true,65536,"项目来源正文");preview.IsReadOnly=true;preview.MaxHeight=200;preview.Visibility=Visibility.Collapsed;
        var artifact=Ui.Input("",true,10000,"项目草稿");artifact.IsReadOnly=true;artifact.MinHeight=160;
        var state=Ui.Text("",12,Ui.Muted);var budget=Ui.Text("",12,Ui.Muted);
        var fileStatus=Ui.Text("",12,Ui.Muted);
        var sharing=Ui.Check("允许本项目向当前AI服务多次发送所选资料与项目草稿",_c.ProjectSharingAllowed);
        sharing.Checked+=(_,_)=>{if(!refreshing)Ui.Guard(()=>_c.AllowProjectSharing(true));};
        sharing.Unchecked+=(_,_)=>{if(!refreshing)Ui.Guard(()=>_c.AllowProjectSharing(false));};
        var load=ProjectAsyncButton("新目标 / 选择资料",async()=>
        {
            var picker=new OpenFileDialog{Title="选择本项目文本或DOCX · 尚未上传",Multiselect=true,Filter="项目资料|*.txt;*.md;*.csv;*.json;*.docx;*.log;*.cs;*.py;*.js;*.ts;*.html;*.css;*.sql;*.xml;*.yaml;*.yml"};
            if(picker.ShowDialog(this)==true)await _c.LoadProjectSources(picker.FileNames);
        });
        var webAddress=Ui.Input("",false,2048,"粘贴一个公开网页地址（HTTP/HTTPS）");
        var loadWeb=ProjectAsyncButton("读取指定网页",()=>_c.LoadProjectWebSource(webAddress.Text));
        var generate=ProjectAsyncButton("生成或修改草稿",()=>_c.GenerateProjectDraft(),true);
        var revise=Ui.Button("应用新目标",()=>_c.ReviseProjectGoal());
        var stop=Ui.Button("结束并清除资料",()=>_c.EndProject());
        var inspect=Ui.Button("发送预览",()=>
        {
            if(_c.CurrentProject is {} run&&(run.Goal!=_c.ProjectGoalDraft.Trim()||run.TaskCount!=_c.ProjectTaskCount))_c.ReviseProjectGoal();
            var text=Ui.Input(_c.ProjectPreview(),true,100000,"项目发送预览");text.IsReadOnly=true;
            _projectPreviewWindow=new Window{Title="项目发送预览 · 尚未联网",Owner=this,Width=700,Height=600,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=Ui.Card(text)};
            try{_projectPreviewWindow.ShowDialog();}finally{_projectPreviewWindow=null;}
        });
        var export=Ui.Button("导出本项目",()=>
        {
            var run=_c.CurrentProject??throw new InvalidDataException("请选择一个项目。");
            var picker=new SaveFileDialog{Title="导出项目记录与草稿 · 不含原资料或密钥",FileName="Forme-项目记录.json",Filter="JSON|*.json"};
            if(picker.ShowDialog(this)==true)File.WriteAllText(picker.FileName,JsonSerializer.Serialize(new{Format="FormeProject-1",Run=run,Draft=_c.Store.ProjectArtifact(run)},new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false));
        });
        var saveDraft=Ui.Button("保存草稿为文件",()=>
        {
            var run=_c.CurrentProject??throw new InvalidDataException("请选择一个项目。");
            var draft=_c.Store.ProjectArtifact(run)??throw new InvalidDataException("当前项目还没有可保存的草稿。");
            string safeTitle=string.Concat(draft.Title.Select(c=>Path.GetInvalidFileNameChars().Contains(c)||char.IsControl(c)?'_':c)).Trim().Trim('.');
            if(safeTitle.Length>100)safeTitle=safeTitle[..100];if(safeTitle.Length==0)safeTitle="项目草稿";
            var picker=new SaveFileDialog{Title="保存项目草稿 · 只写入你选择的文件",FileName="项目-"+safeTitle+".md",DefaultExt=".md",AddExtension=true,Filter="Markdown 文档|*.md|文本文件|*.txt",OverwritePrompt=true};
            if(picker.ShowDialog(this)!=true)return;
            string target=Path.GetFullPath(picker.FileName),directory=Path.GetDirectoryName(target)??throw new InvalidDataException("保存位置无效。"),temporary=Path.Combine(directory,".forme-draft-"+Guid.NewGuid().ToString("N")+".tmp"),fileContents="# "+draft.Title.Replace('\r',' ').Replace('\n',' ').Trim()+"\n\n"+draft.Content;
            try
            {
                File.WriteAllText(temporary,fileContents,new UTF8Encoding(false));File.Move(temporary,target,true);
                if(!string.Equals(File.ReadAllText(target,new UTF8Encoding(false,true)),fileContents,StringComparison.Ordinal))throw new IOException("文件写入后核对不一致；请检查目标文件。");
                fileStatus.Text="草稿已保存并核对："+Path.GetFileName(target);
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        });
        var delete=Ui.Button("删除项目记录，保留草稿",()=>{if(_c.CurrentProject is {} run){_c.EndProject();_c.Store.DeleteProject(run.Id);_c.Refresh();_projectPageUpdate?.Invoke();}});
        void Update()
        {
            refreshing=true;
            try
            {
                var run=_c.CurrentProject;goal.Text=_c.ProjectGoalDraft;count.SelectedIndex=_c.ProjectTaskCount;
                if(_c.ProjectSources.Count==0)_projectPreviewWindow?.Close();
                sharing.IsChecked=_c.ProjectSharingAllowed;sharing.IsEnabled=_c.ProjectSources.Count>0;
                state.Text=_c.ProjectStatus;budget.Text=run is null?"每个目标最多2次模型调用、首次生成起120秒；沿用AI设置的文字与输出预算。":$"{StateLabel(run.State)} · 目标版本 {run.Revision} · 已用 {run.Requests}/2 次请求 · 输出上限 {_c.Preferences.MaxReplyTokens} tokens"+(run.Endpoint is null?"":"\n最后请求："+run.Endpoint+" · "+run.Model);
                load.IsEnabled=!_c.Busy&&!_c.ProjectOperationBusy;loadWeb.IsEnabled=!_c.Busy&&!_c.ProjectOperationBusy;generate.IsEnabled=!_c.Busy&&!_c.ProjectOperationBusy&&_c.ProjectSharingAllowed&&_c.ProjectSources.Count>0&&run?.Requests<2;
                revise.IsEnabled=run is not null&&!_c.ProjectOperationBusy;stop.IsEnabled=run is not null||_c.ProjectOperationBusy;inspect.IsEnabled=!_c.Busy&&!_c.ProjectOperationBusy&&_c.ProjectSources.Count>0;
                export.IsEnabled=run is not null;saveDraft.IsEnabled=!_c.Busy&&!_c.ProjectOperationBusy&&run?.ArtifactId is not null;delete.IsEnabled=!_c.Busy&&!_c.ProjectOperationBusy&&run is not null;
                sources.Children.Clear();preview.Clear();preview.Visibility=Visibility.Collapsed;
                foreach(var source in _c.ProjectSources)
                {
                    var item=source;
                    sources.Children.Add(Ui.Button(item.Reference.Name+" · "+item.Paragraphs.Count+"段",()=>{preview.Text=(item.Reference.SourceUrl is null?"":"网页来源："+item.Reference.SourceUrl+"\n\n")+string.Join("\n\n",item.Paragraphs.Select((p,i)=>"段落 "+(i+1)+"\n"+p));preview.Visibility=Visibility.Visible;}));
                }
                taskSuggestions.Children.Clear();
                if(run is {State:ProjectRunState.DraftReady})for(int i=0;i<run.TaskSuggestions.Count;i++)
                {
                    int index=i;string artifactId=run.ArtifactId??"";var suggestion=run.TaskSuggestions[index];bool promoted=run.PromotedTaskIndexes.Contains(index);
                    taskSuggestions.Children.Add(Ui.Card(Ui.Stack(Ui.Text(suggestion.Title,14,null,true),Ui.Text(suggestion.Description,12,Ui.Muted),promoted?Ui.Text("已加入本机待办",11,Ui.Sage):Ui.Button("确认加入待办",()=>{if(Ui.Confirm($"将“{suggestion.Title}”加入本机待办？不会设置截止或提醒时间。")){if(_c.Store.PromoteProjectTask(run.Id,run.Revision,artifactId,index,DateTimeOffset.Now)){_c.Refresh();_projectPageUpdate?.Invoke();}else Ui.Error("项目目标或草稿已变化，请刷新后确认。");}}))));
                }
                artifact.Text=run is null?"":_c.Store.ProjectArtifact(run)?.Content??"尚未生成草稿，或该草稿已在记事中删除。";
                history.Items.Clear();foreach(var saved in _c.Store.Projects()){var item=new ComboBoxItem{Content=saved.Goal[..Math.Min(28,saved.Goal.Length)]+" · "+StateLabel(saved.State),Tag=saved.Id};history.Items.Add(item);if(saved.Id==run?.Id)history.SelectedItem=item;}
            }
            finally{refreshing=false;}
        }
        _projectPageUpdate=Update;_c.ProjectChanged+=Update;Update();
        return Ui.Stack(Ui.Text("一起完成一个项目",25,null,true),
            Ui.Text("先做草稿。原资料仅在当前任务内存中保留，不进入普通聊天或长期记忆；重启后需重新选择。目标、来源名称/哈希和状态记入本机账本；摘要和引用随草稿保存到记事。",12,Ui.Muted),
            history,goal,Ui.Text("修改目标后点击“应用新目标”；点击生成时也会应用当前输入。",12,Ui.Muted),count,Ui.Row(load,revise),Ui.Text("网页只读取你粘贴的单个公开地址；不执行脚本或网页操作。最多4份资料，网页最多1 MB、20秒，动态页面可能无法提取。网页请求直连且不使用系统代理；需要代理的网络可能无法读取。",12,Ui.Muted),webAddress,loadWeb,sources,preview,
            Ui.Text("发送到："+_c.Preferences.Endpoint+" · "+_c.Preferences.Model,12,Ui.Muted),sharing,
            Ui.Text("每个目标最多2次请求，失败/取消也计次；重新选择资料会建立新目标，有效恢复则沿用原额度。结束、撤回分享或120秒到期会清除内存资料。网页只向当前AI发送本机提取的正文，不发送网页凭据；来源地址保存时会去除查询参数。DOCX段落编号不是Word页码。",12,Ui.Muted),
            Ui.Row(generate,inspect,stop),state,budget,Ui.Text("草稿与待办建议",17,null,true),taskSuggestions,artifact,
            Ui.Row(Ui.Button("到记事查看或编辑",()=>Navigate("notes")),saveDraft,export,delete),fileStatus,
            Ui.Button("休息时一起玩藏物",()=>Navigate("hide")));
    }
    private static string StateLabel(ProjectRunState state)=>state switch{
        ProjectRunState.Ready=>"等待生成",ProjectRunState.Running=>"正在生成",ProjectRunState.DraftReady=>"草稿已保存",ProjectRunState.WaitingUser=>"等待补充",ProjectRunState.WaitingForSources=>"需要重选资料",ProjectRunState.Failed=>"本次未完成",ProjectRunState.Cancelled=>"已停止",_=>"已结束"};
    private Button ProjectAsyncButton(string title,Func<Task> action,bool primary=false)
    {
        var button=Ui.Button(title,()=>{},primary);
        button.Click+=async(_,_)=>{button.IsEnabled=false;try{await action();}catch(Exception ex) when(OperationErrors.Expected(ex)){Ui.Error(OperationErrors.Message(ex));}finally{_projectPageUpdate?.Invoke();}};
        return button;
    }
}
