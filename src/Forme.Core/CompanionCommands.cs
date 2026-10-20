using System.Text.Json;

namespace Forme.Core;

public sealed record CompanionCommand(string Action,string? Value=null,double? X=null,double? Z=null,int? Rotation=null,string? Motion=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] CompanionView? View=null);
public sealed record CompanionReply(string Text,IReadOnlyList<CompanionCommand> Commands,int Rejected);

// This is a narrow presentation protocol, never code, files, URLs or OS commands.
public static class CompanionCommands
{
    public const string Marker="```forme-actions";
    public const string ReportMarker="[Forme 本地动作]";
    public static string VisibleText(string text)
    {
        text=HideLegacyActionJson(text);
        foreach(string marker in new[]{Marker,ProductivityIntentProtocol.Marker,GoalProtocol.Marker})
        {
            int start;
            while((start=text.IndexOf(marker,StringComparison.Ordinal))>=0)
            {
                int end=text.IndexOf("```",start+marker.Length,StringComparison.Ordinal);
                text=end<0?text[..start]:text.Remove(start,end+3-start);
            }
            for(int length=marker.Length-1;length>=3;length--)
            {
                if(length==3&&text.Length!=3)continue;
                if(text.EndsWith(marker[..length],StringComparison.Ordinal)){text=text[..^length];break;}
            }
        }
        return text.TrimEnd();
    }
    private static string HideLegacyActionJson(string text)
    {
        const string fence="```";
        int offset=0;
        while((offset=text.IndexOf(fence,offset,StringComparison.Ordinal))>=0)
        {
            int headerStart=offset+fence.Length;
            int headerEnd=text.IndexOf('\n',headerStart);
            if(headerEnd<0)break;
            string language=text[headerStart..headerEnd].Trim();
            int bodyEnd=text.IndexOf(fence,headerEnd+1,StringComparison.Ordinal);
            if(bodyEnd<0)break;
            int after=bodyEnd+fence.Length;
            if(language.Equals("json",StringComparison.OrdinalIgnoreCase)&&IsLegacyActionPayload(text[(headerEnd+1)..bodyEnd]))
            {
                while(after<text.Length&&text[after] is '\r' or '\n')after++;
                text=text.Remove(offset,after-offset);
            }
            else offset=after;
        }
        return text;
    }
    private static bool IsLegacyActionPayload(string body)
    {
        try
        {
            if(body.Length>8192)return false;
            using var document=JsonDocument.Parse(body,new(){MaxDepth=8});
            var root=document.RootElement;
            if(root.ValueKind==JsonValueKind.Array)return root.EnumerateArray().Any(IsLegacyActionObject);
            return IsLegacyActionObject(root);
        }
        catch(JsonException){return false;}
    }
    private static bool IsLegacyActionObject(JsonElement root)
    {
        if(root.ValueKind!=JsonValueKind.Object)return false;
        bool isAction=false,hasActions=false,isFormeActions=false,hasAction=false;
        foreach(var property in root.EnumerateObject())
        {
            if(property.Name.Equals("type",StringComparison.OrdinalIgnoreCase))
            {
                isAction=property.Value.ValueKind==JsonValueKind.String&&property.Value.GetString()?.Equals("action",StringComparison.OrdinalIgnoreCase)==true;
                isFormeActions=property.Value.ValueKind==JsonValueKind.String&&property.Value.GetString()?.Equals("forme-actions",StringComparison.OrdinalIgnoreCase)==true;
            }
            else if(property.Name.Equals("actions",StringComparison.OrdinalIgnoreCase))hasActions=property.Value.ValueKind==JsonValueKind.Array;
            else if(property.Name.Equals("action",StringComparison.OrdinalIgnoreCase))hasAction=property.Value.ValueKind==JsonValueKind.String;
        }
        return isAction&&hasActions||isFormeActions&&hasAction;
    }
    public static bool TryExtractLegacyActionPlan(string response,out string json)
    {
        json="";bool found=false;int offset=0;
        while((offset=response.IndexOf("```",offset,StringComparison.Ordinal))>=0)
        {
            int headerStart=offset+3,headerEnd=response.IndexOf('\n',headerStart);
            if(headerEnd<0)break;
            string language=response[headerStart..headerEnd].Trim();int bodyEnd=response.IndexOf("```",headerEnd+1,StringComparison.Ordinal);
            if(bodyEnd<0)break;
            string body=response[(headerEnd+1)..bodyEnd];offset=bodyEnd+3;
            if(!language.Equals("json",StringComparison.OrdinalIgnoreCase)||!IsLegacyFormeAction(body))continue;
            if(found)throw new ActionFailureException(ActionResultCode.NotRecognized,"AI返回了多个动作计划，没有执行。");
            json=NormalizeLegacyFormeAction(body);found=true;
        }
        return found;
    }
    private static bool IsLegacyFormeAction(string body)
    {
        try
        {
            if(body.Length>8192)return false;
            using var document=JsonDocument.Parse(body,new(){MaxDepth=4});var root=document.RootElement;
            return root.ValueKind==JsonValueKind.Object&&root.TryGetProperty("type",out var type)&&type.ValueKind==JsonValueKind.String&&type.GetString()?.Equals("forme-actions",StringComparison.OrdinalIgnoreCase)==true;
        }
        catch(JsonException){return false;}
    }
    private static string NormalizeLegacyFormeAction(string body)
    {
        using var document=JsonDocument.Parse(body,new(){MaxDepth=4});var root=document.RootElement;
        var fields=root.EnumerateObject().ToArray();
        if(fields.Any(x=>x.Name is not ("type" or "action" or "target" or "mode" or "count" or "view"))||fields.Select(x=>x.Name).Distinct().Count()!=fields.Length||
            !root.TryGetProperty("action",out var action)||action.ValueKind!=JsonValueKind.String)
            throw new ActionFailureException(ActionResultCode.NotRecognized,"AI动作计划格式不完整，没有执行。");
        var normalized=new Dictionary<string,object?>{{"type",action.GetString()}};
        foreach(string key in new[]{"target","mode","count","view"})if(root.TryGetProperty(key,out var value))normalized[key]=value.Clone();
        return JsonSerializer.Serialize(new[]{normalized});
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
        (command.View is null||Enum.IsDefined(command.View.Value))&&
        (command.View is null||ActionSurface.IsRoomAction(command)||ActionSurface.IsBodyAction(command)||command.Action=="idle")&&
        (command.View!=CompanionView.Desktop||command.Action is "stroll" or "dance" or "pat" or "rub" or "recall" or "idle")&&
        (command.View!=CompanionView.Room||command.Action!="idle")&&
        (command.View!=CompanionView.Room||command.Action!="stroll"||command.Value is not ("lap1" or "lap2"))&&
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
        "model"=>preferences.AllowPetControl&&command.Value is "sprout" or "cat" or "fox" or "penguin" or "space-marine"&&command.X is null&&command.Z is null,
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
