using Forme.Core;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Forme.App;

internal sealed partial class MainWindow
{
    private readonly DispatcherTimer _reminderTimer=new(){Interval=TimeSpan.FromSeconds(20)};
    private readonly HashSet<string> _activeReminderIds=new(StringComparer.Ordinal);
    private void StartReminderScheduler(){_reminderTimer.Tick+=CheckDueReminders;CheckDueReminders(this,EventArgs.Empty);}
    private void StopReminderScheduler(){_reminderTimer.Stop();_reminderTimer.Tick-=CheckDueReminders;}
    private void CheckDueReminders(object? sender,EventArgs e)
    {
        _reminderTimer.Stop();
        foreach(var reminder in _c.Store.DueReminders(DateTimeOffset.Now,10))
            if(_activeReminderIds.Add(reminder.Id))ShowReminder(reminder);
        ScheduleReminderCheck();
    }
    private void ScheduleReminderCheck()
    {
        _reminderTimer.Stop();var next=_c.Store.NextReminderTime();if(next is null)return;
        var remaining=next.Value-DateTimeOffset.Now;
        _reminderTimer.Interval=remaining<=TimeSpan.Zero?TimeSpan.FromMinutes(15):remaining>TimeSpan.FromMinutes(15)?TimeSpan.FromMinutes(15):remaining;
        if(remaining<=TimeSpan.Zero&&_activeReminderIds.Count>0)return;
        _reminderTimer.Start();
    }
    private void ShowReminder(UserReminder reminder)
    {
        var window=new Window{Title="Forme 提醒",Width=380,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=this,ShowActivated=false,ShowInTaskbar=true};
        bool handled=false;var content=Ui.Stack(Ui.Text("到时间了",12,Ui.Muted),Ui.Text(reminder.Title,19,null,true),Ui.Text(reminder.DueAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),12,Ui.Muted));
        var buttons=Ui.Row(
            Ui.Button("稍后 10 分钟",()=>{handled=true;_c.Store.SnoozeUserReminder(reminder.Id,DateTimeOffset.Now.AddMinutes(10),DateTimeOffset.Now);window.Close();}),
            Ui.Button("完成",()=>{handled=true;if(reminder.TaskId is {} taskId)_c.CompleteUserTask(taskId,"done");_c.Store.DismissUserReminder(reminder.Id,DateTimeOffset.Now);window.Close();},true),
            Ui.Button("关闭",()=>{handled=true;_c.Store.DismissUserReminder(reminder.Id,DateTimeOffset.Now);window.Close();}));
        content.Children.Add(buttons);window.Content=Ui.Card(content);window.Padding=new Thickness(8);
        window.Closed+=(_,_)=>{if(!handled)_c.Store.SnoozeUserReminder(reminder.Id,DateTimeOffset.Now.AddMinutes(10),DateTimeOffset.Now);_activeReminderIds.Remove(reminder.Id);ScheduleReminderCheck();};window.Show();
    }
    private UIElement NotesPage()
    {
        string? editingId=null;DateTimeOffset created=DateTimeOffset.Now;bool pinned=false,archived=false;
        var title=Ui.Input("",max:120,automationName:"记事标题");var body=Ui.Input("",true,10000,"记事内容");var tags=Ui.Input("",max:200,automationName:"记事标签");var search=Ui.Input("",max:120,automationName:"搜索记事");
        var results=Ui.Stack();var status=Ui.Text("记事只保存在本机；不会自动发送给 AI。",11,Ui.Muted);
        void LoadNotes()
        {
            results.Children.Clear();var notes=_c.Store.UserNotes(includeArchived:true,search:search.Text);
            foreach(var note in notes)
            {
                var row=Ui.Stack(Ui.Text((note.Pinned?"★ ":"")+note.Title+(note.Archived?" · 已归档":""),15,null,true),Ui.Text(note.Content.Length>360?note.Content[..360]+"…":note.Content,13),Ui.Text(string.Join(" · ",note.Tags),11,Ui.Muted),Ui.Row(
                    Ui.Button("编辑",()=>{editingId=note.Id;created=note.Created;pinned=note.Pinned;archived=note.Archived;title.Text=note.Title;body.Text=note.Content;tags.Text=string.Join(", ",note.Tags);}),
                    Ui.Button(note.Pinned?"取消置顶":"置顶",()=>{_c.Store.SaveUserNote(note with{Pinned=!note.Pinned,Updated=DateTimeOffset.Now});LoadNotes();}),
                    Ui.Button(note.Archived?"取消归档":"归档",()=>{_c.Store.SaveUserNote(note with{Archived=!note.Archived,Updated=DateTimeOffset.Now});LoadNotes();}),
                    Ui.Button("删除",()=>{if(Ui.Confirm("删除这条本机记事？")){_c.Store.DeleteUserNote(note.Id);LoadNotes();}})));
                results.Children.Add(Ui.Card(row));
            }
            if(notes.Count==0)results.Children.Add(Ui.Text("没有匹配的记事。",13,Ui.Muted));
        }
        var editor=Ui.Card(Ui.Stack(Ui.Text("快速记事",16,null,true),Ui.Text("标题",12,Ui.Muted),title,Ui.Text("内容",12,Ui.Muted),body,Ui.Text("标签（逗号分隔）",12,Ui.Muted),tags,Ui.Row(
            Ui.Button("保存记事",()=>{var now=DateTimeOffset.Now;var note=new UserNote(editingId??Guid.NewGuid().ToString("N"),title.Text.Trim(),body.Text,tags.Text.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),pinned,archived,"manual",created,now);_c.Store.SaveUserNote(note);editingId=null;created=now;pinned=archived=false;title.Clear();body.Clear();tags.Clear();LoadNotes();status.Text="已保存到本机。只有你主动复制到聊天并发送时，内容才会传给 AI。";},true),Ui.Button("清空编辑框",()=>{editingId=null;created=DateTimeOffset.Now;pinned=archived=false;title.Clear();body.Clear();tags.Clear();}))));
        var page=Ui.Stack(Ui.Text("轻量记事",25,null,true),status,editor,Ui.Card(Ui.Stack(Ui.Text("查找记事",16,null,true),search,Ui.Button("搜索",LoadNotes))),results);LoadNotes();return page;
    }
    private UIElement TodayPage()
    {
        string? editingId=null;UserTask? editingTask=null;
        var title=Ui.Input("",max:200,automationName:"待办标题");var description=Ui.Input("",true,2000,"待办说明");var due=Ui.Input("",max:40,automationName:"截止时间");var remind=Ui.Input("",max:40,automationName:"提醒时间");var priority=Ui.Select(new[]{"普通","重要","紧急"},"普通","待办优先级");
        var reminderTitle=Ui.Input("",max:200,automationName:"提醒内容");var reminderAt=Ui.Input("",max:40,automationName:"提醒时间");var tasks=Ui.Stack();var reminders=Ui.Stack();var pageStatus=Ui.Text("待办和提醒在本机保存；提醒在 Forme 运行期间检查。",11,Ui.Muted);
        bool ParseTime(TextBox input,out DateTimeOffset? value)
        {
            value=null;if(string.IsNullOrWhiteSpace(input.Text))return true;
            if(!DateTimeOffset.TryParse(input.Text,CultureInfo.CurrentCulture,DateTimeStyles.AssumeLocal,out var parsed))return false;
            value=parsed;return true;
        }
        void LoadLists()
        {
            tasks.Children.Clear();var list=_c.Store.UserTasks(limit:100);
            foreach(var task in list)
            {
                var titleText=$"{(task.Status=="done"?"✓ ":task.Status=="doing"?"◉ ":"□ ")}{task.Title} · {task.Priority switch{"urgent"=>"紧急","important"=>"重要",_=>"普通"}}";
                var info=string.Join("  ·  ",new[]{task.DueAt is null?"":task.DueAt.Value.ToLocalTime().ToString("MM-dd HH:mm"),task.Project,task.Description}.Where(x=>!string.IsNullOrWhiteSpace(x)));
                var actions=Ui.Row(Ui.Button("编辑",()=>{editingId=task.Id;editingTask=task;title.Text=task.Title;description.Text=task.Description;due.Text=task.DueAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm")??"";remind.Text=task.RemindAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm")??"";priority.SelectedItem=task.Priority switch{"urgent"=>"紧急","important"=>"重要",_=>"普通"};}));
                if(task.Status is "done" or "cancelled")actions.Children.Add(Ui.Button("恢复待办",()=>{_c.CompleteUserTask(task.Id,"todo");LoadLists();}));
                else
                {
                    if(task.Status=="todo")actions.Children.Add(Ui.Button("开始",()=>{_c.CompleteUserTask(task.Id,"doing");LoadLists();}));
                    actions.Children.Add(Ui.Button("完成",()=>{_c.CompleteUserTask(task.Id,"done");LoadLists();}));
                    actions.Children.Add(Ui.Button("取消",()=>{_c.CompleteUserTask(task.Id,"cancelled");LoadLists();}));
                }
                actions.Children.Add(Ui.Button("删除",()=>{if(Ui.Confirm("删除待办及其关联提醒？")){_c.Store.DeleteUserTask(task.Id);LoadLists();}}));
                tasks.Children.Add(Ui.Card(Ui.Stack(Ui.Text(titleText,15,null,true),Ui.Text(info,12,Ui.Muted),actions)));
            }
            reminders.Children.Clear();var dueList=_c.Store.DueReminders(DateTimeOffset.MaxValue,100);
            foreach(var reminder in dueList.Where(x=>x.Status=="pending"))reminders.Children.Add(Ui.Card(Ui.Stack(Ui.Text(reminder.Title,14,null,true),Ui.Text(reminder.DueAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),12,Ui.Muted),Ui.Row(Ui.Button("稍后 10 分钟",()=>{_c.Store.SnoozeUserReminder(reminder.Id,DateTimeOffset.Now.AddMinutes(10),DateTimeOffset.Now);LoadLists();}),Ui.Button("删除",()=>{_c.Store.DeleteUserReminder(reminder.Id);LoadLists();})))));
        }
        var taskEditor=Ui.Card(Ui.Stack(Ui.Text(editingId is null?"新增今日待办":"编辑今日待办",16,null,true),title,description,priority,Ui.Text("截止时间 / 提醒时间，可留空；例如 2026-10-13 15:00",11,Ui.Muted),due,remind,Ui.Row(
            Ui.Button("保存待办",()=>{if(!ParseTime(due,out var dueAt)||!ParseTime(remind,out var remindAtValue))throw new OperationFailureException("请检查时间格式，例如 2026-10-13 15:00。");var now=DateTimeOffset.Now;var next=new UserTask(editingId??Guid.NewGuid().ToString("N"),title.Text.Trim(),description.Text,editingTask?.Status??"todo",priority.SelectedItem?.ToString() switch{"紧急"=>"urgent","重要"=>"important",_=>"normal"},dueAt,remindAtValue,editingTask?.Created??now,now,editingTask?.CompletedAt,"manual",editingTask?.Project??"",editingTask?.Tags??[],editingTask?.SortOrder??0);_c.Store.SaveUserTask(next);ScheduleReminderCheck();editingId=null;editingTask=null;title.Clear();description.Clear();due.Clear();remind.Clear();LoadLists();pageStatus.Text="已保存到本机。";},true),Ui.Button("清空",()=>{editingId=null;editingTask=null;title.Clear();description.Clear();due.Clear();remind.Clear();}))));
        var reminderEditor=Ui.Card(Ui.Stack(Ui.Text("单独提醒",16,null,true),reminderTitle,reminderAt,Ui.Button("保存提醒",()=>{if(!ParseTime(reminderAt,out var at)||at is null)throw new OperationFailureException("请填写有效提醒时间，例如 2026-10-13 15:00。");var now=DateTimeOffset.Now;_c.Store.SaveUserReminder(new(Guid.NewGuid().ToString("N"),reminderTitle.Text.Trim(),at.Value,null,"pending","",now,now,null));ScheduleReminderCheck();reminderTitle.Clear();reminderAt.Clear();LoadLists();pageStatus.Text="提醒已保存在本机。Forme 运行时会按时提示。";},true)));
        LoadLists();return Ui.Stack(Ui.Text("今日工作台",25,null,true),pageStatus,taskEditor,Ui.Card(Ui.Stack(Ui.Text("待办",17,null,true),tasks)),reminderEditor,Ui.Card(Ui.Stack(Ui.Text("待触发提醒",17,null,true),reminders)));
    }
}
