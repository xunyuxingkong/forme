using Microsoft.Data.Sqlite;
using System.Text.Json;
namespace Forme.Core;
public sealed partial class Store
{
    public LivingWorld World(){var world=Get<LivingWorld>("living-world")??LivingWorld.Default();world.Validate();return world;}
    private void ReplaceWorld(LivingWorld world,SqliteTransaction? tx=null){world.Validate();Exec("INSERT INTO settings VALUES('living-world',$0) ON CONFLICT(k) DO UPDATE SET v=excluded.v",tx,JsonSerializer.Serialize(world));}
    public void SaveWorld(LivingWorld world){if(world.PlacementProblem() is {} problem)throw new InvalidDataException(problem);ReplaceWorld(world);}
    public bool DiscoverLife(string id){var world=World();if(world.Discoveries.Contains(id))return false;world.Discoveries.Add(id);ReplaceWorld(world);return true;}
    public void ResetLife(){var world=World();ReplaceWorld(world with{Discoveries=[]});RemoveBackup();}
}
