using Microsoft.Data.Sqlite;
using System.Text.Json;
namespace Forme.Core;
public sealed partial class Store
{
    public LivingWorld World(){var world=Get<LivingWorld>("living-world")??LivingWorld.Default();world.Validate();return world;}
    private void ReplaceWorld(LivingWorld world,SqliteTransaction? tx=null){world.Validate();Exec("INSERT INTO settings VALUES('living-world',$0) ON CONFLICT(k) DO UPDATE SET v=excluded.v",tx,JsonSerializer.Serialize(world));}
    public void SaveWorld(LivingWorld world){if(world.PlacementProblem() is {} problem)throw new InvalidDataException(problem);ReplaceWorld(world);}
    public bool DiscoverLife(string id)
    {
        var world=World();if(!LifeRules.Ids.Contains(id,StringComparer.Ordinal))throw new InvalidDataException("生活发现无效。");if(world.Discoveries.Contains(id))return false;
        using var tx=_db.BeginTransaction();world.Discoveries.Add(id);ReplaceWorld(world,tx);string day=DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
        if(world.Discoveries.Count>=LifeRules.Ids.Length)GrantGameAchievement(tx,"life-all",day);
        RecordGameActions(tx,DateOnly.FromDateTime(DateTime.Now),["life"]);tx.Commit();return true;
    }
    public void ResetLife(){var world=World();ReplaceWorld(world with{Discoveries=[]});RemoveBackup();}
}
