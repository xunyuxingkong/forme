namespace Forme.Core;

// Screen work-area coordinates only. No screen capture, OS handles or model assumptions.
public sealed class DesktopWander(Random? random=null)
{
    private readonly Random _random=random??new Random();
    private GroundPoint? _target;
    private Queue<GroundPoint>? _route;
    private double _minX,_maxX,_minY,_maxY,_wait=2,_phase;
    public GroundPoint Position {get;private set;}
    public bool Moving=>_target is not null;
    public void Reset(GroundPoint position,double minX,double maxX,double minY,double maxY)
    {
        _minX=minX;_maxX=Math.Max(minX,maxX);_minY=minY;_maxY=Math.Max(minY,maxY);
        Position=new(Math.Clamp(position.X,_minX,_maxX),Math.Clamp(position.Z,_minY,_maxY));_target=null;_route=null;_wait=2;_phase=0;
    }
    public void Pause(){_target=null;_route=null;_wait=2;}
    public bool BeginStep(double distance)
    {
        if(!double.IsFinite(distance)||distance<=0||distance>1000)throw new ArgumentOutOfRangeException(nameof(distance));
        var targets=new[]{new GroundPoint(Math.Min(_maxX,Position.X+distance),Position.Z),new(Math.Max(_minX,Position.X-distance),Position.Z),new(Position.X,Math.Max(_minY,Position.Z-distance))};
        foreach(double minimum in new[]{distance,Math.Min(distance,20)})foreach(var p in targets)
            if(Math.Abs(p.X-Position.X)+Math.Abs(p.Z-Position.Z)>=minimum){_target=p;return true;}
        return false;
    }
    public bool BeginLaps(int count)
    {
        if(count is <1 or >2)throw new ArgumentOutOfRangeException(nameof(count));
        if(_maxX-_minX<80||_maxY-_minY<80)return false;
        GroundPoint[] corners=[new(_maxX,_minY),new(_maxX,_maxY),new(_minX,_maxY),new(_minX,_minY)];
        static double Distance(GroundPoint a,GroundPoint b)=>Math.Abs(a.X-b.X)+Math.Abs(a.Z-b.Z);
        int first=Enumerable.Range(0,4).MinBy(i=>Distance(Position,corners[i]));
        _route=new Queue<GroundPoint>(Enumerable.Range(0,count*4).Select(step=>corners[(first+step+1)%4]));
        _target=_route.Dequeue();return true;
    }
    public void FinishStep(){if(_route is {Count:>0})Position=_route.Last();else if(_target is {} target)Position=target;Pause();}
    public PetPose? Advance(double seconds,bool running,double scale=1,double speedMultiplier=1)
    {
        if(!double.IsFinite(seconds)||seconds<0||!double.IsFinite(scale)||scale<=0||!double.IsFinite(speedMultiplier)||speedMultiplier<=0||speedMultiplier>10)throw new ArgumentOutOfRangeException(nameof(seconds));
        double dt=Math.Min(seconds,.1);
        if(_target is null)
        {
            _wait-=Math.Min(seconds,1);if(_wait>0)return null;
            _target=new(_minX+_random.NextDouble()*(_maxX-_minX),_minY+_random.NextDouble()*(_maxY-_minY));
        }
        var target=_target.Value;double dx=target.X-Position.X,dy=target.Z-Position.Z,distance=Math.Sqrt(dx*dx+dy*dy),step=dt*(running?170:65)*scale*speedMultiplier;
        if(distance<=Math.Max(step,1))
        {
            Position=target;
            if(_route is {Count:>0})_target=_route.Dequeue();
            else{_target=null;_route=null;_wait=3+_random.NextDouble()*5;}
            return null;
        }
        Position=new(Position.X+dx/distance*step,Position.Z+dy/distance*step);_phase+=dt*(running?3.6:2.2)*Math.PI*2;
        return new(1,1,.018*Math.Abs(Math.Sin(_phase)),dx<0?-18:18,2*Math.Sin(_phase),false,"idle",Math.Sin(_phase)*(running?1:.65));
    }
}
