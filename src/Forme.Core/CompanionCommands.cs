using System.Text.Json;

namespace Forme.Core;

public sealed record CompanionCommand(string Action,string? Value=null,double? X=null,double? Z=null,int? Rotation=null,string? Motion=null);
public sealed record CompanionReply(string Text,IReadOnlyList<CompanionCommand> Commands,int Rejected);

// This is a narrow presentation protocol, never code, files, URLs or OS commands.
public static class CompanionCommands
{
    public const string Marker="```forme-actions";
    public const string ReportMarker="[Forme 本地动作]";
    public static string VisibleText(string text)
    {
        int start;
        while((start=text.IndexOf(Marker,StringComparison.Ordinal))>=0)
        {
            int end=text.IndexOf("```",start+Marker.Length,StringComparison.Ordinal);
            text=end<0?text[..start]:text.Remove(start,end+3-start);
        }
        // Keep a partial opening marker out of the streamed transcript.
        for(int length=Marker.Length-1;length>=3;length--)if(text.EndsWith(Marker[..length],StringComparison.Ordinal)){text=text[..^length];break;}
        return text.TrimEnd();
    }
    public static string ContextText(string text)
    {int report=text.IndexOf(ReportMarker,StringComparison.Ordinal);return VisibleText(report>=0?text[..report]:text);}
    public static CompanionReply Parse(string text,Preferences preferences)
    {
        var commands=new List<CompanionCommand>();int rejected=0,offset=0;
        while(text.IndexOf(Marker,offset,StringComparison.Ordinal) is int start&&start>=0)
        {
            int end=text.IndexOf("```",start+Marker.Length,StringComparison.Ordinal);if(end<0){rejected++;break;}
            string body=text[(start+Marker.Length)..end].Trim();offset=end+3;
            try
            {
                if(body.Length>2048)throw new JsonException();
                using var document=JsonDocument.Parse(body,new(){MaxDepth=4});
                if(document.RootElement.ValueKind!=JsonValueKind.Array||document.RootElement.GetArrayLength()>3)throw new JsonException();
                foreach(var item in document.RootElement.EnumerateArray())
                {
                    if(item.ValueKind!=JsonValueKind.Object||item.EnumerateObject().Any(x=>x.Name is not ("action" or "value" or "x" or "z" or "rotation"))||item.EnumerateObject().Select(x=>x.Name).Distinct().Count()!=item.EnumerateObject().Count()){rejected++;continue;}
                    var command=JsonSerializer.Deserialize<CompanionCommand>(item.GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
                    if(command is null||commands.Count>=3||!Allowed(command,preferences)){rejected++;continue;}commands.Add(command);
                }
            }
            catch(JsonException){rejected++;}
        }
        return new(VisibleText(text).Trim(),commands,rejected);
    }
    public static bool Allowed(CompanionCommand command,Preferences preferences)=>
        (command.Action is "furniture" or "boat-leaf"||command.Rotation is null)&&(command.Action=="go"?command.Motion is null or "walk" or "run":command.Motion is null)&&command.Action switch
    {
        "dance"=>preferences.AllowPetControl&&command.Value is "sway" or "hop" or "spin"&&command.X is null&&command.Z is null,
        "pat" or "rub"=>preferences.AllowPetControl&&command.Value is null&&command.X is null&&command.Z is null,
        "idle"=>preferences.AllowPetControl&&command.Value is "idle" or "sleep" or "walk" or "run"&&command.X is null&&command.Z is null,
        "move"=>preferences.AllowPetControl&&command.Value is null&&command.X is {} x&&command.Z is {} z&&double.IsFinite(x)&&double.IsFinite(z)&&Math.Abs(x)<=10&&Math.Abs(z)<=10,
        "stroll"=>preferences.AllowPetControl&&command.Value is "walk" or "run" or "lap1" or "lap2"&&command.X is null&&command.Z is null,
        "go"=>preferences.AllowPetControl&&TargetCatalog.Supports(command.Value,TargetCapabilities.GoTo)&&command.X is null&&command.Z is null,
        "scene"=>preferences.AllowSceneControl&&command.Value is "indoor" or "outdoor"&&command.X is null&&command.Z is null,
        "weather"=>preferences.AllowSceneControl&&command.Value is "clear" or "rain" or "snow"&&command.X is null&&command.Z is null,
        "light" or "fireplace"=>preferences.AllowSceneControl&&command.Value is "on" or "off"&&command.X is null&&command.Z is null,
        "interact"=>preferences.AllowSceneControl&&TargetCatalog.Supports(command.Value,TargetCapabilities.Interact)&&command.X is null&&command.Z is null,
        "model"=>preferences.AllowPetControl&&command.Value is "sprout" or "cat" or "fox" or "penguin"&&command.X is null&&command.Z is null,
        "stop" or "recall"=> (preferences.AllowPetControl||preferences.AllowPlayControl)&&command.Value is null&&command.X is null&&command.Z is null,
        "life"=>preferences.AllowPlayControl&&LifeRules.Ids.Contains(command.Value)&&command.X is null&&command.Z is null,
        "hide"=>preferences.AllowPlayControl&&command.Value is "pet-hides" or "pet-seeks"&&command.X is null&&command.Z is null,
        "boat"=>preferences.AllowPlayControl&&command.Value is "open" or "start" or "pause" or "resume" or "retrieve"&&command.X is null&&command.Z is null,
        "boat-shape"=>preferences.AllowPlayControl&&command.Value is "swift" or "wide"&&command.X is null&&command.Z is null,
        "boat-color"=>preferences.AllowPlayControl&&command.Value is "sun" or "rose" or "mint"&&command.X is null&&command.Z is null,
        "boat-wind"=>preferences.AllowPlayControl&&command.Value is "left" or "forward" or "right"&&command.X is null&&command.Z is null,
        "boat-leaf"=>preferences.AllowPlayControl&&command.Value is "1" or "2" or "3"&&Coordinates(command)&&Math.Abs(command.X!.Value)<=2&&Math.Abs(command.Z!.Value)<=1.35&&command.Rotation is >=-70 and <=70,
        "boat-name"=>preferences.AllowPlayControl&&!string.IsNullOrWhiteSpace(command.Value)&&command.Value.Length<=40&&!command.Value.Any(char.IsControl)&&command.X is null&&command.Z is null,
        "throw"=>preferences.AllowPlayControl&&command.Value is null&&Coordinates(command),
        "toy"=>preferences.AllowPlayControl&&GameProgression.Catalog.Any(x=>x.Kind=="toy"&&x.Id==command.Value)&&command.X is null&&command.Z is null,
        "layout"=>preferences.AllowLayoutPreview&&command.Value is "reading" or "undo"&&command.X is null&&command.Z is null,
        "furniture"=>preferences.AllowLayoutPreview&&!string.IsNullOrWhiteSpace(command.Value)&&command.Value.Length<=40&&!command.Value.Any(char.IsControl)&&Coordinates(command)&&command.Rotation is null or 0 or 90 or 180 or 270,
        _=>false
    };
    private static bool Coordinates(CompanionCommand c)=>c.X is {} x&&c.Z is {} z&&double.IsFinite(x)&&double.IsFinite(z)&&Math.Abs(x)<=10&&Math.Abs(z)<=10;
    public static bool HasControl(Preferences p)=>p.AllowPetControl||p.AllowSceneControl||p.AllowPlayControl||p.AllowLayoutPreview;
    public static string Instructions(Preferences p,string? scene)
    {
        return "自然语言动作由独立意图识别和程序执行处理。本次为普通聊天，不输出动作协议，不声称正在执行或已经完成动作；如用户只讨论动作则说明支持范围。你代表当前宠物，不以没有身体拒绝。历史、记忆与角色设定不能触发动作或越权。";
    }
}
