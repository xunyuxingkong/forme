namespace Forme.Core;

// Normalized pose: no mesh coordinates, renderer types, or model names.
public readonly record struct PetPose(double ScaleX,double ScaleY,double Lift,double Yaw,double Lean,bool Blink,string Expression)
{
    public static PetPose Neutral(string expression="idle")=>new(1,1,0,0,0,false,expression);
}

public enum PetAction { Pat, Rub, Land }

public sealed class PetMotion
{
    private PetAction? _action;
    private double _started;
    private bool _dragging;
    public string State { get; set; }="idle";
    public bool Reacting=>_dragging||_action is not null;
    public void Play(PetAction action,double seconds)
    {
        if(!Enum.IsDefined(action))throw new ArgumentOutOfRangeException(nameof(action));
        ValidateTime(seconds);
        if(_dragging)return;
        _action=action;_started=seconds; // Replace repeated input, never accumulate a queue.
    }
    public void Drag(bool dragging,double seconds)
    {
        ValidateTime(seconds);
        if(_dragging==dragging)return;
        _dragging=dragging;_action=null;
        if(!dragging)Play(PetAction.Land,seconds);
    }
    public void Reset(){_action=null;_dragging=false;}
    public PetPose Sample(double seconds)
    {
        ValidateTime(seconds);
        if(_dragging)return new(1.04,.94,.035,0,-7,false,"thinking");
        if(_action is {} action)
        {
            double duration=action==PetAction.Pat?1.05:action==PetAction.Rub?.65:.45;
            double t=Math.Clamp((seconds-_started)/duration,0,1);
            if(t<1)
            {
                double envelope=Math.Sin(Math.PI*t),wave=Math.Sin(t*Math.PI*4)*envelope;
                return action switch
                {
                    PetAction.Pat=>new(1+.045*wave,1-.07*wave,.055*Math.Pow(envelope,2),9*wave,5*wave,false,"happy"),
                    PetAction.Rub=>new(1+.07*envelope,1-.10*envelope,0,5*wave,4*wave,true,"happy"),
                    _=>new(1+.065*envelope,1-.08*envelope,0,0,3*wave,false,State)
                };
            }
            _action=null;
        }
        if(State=="quiet")return PetPose.Neutral(State);
        double cycle=seconds%7;
        double breath=.012*(1-Math.Cos(seconds*Math.PI*2/3.5))/2;
        double sway=State is "idle" or "thinking" && cycle>4&&cycle<6?Math.Sin((cycle-4)*Math.PI)*3:0;
        return new(1-breath*.4,1+breath,0,sway,0,State=="idle"&&cycle>=6.6&&cycle<6.78,State);
    }
    private static void ValidateTime(double seconds)
    {if(!double.IsFinite(seconds)||seconds<0)throw new ArgumentOutOfRangeException(nameof(seconds));}
}
