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
    private bool _visible,_reduced,_quiet,_suspended,_disposed;
    public bool Running=>_timer.IsEnabled;
    public PetAnimator(IPetModel model){_model=model;_timer.Tick+=Tick;}
    public void Configure(string state,bool visible,bool reduced,bool quiet,bool suspended)
    {
        if(_disposed)return;
        _motion.State=state;_visible=visible;_reduced=reduced;_quiet=quiet;_suspended=suspended;
        if(!visible||reduced||suspended){Stop();return;}
        if(quiet&&!_motion.Reacting){Stop();return;}
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
        _model.Apply(_motion.Sample(_time.Elapsed.TotalSeconds));
        if(_quiet&&!_motion.Reacting){Stop();return;}
        _timer.Interval=TimeSpan.FromMilliseconds(_motion.Reacting?1000d/30:1000d/15);
        _timer.Start();
    }
    private void Stop(){_timer.Stop();_time.Reset();_motion.Reset();_model.Apply(PetPose.Neutral(_motion.State));}
    public void Replace(IPetModel model)
    {
        Stop();_model.Dispose();_model=model;Configure(_motion.State,_visible,_reduced,_quiet,_suspended);
    }
    public void Dispose(){if(_disposed)return;Stop();_disposed=true;_timer.Tick-=Tick;_model.Dispose();}
}
