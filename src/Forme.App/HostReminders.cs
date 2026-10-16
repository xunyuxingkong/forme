using Forme.Core;
using System.Windows;
using System.Windows.Threading;

namespace Forme.App;

internal sealed partial class DesktopHost
{
    private readonly DispatcherTimer _reminderTimer=new(DispatcherPriority.Background);
    private readonly Dictionary<string,Window> _reminderWindows=new(StringComparer.Ordinal);
    internal int ActiveReminderCount=>_reminderWindows.Count;
    private void InitializeReminders()
    {
        _reminderTimer.Tick+=CheckDueReminders;
        ScheduleReminderCheck();
    }
    private void ScheduleReminderCheck()
    {
        _reminderTimer.Stop();
        if(_exit||_c.AnimationSuspended||_reminderWindows.Count>=5)return;
        var next=_c.Store.NextReminderTime(_reminderWindows.Keys);
        if(next is null)return;
        var remaining=next.Value-DateTimeOffset.Now;
        _reminderTimer.Interval=remaining<=TimeSpan.Zero?TimeSpan.FromMilliseconds(50):remaining>TimeSpan.FromMinutes(1)?TimeSpan.FromMinutes(1):remaining;
        _reminderTimer.Start();
    }
    private void CheckDueReminders(object? sender,EventArgs e)
    {
        _reminderTimer.Stop();
        try
        {
            if(_exit||_c.AnimationSuspended)return;
            foreach(var reminder in _c.Store.DueReminders(DateTimeOffset.Now,100).Where(x=>!_reminderWindows.ContainsKey(x.Id)).Take(5-_reminderWindows.Count))
                ShowReminder(reminder);
        }
        catch(Exception ex) when(OperationErrors.Expected(ex))
        {
            _reminderTimer.Interval=TimeSpan.FromMinutes(1);_reminderTimer.Start();
            Notice("提醒暂未展示："+OperationErrors.Message(ex));return;
        }
        ScheduleReminderCheck();
    }
    private void ShowReminder(UserReminder reminder)
    {
        var window=new Window{Title="Forme 提醒",Width=380,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterScreen,ShowActivated=false,ShowInTaskbar=true};
        bool handled=false;
        var content=Ui.Stack(Ui.Text("到时间了",12,Ui.Muted),Ui.Text(reminder.Title,19,null,true),Ui.Text(reminder.DueAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),12,Ui.Muted));
        void Finish(Action save)
        {
            save();handled=true;window.Close();_c.Refresh();
        }
        content.Children.Add(Ui.Row(
            Ui.Button("稍后 10 分钟",()=>Finish(()=>_c.Store.SnoozeUserReminder(reminder.Id,DateTimeOffset.Now.AddMinutes(10),DateTimeOffset.Now))),
            Ui.Button("完成",()=>Finish(()=>{if(reminder.TaskId is {} taskId)_c.CompleteUserTask(taskId,"done");_c.Store.DismissUserReminder(reminder.Id,DateTimeOffset.Now);}),true),
            Ui.Button("关闭",()=>Finish(()=>_c.Store.DismissUserReminder(reminder.Id,DateTimeOffset.Now)))));
        window.Content=Ui.Card(content);window.Padding=new Thickness(8);
        _reminderWindows.Add(reminder.Id,window);
        window.Closed+=(_,_)=>
        {
            _reminderWindows.Remove(reminder.Id);
            if(_exit)return; // Shutdown leaves pending records intact for the next launch.
            Ui.Guard(()=>{if(!handled)_c.Store.SnoozeUserReminder(reminder.Id,DateTimeOffset.Now.AddMinutes(10),DateTimeOffset.Now);ScheduleReminderCheck();});
        };
        try{window.Show();}catch{_reminderWindows.Remove(reminder.Id);throw;}
    }
    private void DisposeReminders()
    {
        _reminderTimer.Stop();_reminderTimer.Tick-=CheckDueReminders;
        foreach(var window in _reminderWindows.Values.ToArray())window.Close();
        _reminderWindows.Clear();
    }
}
