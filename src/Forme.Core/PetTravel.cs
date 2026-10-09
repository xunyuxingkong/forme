namespace Forme.Core;

public readonly record struct GroundPoint(double X,double Z);
public readonly record struct GroundObstacle(double X,double Z,double Width,double Depth);

// Metres and semantic poses only; independent of WPF, geometry and character assets.
public sealed class PetTravel(double minX,double maxX,double minZ,double maxZ,IReadOnlyList<GroundObstacle> obstacles)
{
    private const double Cell=.35,Radius=.38;
    private readonly Queue<GroundPoint> _route=new();
    public GroundPoint Position {get;private set;}
    public bool Moving=>_route.Count>0;
    public bool Running {get;private set;}
    public double Heading {get;private set;}
    private double _phase;
    public void Reset(GroundPoint point){_route.Clear();Position=point;_phase=0;}
    public void Stop()=>_route.Clear();
    public bool Walkable(GroundPoint p)=>double.IsFinite(p.X)&&double.IsFinite(p.Z)&&p.X>=minX+Radius&&p.X<=maxX-Radius&&p.Z>=minZ+Radius&&p.Z<=maxZ-Radius&&!obstacles.Any(o=>Math.Abs(p.X-o.X)<o.Width/2+Radius&&Math.Abs(p.Z-o.Z)<o.Depth/2+Radius);
    public bool MoveTo(GroundPoint target,bool instant=false)
    {
        if(!Walkable(target))return false;
        int nx=(int)Math.Ceiling((maxX-minX)/Cell)+1,nz=(int)Math.Ceiling((maxZ-minZ)/Cell)+1;
        GroundPoint Point(int id)=>new(minX+id%nx*Cell,minZ+id/nx*Cell);
        int Id(GroundPoint p)=>Math.Clamp((int)Math.Round((p.Z-minZ)/Cell),0,nz-1)*nx+Math.Clamp((int)Math.Round((p.X-minX)/Cell),0,nx-1);
        bool Clear(GroundPoint a,GroundPoint b){if(!Walkable(a))return false;int steps=Math.Max(1,(int)Math.Ceiling(Distance(a,b)/.1));for(int i=1;i<=steps;i++)if(!Walkable(new(a.X+(b.X-a.X)*i/steps,a.Z+(b.Z-a.Z)*i/steps)))return false;return true;}
        int start=Id(Position),end=Id(target);var open=new PriorityQueue<int,double>();var cost=new Dictionary<int,double>{{start,0}};var previous=new Dictionary<int,int>();open.Enqueue(start,0);
        if(start==end&&!Clear(Position,target))return false;
        bool found=false;
        while(open.TryDequeue(out int current,out _))
        {
            if(current==end){found=true;break;}
            int x=current%nx,z=current/nx;
            foreach(var (dx,dz) in new[]{(-1,0),(1,0),(0,-1),(0,1),(-1,-1),(-1,1),(1,-1),(1,1)})
            {
                int xx=x+dx,zz=z+dz;if(xx<0||xx>=nx||zz<0||zz>=nz)continue;int next=zz*nx+xx;
                var a=current==start?Position:Point(current);var b=next==end?target:Point(next);if(!Clear(a,b))continue;
                double distance=cost[current]+Distance(a,b);if(cost.TryGetValue(next,out double old)&&distance>=old)continue;
                cost[next]=distance;previous[next]=current;open.Enqueue(next,distance+Distance(b,target));
            }
        }
        if(!found)return false;
        var route=new List<GroundPoint>{target};for(int id=end;id!=start;){id=previous[id];if(id!=start)route.Add(Point(id));}route.Reverse();
        _route.Clear();foreach(var point in route)_route.Enqueue(point);Running=cost[end]>3;_phase=0;
        if(instant){Position=target;Stop();}return true;
    }
    public PetPose Advance(double seconds)
    {
        if(!double.IsFinite(seconds)||seconds<0)throw new ArgumentOutOfRangeException(nameof(seconds));
        double budget=Math.Min(seconds,.1)*(Running?3.4:1.4);_phase+=Math.Min(seconds,.1)*(Running?3.6:2.2)*Math.PI*2;
        while(Moving&&budget>0)
        {
            var next=_route.Peek();double distance=Distance(Position,next);Heading=Math.Atan2(next.X-Position.X,next.Z-Position.Z)*180/Math.PI;
            if(distance<=budget){Position=next;_route.Dequeue();budget-=distance;}else{Position=new(Position.X+(next.X-Position.X)*budget/distance,Position.Z+(next.Z-Position.Z)*budget/distance);budget=0;}
        }
        return Moving?new(1,1,.018*Math.Abs(Math.Sin(_phase)),0,Math.Sin(_phase)*2,false,"idle",Math.Sin(_phase)*(Running?1:.65)):PetPose.Neutral();
    }
    private static double Distance(GroundPoint a,GroundPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
}
