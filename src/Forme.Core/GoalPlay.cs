namespace Forme.Core;

public enum GoalPlayPhase { Unavailable, Ready, Running, Paused, Completed, Cancelled }
// Local execution identity only; not part of PublicContext or the goal ledger.
public sealed record GoalPlayObservation(string Kind,string Subject,string Round,GoalPlayPhase Phase,int Revision=0,string? Outcome=null);

public static class GoalPlay
{
    public static string? Kind(CompanionCommand command)=>command.Action switch{"hide"=>"hide","boat"=>"boat",_=>null};
    public static bool Supported(CompanionCommand command)=>command.Action=="hide"&&command.Value=="pet-hides"||command.Action=="boat"&&command.Value is "open" or "start";
    public static bool SameRound(GoalPlayObservation before,GoalPlayObservation current)=>before.Kind==current.Kind&&before.Subject==current.Subject&&before.Round.Length>0&&before.Round==current.Round;
    public static string Evidence(GoalPlayObservation observation)=>observation.Kind switch{
        "hide" when observation.Phase==GoalPlayPhase.Completed&&observation.Outcome=="found"=>"play:hide:found",
        "boat" when observation.Phase==GoalPlayPhase.Completed&&observation.Outcome is "willow" or "bridge" or "lily"=>"play:boat:"+observation.Outcome,
        "hide"=>"play:hide:waiting",_=>"play:boat:waiting"};
}
