namespace Forme.Core;

// Normalized pose: no mesh coordinates, renderer types, or model names.
public readonly record struct PetPose(double ScaleX,double ScaleY,double Lift,double Yaw,double Lean,bool Blink,string Expression,double Stride=0,double Reach=0)
{
    public static PetPose Neutral(string expression="idle")=>new(1,1,0,0,0,false,expression);
}

public enum PetAction { Pat, Rub, Land, DanceSway, DanceHop, DanceSpin, Eat, Read, Rest, Look, Launch, Cheer }

public sealed class PetMotion
{
    private PetAction? _action;
    private double _started;
    private bool _dragging;
    public string State { get; set; }="idle";
    public bool Reacting=>_dragging||_action is not null;
    public PetAction? ActiveAction=>_action;
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
            double duration=action==PetAction.Pat?1.05:action==PetAction.Rub?.65:action==PetAction.Land?.45:action==PetAction.Rest?10:action==PetAction.Eat?4:action==PetAction.Launch?2:action==PetAction.Cheer?3:6;
            double t=Math.Clamp((seconds-_started)/duration,0,1);
            if(t<1)
            {
                double envelope=Math.Sin(Math.PI*t),wave=Math.Sin(t*Math.PI*4)*envelope;
                return action switch
                {
                    PetAction.Pat=>new(1+.045*wave,1-.07*wave,.055*Math.Pow(envelope,2),9*wave,5*wave,false,"happy"),
                    PetAction.Rub=>new(1+.07*envelope,1-.10*envelope,0,5*wave,4*wave,true,"happy"),
                    PetAction.DanceSway=>new(1,1,0,8*Math.Sin(t*Math.PI*12)*envelope,7*Math.Sin(t*Math.PI*12)*envelope,false,"happy",Math.Sin(t*Math.PI*12)*envelope),
                    PetAction.DanceHop=>new(1+.04*wave,1-.06*wave,.075*Math.Abs(Math.Sin(t*Math.PI*16))*envelope,6*wave,3*wave,false,"happy",wave),
                    PetAction.DanceSpin=>new(1,1,.025*envelope,360*(3*t-Math.Sin(6*Math.PI*t)/(6*Math.PI)),4*wave,false,"happy",wave),
                    PetAction.Eat=>new(1+.018*Math.Sin(t*Math.PI*20)*envelope,1-.014*Math.Sin(t*Math.PI*20)*envelope,0,0,3*envelope,t%.2<.06,"happy"),
                    PetAction.Read=>new(1,1-.02*envelope,0,3*wave,-2*envelope,true,"focus"),
                    PetAction.Rest=>new(1+.05*envelope,1-.10*envelope+.004*Math.Sin(t*Math.PI*8),0,0,4*envelope,true,"rest"),
                    PetAction.Look=>new(1,1,0,7*Math.Sin(t*Math.PI*4)*envelope,0,false,"thinking"),
                    PetAction.Launch=>new(1,1-.04*envelope,0,0,-5*envelope,false,"happy",0,envelope),
                    PetAction.Cheer=>new(1,1,.045*Math.Abs(Math.Sin(t*Math.PI*8))*envelope,6*wave,5*wave,false,"happy",0,.65*envelope),
                    _=>new(1+.065*envelope,1-.08*envelope,0,0,3*wave,false,State)
                };
            }
            _action=null;
        }
        if(State=="quiet")return PetPose.Neutral(State);
        if(State=="sleep")return new(1.07,.87+.006*Math.Sin(seconds*Math.PI/3),0,0,5,true,"rest");
        double cycle=seconds%7;
        double breath=.012*(1-Math.Cos(seconds*Math.PI*2/3.5))/2;
        double sway=State is "idle" or "thinking" && cycle>4&&cycle<6?Math.Sin((cycle-4)*Math.PI)*3:0;
        return new(1-breath*.4,1+breath,0,sway,0,State=="idle"&&cycle>=6.6&&cycle<6.78,State);
    }
    private static void ValidateTime(double seconds)
    {if(!double.IsFinite(seconds)||seconds<0)throw new ArgumentOutOfRangeException(nameof(seconds));}
}
