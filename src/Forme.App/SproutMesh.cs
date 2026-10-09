using System.Windows.Media.Media3D;
using static Forme.App.MeshArt;

namespace Forme.App;

// Character-specific geometry and expression mapping only.
internal static class SproutMesh
{
    public static Model3DGroup Create(string state,bool blink)
    {
        var g=new Model3DGroup();bool closed=blink || state is "focus" or "rest" or "quiet" or "blink";
        // Face points towards +Z. Different states remain the same sculpted character.
        Ellipse(g,"#C9DCBB",0,.74,0,.65,.62,.53);
        Ellipse(g,"#A7C49C",-.42,1.32,-.06,.16,.37,.13,-26);Ellipse(g,"#A7C49C",.42,1.32,-.06,.16,.37,.13,26);
        Ellipse(g,"#BDD2AB",-.35,.19,.23,.23,.13,.25);Ellipse(g,"#BDD2AB",.35,.19,.23,.23,.13,.25);
        Ellipse(g,"#B9CFAB",-.58,.56,.06,.14,.24,.15,-20);Ellipse(g,"#B9CFAB",.58,.56,.06,.14,.24,.15,20);
        Ellipse(g,"#E5B9A1",-.34,.66,.451,.11,.048,.028);Ellipse(g,"#E5B9A1",.34,.66,.451,.11,.048,.028);
        if(closed){Box(g,"#38503C",-.22,.86,.499,.12,.024,.024,-6);Box(g,"#38503C",.22,.86,.499,.12,.024,.024,6);}
        else{Ellipse(g,"#38503C",-.22,.86,.501,.038,.06,.024);Ellipse(g,"#38503C",.22,.86,.501,.038,.06,.024);Ellipse(g,"#FFFFFF",-.212,.881,.522,.011,.013,.006);Ellipse(g,"#FFFFFF",.228,.881,.522,.011,.013,.006);}
        if(state=="thinking")Ellipse(g,"#38503C",0,.65,.517,.032,.043,.02);
        else{Box(g,"#38503C",-.033,.665,.517,.08,.018,.02,-22);Box(g,"#38503C",.033,.665,.517,.08,.018,.02,22);}
        Box(g,"#6D935F",0,1.47,-.015,.034,.3,.034,-8);
        Ellipse(g,"#83A96E",-.12,1.57,-.014,.17,.065,.085,-30);Ellipse(g,"#A0BE7F",.12,1.63,-.014,.17,.065,.085,27);
        if(state=="focus"){Box(g,"#F3E5B8",0,.34,.54,.57,.055,.25);Box(g,"#BBAA81",0,.373,.54,.009,.008,.24);}
        if(state=="happy"){Ellipse(g,"#E9C271",.69,1.3,.1,.055,.14,.055,25);Ellipse(g,"#E9C271",.69,1.3,.1,.14,.045,.045,25);}
        return g;
    }
}
