namespace Forme.Core;
public sealed record AutonomyLedger
{
    public int Version {get;init;}=1;
    public DateTimeOffset LastSeen {get;set;}
    public DateTimeOffset? LastGoal {get;set;}
    public List<DateTimeOffset> Calls {get;set;}=[];
    public int Failures {get;set;}
    public DateTimeOffset? PauseUntil {get;set;}
    public DateTimeOffset? LastInvitation {get;set;}
    public string RejectedActivity {get;set;}="";
    public DateTimeOffset? RejectUntil {get;set;}
    public void Validate()
    {
        if(Version!=1||Calls is null||Calls.Count>100||Failures is <0 or >2||RejectedActivity is null||RejectedActivity.Length>64)throw new InvalidDataException("自主调用额度数据无效。");
    }
    public bool Reserve(DateTimeOffset now,int dailyLimit,bool newGoal=true)
    {
        Validate();if(dailyLimit is <1 or >100)throw new InvalidDataException("每日额度无效。");
        now=now>LastSeen?now:LastSeen;LastSeen=now;Calls.RemoveAll(x=>x>now||now-x>=TimeSpan.FromHours(24));
        if(PauseUntil>now||Calls.Count>=dailyLimit||Calls.Count(x=>x<=now&&now-x<TimeSpan.FromHours(1))>=6||
            newGoal&&LastGoal is {} previous&&now-previous<TimeSpan.FromMinutes(10))return false;
        Calls.Add(now);if(newGoal)LastGoal=now;return true;
    }
    public void Result(DateTimeOffset now,bool success,bool cancelled=false)
    {
        now=now>LastSeen?now:LastSeen;LastSeen=now;if(cancelled)return;
        if(success){Failures=0;return;}Failures=Math.Min(2,Failures+1);if(Failures==2)PauseUntil=now.AddMinutes(30);
    }
    public bool Invite(DateTimeOffset now,string activity)
    {
        now=now>LastSeen?now:LastSeen;
        if(LastInvitation is {} last&&now-last<TimeSpan.FromMinutes(30)||RejectedActivity==activity&&RejectUntil>now)return false;
        LastInvitation=now;return true;
    }
    public void Reject(DateTimeOffset now,string activity,bool today)
    {
        now=now>LastSeen?now:LastSeen;RejectedActivity=activity;
        var local=now.ToLocalTime();RejectUntil=today?new DateTimeOffset(local.Date.AddDays(1),local.Offset):now.AddHours(1);
    }
}
public sealed record AutonomyValidation(string Endpoint,string Model,DateTimeOffset At);
