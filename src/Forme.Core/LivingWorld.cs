namespace Forme.Core;

public sealed record FurnitureKind(string Id,string Name,double X,double Z,double Width,double Depth,string Action);
public sealed record FurnitureItem(string Id,string Kind,double X,double Z,int Rotation);
public sealed record LivingWorld(List<FurnitureItem> Items,List<string> Discoveries)
{
    public static readonly FurnitureKind[] Catalog=[new("book","书架",3.64,-3,.8,.6,"book"),new("lamp","暖灯",3.48,-.4,.6,.6,"lamp"),new("feed","零食碗",-2.66,1.6,.65,.55,"feed"),new("sleep","小窝",2.83,1.46,1.5,1.25,"sleep"),new("fish","鱼缸",-.2,-3.12,1,.55,"fish"),new("cushion","坐垫",-1,1.8,.9,.9,"sleep")];
    public static LivingWorld Default()=>new(Catalog.Select(k=>new FurnitureItem(k.Id,k.Id,k.X,k.Id=="book"?.25:k.Z,0)).ToList(),[]);
    public LivingWorld Copy()=>new(Items.ToList(),Discoveries.ToList());
    public static FurnitureKind Kind(string id)=>Catalog.FirstOrDefault(k=>k.Id==id)??throw new InvalidDataException("未知家具。");
    public static readonly GroundObstacle[] Fixed=[new(-2.25,-2.29,2.83,1.28),new(-2.32,-1.19,.8,.8),new(2.36,-2.1,3.25,1.7),new(-1.96,4.32,1.45,1.88),new(2.12,3.73,.75,.75),new(1.85,5.05,1.6,.6),new(-3.95,4.75,1.4,3.2),new(3.95,4.75,1.4,3.2),new(-3.65,-.5,.75,1.2)];
    public static GroundObstacle Footprint(FurnitureItem item){var k=Kind(item.Kind);return new(item.X,item.Z,item.Rotation%180==0?k.Width:k.Depth,item.Rotation%180==0?k.Depth:k.Width);}
    public IReadOnlyList<GroundObstacle> Obstacles=>Fixed.Concat(Items.Where(i=>i.Kind is not ("sleep" or "cushion" or "feed")).Select(Footprint)).ToArray();
    public PetTravel Travel()=>new(-4.3,4.3,-3.6,5.65,Obstacles);
    public void Validate()
    {
        if(Items is null||Discoveries is null||Items.Count>24||Items.Any(i=>i is null||string.IsNullOrWhiteSpace(i.Id)||i.Id.Length>40)||Items.Select(i=>i.Id).Distinct().Count()!=Items.Count||Discoveries.Count>12||Discoveries.Distinct().Count()!=Discoveries.Count||Discoveries.Any(d=>!LifeRules.Ids.Contains(d)))throw new InvalidDataException("家具或生活图鉴数据无效。");
        foreach(var i in Items){var box=Footprint(i);if(!double.IsFinite(i.X)||!double.IsFinite(i.Z)||i.Rotation is not (0 or 90 or 180 or 270)||Math.Abs(i.X)+box.Width/2>4.3||i.Z-box.Depth/2< -3.6||i.Z+box.Depth/2>5.65)throw new InvalidDataException("家具超出小屋范围。");}
    }
    public string? PlacementProblem()
    {
        Validate();
        foreach(var item in Items)
        {
            var a=Footprint(item);
            bool overlap(GroundObstacle b)=>Math.Abs(a.X-b.X)<(a.Width+b.Width)/2-.02&&Math.Abs(a.Z-b.Z)<(a.Depth+b.Depth)/2-.02;
            if(Fixed.Any(overlap)||Items.Any(other=>other.Id!=item.Id&&overlap(Footprint(other))))return Kind(item.Kind).Name+"与其他家具重叠，请移到空地。";
        }
        var travel=Travel();travel.Reset(new(.15,1.25));if(!travel.Walkable(travel.Position))return "请给伙伴的起点留出空间。";
        foreach(var item in Items)if(Approach(item,travel) is null)return Kind(item.Kind).Name+"没有可达的互动位置，请留出通道。";
        return null;
    }
    public static GroundPoint? Approach(FurnitureItem item,PetTravel travel)
    {
        var k=Kind(item.Kind);var b=Footprint(item);
        var targets=item.Kind is "sleep" or "cushion" or "feed"?new[]{new GroundPoint(item.X,item.Z)}:new GroundPoint[0];
        targets=targets.Concat(new[]{new GroundPoint(item.X,item.Z+b.Depth/2+.5),new(item.X-b.Width/2-.5,item.Z),new(item.X+b.Width/2+.5,item.Z),new(item.X,item.Z-b.Depth/2-.5)}).ToArray();
        foreach(var point in targets){if(travel.MoveTo(point)){travel.Stop();return point;}}
        return null;
    }
}
public sealed record LifeEvent(string Id,string Title,string Description,string Action,string Target);
public static class LifeRules
{
    public static readonly string[] Ids=["reading","rain-reading","sun-nap","snack-rest","fish-watch","warm-nap","night-book","garden-break"];
    public static IReadOnlyList<LifeEvent> Evaluate(LivingWorld world,string weather,bool night,bool lamp)
    {
        bool near(string a,string b)=>world.Items.Any(x=>x.Kind==a&&world.Items.Any(y=>y.Kind==b&&x.Id!=y.Id&&Distance(x,y)<2.1));
        bool window(string a)=>world.Items.Any(x=>x.Kind==a&&x.Z< -1.4);
        string target(string kind)=>world.Items.First(x=>x.Kind==kind).Id;
        var events=new List<LifeEvent>();void add(bool ok,string id,string title,string text,string action,string kind){if(ok)events.Add(new(id,title,text,action,target(kind)));}
        add(near("cushion","book"),"reading","小小阅读角","坐垫靠近书架，伙伴可以坐下来读书。","book","cushion");
        add(near("cushion","book")&&near("cushion","lamp")&&weather=="rain"&&lamp,"rain-reading","听雨读书","阅读角开着暖灯，雨声成了故事的背景。","book","cushion");
        add(window("cushion")&&weather=="clear"&&!night,"sun-nap","窗边晒太阳","窗边的坐垫接住了白天的阳光。","sleep","cushion");
        add(near("feed","sleep"),"snack-rest","零食小憩","零食碗就在小窝旁，吃完可以休息。","feed","feed");
        add(near("cushion","fish"),"fish-watch","陪小鱼发呆","坐垫靠近鱼缸，伙伴可以安静观察。","fish","cushion");
        add(near("sleep","lamp")&&lamp,"warm-nap","暖灯晚安","小窝旁的灯亮着，休息角变暖了。","sleep","sleep");
        add(near("book","lamp")&&night&&lamp,"night-book","夜读一页","夜晚亮灯的书架留了一页故事。","book","book");
        add(world.Items.Any(x=>x.Kind=="cushion"&&x.Z>2.3),"garden-break","花园边歇脚","坐垫摆到前庭，可以看看花草。","sleep","cushion");return events;
    }
    private static double Distance(FurnitureItem a,FurnitureItem b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
}
