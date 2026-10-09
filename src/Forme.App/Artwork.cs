using Forme.Core;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Forme.App;

internal static class Art
{
    private static readonly Dictionary<string,Brush> Brushes=new();
    private static readonly Dictionary<string,Geometry> Geometries=new();
    public static Brush B(string color){if(!Brushes.TryGetValue(color,out var b)){b=Ui.Brush(color);Brushes[color]=b;}return b;}
    public static void Rect(DrawingContext d,string color,double x,double y,double w,double h,double r=0)=>d.DrawRoundedRectangle(B(color),null,new Rect(x,y,w,h),r,r);
    public static void Oval(DrawingContext d,string color,double x,double y,double rx,double ry)=>d.DrawEllipse(B(color),null,new Point(x,y),rx,ry);
    public static void Line(DrawingContext d,string color,double x,double y,double a,double b,double width=2)=>d.DrawLine(new Pen(B(color),width),new(x,y),new(a,b));
    public static void Path(DrawingContext d,string color,string path)
    {if(!Geometries.TryGetValue(path,out var g)){g=Geometry.Parse(path);g.Freeze();Geometries[path]=g;}d.DrawGeometry(B(color),null,g);}
    public static void Label(DrawingContext d,string text,double x,double y,double size=12,string color="#78847B")
    {d.DrawText(new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),size,B(color),1),new(x,y));}
    public static void Pet(DrawingContext d,double x,double y,double scale,string state,bool blink=false)
    {
        d.PushTransform(new TranslateTransform(x,y));d.PushTransform(new ScaleTransform(scale,scale));
        Oval(d,"#D9DDD2",80,149,57,9);
        Path(d,"#A9C4A4","M 38,67 C 19,40 22,21 41,29 C 53,35 58,50 57,63 Z");
        Path(d,"#A9C4A4","M 102,62 C 109,28 132,21 136,42 C 140,53 129,68 121,72 Z");
        Path(d,"#C8DABC","M 22,102 C 20,67 38,49 76,50 C 118,46 143,65 143,105 C 146,134 121,150 80,148 C 43,149 20,132 22,102 Z");
        Path(d,"#D8E5CC","M 33,99 C 31,69 49,58 77,57 C 100,54 117,64 122,76 C 99,70 46,83 33,99 Z");
        Oval(d,"#BDD1B0",43,139,16,10);Oval(d,"#BDD1B0",119,139,16,10);
        Oval(d,"#ECC4AB",47,109,10,5);Oval(d,"#ECC4AB",115,109,10,5);
        bool sleepy=state is "focus" or "rest" or "quiet";
        if(blink || sleepy){Line(d,"#405945",53,96,65,97,3);Line(d,"#405945",98,97,110,96,3);}
        else{Oval(d,"#405945",59,94,3.7,5.5);Oval(d,"#405945",103,94,3.7,5.5);Oval(d,"#FFFFFF",60,92,1.2,1.2);Oval(d,"#FFFFFF",104,92,1.2,1.2);}
        if(state=="thinking")Oval(d,"#405945",82,113,3,4);
        else {var smile=Geometry.Parse("M 74,110 Q 81,117 88,110");d.DrawGeometry(null,new Pen(B("#405945"),2.5),smile);}
        Line(d,"#5F8B58",80,49,82,31,3);
        Path(d,"#6E9A62","M 82,34 C 66,33 66,20 71,18 C 82,18 87,29 82,34 Z");
        Path(d,"#8CAF75","M 83,30 C 85,16 99,15 103,18 C 101,28 91,32 83,30 Z");
        if(state=="focus"){Rect(d,"#F5E7BA",50,128,62,13,3);Line(d,"#BFAC7B",80,129,80,140,1);}
        if(state=="happy"){Label(d,"✦",132,41,22,"#D6AA59");Label(d,"✧",9,61,16,"#D6AA59");}
        d.Pop();d.Pop();
    }
}

