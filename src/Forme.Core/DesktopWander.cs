namespace Forme.Core;

// Screen work-area coordinates only. No screen capture, OS handles or model assumptions.
public sealed class DesktopWander(Random? random=null)
{
    private readonly Random _random=random??new Random();
    private GroundPoint? _target;
    private double _minX,_maxX,_minY,_maxY,_wait=2,_phase;
    public GroundPoint Position {get;private set;}
    public bool Moving=>_target is not null;
    public void Reset(GroundPoint position,double minX,double maxX,double minY,double maxY)
    {
        _minX=minX;_maxX=Math.Max(minX,maxX);_minY=minY;_maxY=Math.Max(minY,maxY);
        Position=new(Math.Clamp(position.X,_minX,_maxX),Math.Clamp(position.Z,_minY,_maxY));_target=null;_wait=2;_phase=0;
    }
    public void Pause(){_target=null;_wait=2;}
    public PetPose? Advance(double seconds,bool running,double scale=1)
    {
        if(!double.IsFinite(seconds)||seconds<0||!double.IsFinite(scale)||scale<=0)throw new ArgumentOutOfRangeException(nameof(seconds));
        double dt=Math.Min(seconds,.1);
        if(_target is null)
        {
            _wait-=dt;if(_wait>0)return null;
            _target=new(_minX+_random.NextDouble()*(_maxX-_minX),_minY+_random.NextDouble()*(_maxY-_minY));
        }
        var target=_target.Value;double dx=target.X-Position.X,dy=target.Z-Position.Z,distance=Math.Sqrt(dx*dx+dy*dy),step=dt*(running?170:65)*scale;
        if(distance<=Math.Max(step,1)){Position=target;_target=null;_wait=3+_random.NextDouble()*5;return null;}
        Position=new(Position.X+dx/distance*step,Position.Z+dy/distance*step);_phase+=dt*(running?3.6:2.2)*Math.PI*2;
        return new(1,1,.018*Math.Abs(Math.Sin(_phase)),dx<0?-18:18,2*Math.Sin(_phase),false,"idle",Math.Sin(_phase)*(running?1:.65));
    }
}
