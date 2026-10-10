namespace Forme.Core;

public sealed record BoatLeaf(double X,double Z,double Angle);
public sealed record BoatLayout(string Shape,string Color,int Wind,List<BoatLeaf> Leaves)
{
    public static BoatLayout Default()=>new("swift","sun",0,[new(-1.25,-.65,0),new(-.15,.65,0),new(1.05,-.5,0)]);
    public BoatLayout Copy()=>this with{Leaves=Leaves.ToList()};
    public void Validate()
    {
        if(Shape is not ("swift" or "wide")||Color is not ("sun" or "rose" or "mint")||Wind is <-1 or >1||Leaves is null||Leaves.Count!=3||Leaves.Any(l=>l is null||!double.IsFinite(l.X)||!double.IsFinite(l.Z)||!double.IsFinite(l.Angle)||l.X is <-2 or >2||l.Z is <-1.35 or >1.35||l.Angle is <-70 or >70))
            throw new InvalidDataException("纸船或导流叶片配置无效。");
    }
}

// Small bounded flow simulation. No renderer, timers, network, or random reward logic.
public sealed class PaperBoat
{
    public BoatLayout Layout {get;}
    public GroundPoint Position {get;private set;}=new(-2.55,0);
    public double Heading {get;private set;}=90;
    public double Elapsed {get;private set;}
    public string? Dock {get;private set;}
    public bool PetalFound {get;private set;}
    public bool Complete=>Dock is not null;
    private double _remainder;
    public PaperBoat(BoatLayout layout){layout.Validate();Layout=layout.Copy();}
    public void Advance(double seconds)
    {
        if(!double.IsFinite(seconds)||seconds<0)throw new ArgumentOutOfRangeException(nameof(seconds));
        _remainder+=Math.Min(seconds,.25);
        while(_remainder>=.02&&!Complete){_remainder-=.02;Step();}
    }
    private void Step()
    {
        double dx=Layout.Shape=="swift"?.43:.35,dz=Layout.Wind*(Layout.Shape=="swift"?.105:.125);
        foreach(var leaf in Layout.Leaves)
        {
            double distance=Math.Sqrt(Math.Pow(Position.X-leaf.X,2)+Math.Pow(Position.Z-leaf.Z,2));
            double weight=Math.Max(0,1-distance/1.05);weight*=weight;
            dz+=Math.Sin(leaf.Angle*Math.PI/180)*.55*weight;dx-=.07*weight;
        }
        var next=new GroundPoint(Position.X+dx*.02,Math.Clamp(Position.Z+dz*.02,-1.45,1.45));
        Heading=Math.Atan2(next.X-Position.X,next.Z-Position.Z)*180/Math.PI;Position=next;Elapsed+=.02;
        if(Math.Sqrt(Math.Pow(Position.X+.65,2)+Math.Pow(Position.Z-1.05,2))<.45)PetalFound=true;
        if(Position.X>=2.55){Position=new(2.55,Position.Z);Dock=Position.Z<-.58?"willow":Position.Z>.58?"lily":"bridge";}
    }
    public static string DockName(string? dock)=>dock switch{"willow"=>"柳树码头","lily"=>"睡莲码头","bridge"=>"木桥码头",_=>"尚未抵达"};
    public static IReadOnlyList<GroundPoint> Predict(BoatLayout layout)
    {
        var game=new PaperBoat(layout);var route=new List<GroundPoint>{game.Position};
        for(int i=0;i<500&&!game.Complete;i++){game.Advance(.05);if(i%5==0||game.Complete)route.Add(game.Position);}
        return route;
    }
}

public sealed record BoatSouvenir(string Id,string Title,BoatLayout Layout,string Dock,bool PetalFound,string PetModel,DateTimeOffset Created);
public sealed record BoatAlbum(List<BoatSouvenir> Entries,string? DisplayedId)
{
    public static BoatAlbum Empty()=>new([],null);
    public void Validate()
    {
        if(Entries is null||Entries.Count>60||Entries.Any(e=>e is null||!Guid.TryParseExact(e.Id,"N",out _)||string.IsNullOrWhiteSpace(e.Title)||e.Title.Length>40||e.Layout is null||e.Dock is not ("willow" or "lily" or "bridge")||e.PetModel is not ("sprout" or "cat" or "fox" or "penguin"))||Entries.Select(e=>e.Id).Distinct().Count()!=Entries.Count||DisplayedId is not null&&!Entries.Any(e=>e.Id==DisplayedId))
            throw new InvalidDataException("纪念船记录无效，最多收藏60次旅行。");
        foreach(var entry in Entries)entry.Layout.Validate();
    }
}
