using Forme.Core;
using System.Diagnostics;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace Forme.App;

// A new character implements this adapter; animation never knows its geometry or bones.
internal interface IPetModel : IDisposable
{
    Model3DGroup Root { get; }
    void Apply(PetPose pose);
    bool Contains(Model3D model);
}

internal interface IPetModelFactory { IPetModel Create(); }
internal sealed class PetAnimator : IDisposable
{
    private readonly Stopwatch _time=new();
    private readonly DispatcherTimer _timer=new(DispatcherPriority.Background);
    private readonly PetMotion _motion=new();
    private IPetModel _model;
    private PetTravel? _travel;
    private Action<GroundPoint,double>? _place;
    private double _lastFrame;
    public Func<double,bool,PetPose?>? ExternalMotion {get;set;}
    public bool ExternalActive {get;set;}
    public Func<bool>? ExternalMoving {get;set;}
    public Action<double>? Frame {get;set;}
    public void StopAction(){_motion.Reset();Configure(_motion.State,_visible,_reduced,_quiet,_suspended);}
    public void AttachTravel(PetTravel travel,Action<GroundPoint,double> place){_travel?.Stop();_travel=travel;_place=place;place(travel.Position,travel.Heading);}
    public bool MoveTo(GroundPoint target)
    {
        if(_disposed||!_visible||_suspended||_travel is null||!_travel.MoveTo(target,_reduced))return false;
        _place?.Invoke(_travel.Position,_travel.Heading);
        if(!_reduced){_time.Start();Step();}return true;
    }
    private bool _visible,_reduced,_quiet,_suspended,_disposed;
    public bool Running=>_timer.IsEnabled;
    public PetAnimator(IPetModel model){_model=model;_timer.Tick+=Tick;}
    public void Configure(string state,bool visible,bool reduced,bool quiet,bool suspended)
    {
        if(_disposed)return;
        _motion.State=state;_visible=visible;_reduced=reduced;_quiet=quiet;_suspended=suspended;
        if(!visible||reduced||suspended){Stop();return;}
        if(quiet&&!_motion.Reacting&&_travel?.Moving!=true&&!ExternalActive){Stop();return;}
        if(!_time.IsRunning)_time.Start();
        Step();
    }
    public void Play(PetAction action)
    {
        if(_disposed||!_visible||_suspended)return;
        if(_reduced){_model.Apply(PetPose.Neutral("happy"));return;}
        _time.Start();_motion.Play(action,_time.Elapsed.TotalSeconds);Step();
    }
    public void Drag(bool dragging)
    {
        if(_disposed||!_visible||_reduced||_suspended)return;
        _time.Start();_motion.Drag(dragging,_time.Elapsed.TotalSeconds);Step();
    }
    private void Tick(object? sender,EventArgs e)=>Step();
    private void Step()
    {
        if(_disposed)return;
        double now=_time.Elapsed.TotalSeconds;var pose=_motion.Sample(now);
        if(ExternalMotion?.Invoke(Math.Max(0,now-_lastFrame),_motion.Reacting) is {} external&&!_motion.Reacting)pose=external;
        if(_travel is {} travel)
        {
            var gait=travel.Advance(Math.Max(0,now-_lastFrame));_place?.Invoke(travel.Position,travel.Heading);
            if(travel.Moving)pose=gait;
        }
        Frame?.Invoke(Math.Max(0,now-_lastFrame));_lastFrame=now;_model.Apply(pose);
        if(_quiet&&!_motion.Reacting&&_travel?.Moving!=true&&!ExternalActive){Stop();return;}
        bool movingExternal=ExternalActive&&ExternalMoving?.Invoke()==true;
        _timer.Interval=TimeSpan.FromMilliseconds(_motion.Reacting||_travel?.Moving==true||movingExternal?1000d/30:_motion.State=="sleep"||ExternalActive?200:1000d/15);
        _timer.Start();
    }
    private void Stop(){_timer.Stop();_time.Reset();_lastFrame=0;_travel?.Stop();_motion.Reset();_model.Apply(_motion.State=="sleep"?_motion.Sample(0):PetPose.Neutral(_motion.State));}
    public void Replace(IPetModel model)
    {
        Stop();_model.Dispose();_model=model;Configure(_motion.State,_visible,_reduced,_quiet,_suspended);
    }
    public void Dispose(){if(_disposed)return;Stop();_disposed=true;_timer.Tick-=Tick;_model.Dispose();}
}
