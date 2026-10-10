namespace Forme.Core;

public sealed record EnvironmentContext(string Weather,int Hour,string Season,bool IsNight,bool LampOn)
{
    public static EnvironmentContext From(DateTime local,string weather,string theme,bool lamp)=>new(weather,local.Hour,
        local.Month switch {3 or 4 or 5=>"spring",6 or 7 or 8=>"summer",9 or 10 or 11=>"autumn",_=>"winter"},
        theme=="night"||theme=="auto"&&(local.Hour>=19||local.Hour<7),lamp);
}
