using System.Diagnostics;

namespace Forme.Core;

public sealed class FocusClock
{
    private readonly Func<double> _seconds;
    private double _anchor;
    private double _saved;
    public ActivitySnapshot? Activity { get; private set; }
    public bool Running { get; private set; }
    public bool Active => Activity is not null;
    public double Elapsed => Activity is null ? 0 : Math.Min(Activity.TargetSeconds,
        _saved + (Running ? Math.Max(0, _seconds() - _anchor) : 0));
    public double Remaining => Activity is null ? 0 : Math.Max(0, Activity.TargetSeconds - Elapsed);
    public bool Complete => Active && Elapsed >= Activity!.TargetSeconds;

    public FocusClock(Func<double>? seconds = null) => _seconds = seconds ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);

    public void Start(string kind, int minutes, string title)
    {
        if (Active) throw new InvalidOperationException("请先结束当前计时。");
        if (minutes is < 1 or > 180 || kind is not ("focus" or "rest") || title.Length > 120)
            throw new ArgumentException("计时参数无效。");
        Activity = new(Guid.NewGuid().ToString("N"), kind, title, DateTimeOffset.Now, minutes * 60, 0);
        _saved = 0; _anchor = _seconds(); Running = true;
    }
    public void Pause() { if (!Running) return; _saved = Elapsed; Running = false; }
    public void Resume() { if (!Active || Running || Complete) return; _anchor = _seconds(); Running = true; }
    public ActivitySnapshot? Snapshot() => Activity is null ? null : Activity with { ElapsedSeconds = Elapsed };
    public void Restore(ActivitySnapshot snapshot)
    {
        if (snapshot.TargetSeconds is < 60 or > 10800 || !double.IsFinite(snapshot.ElapsedSeconds) ||
            snapshot.ElapsedSeconds < 0 || snapshot.ElapsedSeconds > snapshot.TargetSeconds || snapshot.Kind is not ("focus" or "rest"))
            throw new InvalidDataException("计时恢复记录无效。");
        Activity = snapshot; _saved = snapshot.ElapsedSeconds; Running = false;
    }
    public FocusEntry Finish()
    {
        if (Activity is null) throw new InvalidOperationException("没有计时任务。");
        var entry = new FocusEntry(Activity.Id, Activity.Kind, Activity.Title, Activity.Started,
            Activity.TargetSeconds, Elapsed, Complete ? "completed" : "ended");
        Activity = null; Running = false; _saved = 0;
        return entry;
    }
}
