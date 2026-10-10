using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Forme.Core;

public sealed partial class Store
{
    public BoatAlbum Boats(){var album=Get<BoatAlbum>("boat-album")??BoatAlbum.Empty();album.Validate();return album;}
    private void ReplaceBoats(BoatAlbum album,SqliteTransaction? tx=null)
    {album.Validate();Exec("INSERT INTO settings VALUES('boat-album',$0) ON CONFLICT(k) DO UPDATE SET v=excluded.v",tx,JsonSerializer.Serialize(album));}
    public void KeepBoat(BoatSouvenir souvenir)
    {
        var album=Boats();var entries=album.Entries.Where(e=>e.Id!=souvenir.Id).ToList();entries.Insert(0,souvenir);
        ReplaceBoats(new(entries,souvenir.Id));
    }
    public void DisplayBoat(string? id){var album=Boats();ReplaceBoats(album with{DisplayedId=id});}
    public void DeleteBoat(string id)
    {
        var album=Boats();ReplaceBoats(new(album.Entries.Where(e=>e.Id!=id).ToList(),album.DisplayedId==id?null:album.DisplayedId));RemoveBackup();
    }
}
