using System.Text.Json;

namespace Forme.Core;

public static class AiIntentProtocol
{
    public static List<AiTurn> BuildContext(Preferences preferences,string input)
    {
        if(string.IsNullOrWhiteSpace(input) || input.EnumerateRunes().Count()>2000) throw new InvalidDataException("请输入内容，最多2,000个字符。");
        string targets = string.Join(",",TargetCatalog.All.Select(x=>x.Id));
        var turns = new List<AiTurn> {
            new("system",$"只识别本次用户明确要求的宠物动作，不执行、不聊天、不声称完成。只输出JSON数组，最多3项；不明确或否定、引用、讨论、假设则输出[]。每项仅有type,target,mode,count。type: GoTo,Interact,Stroll,Dance,Idle,Recall,Stop,ChangeScene,ChangeWeather,ChangeModel,ToggleDevice,StartPlay。GoTo/Interact的target只能为{targets}。Stroll mode为walk/run，环绕屏幕count仅1/2。Dance mode为sway/hop/spin。Idle mode为idle/sleep/walk/run。ChangeScene target为indoor/outdoor。ChangeWeather mode为clear/rain/snow。ChangeModel target为sprout/cat/fox/penguin。ToggleDevice target为light/fireplace且mode为on/off。StartPlay target为boat且mode为open/start/pause/resume/retrieve，或target为hide且mode为pet-hides/pet-seeks。禁止移动坐标、路径、脚本、数据库、奖励、购买、记忆修改。去有水的地方对应GoTo pond。"),
            new("user",input)
        };
        turns[0]=turns[0] with{Content=turns[0].Content+" Also support: Touch(mode:pat/rub); UseToy(target:ball-yellow/feather-mint/yarn-rose/frisbee-sky/paper-plane/bell-brass); Life(target:"+string.Join('/',LifeRules.Ids)+"); LayoutPreview(mode:reading/undo, or mode:furniture,target:explicit furniture ID,x,z,rotation); BoatConfiguration(target:shape,mode:swift/wide; target:color,mode:sun/rose/mint; target:wind,mode:left/forward/right; target:1/2/3 leaf,x,z,rotation); NameBoat(target:user's explicit title). x,z,rotation ONLY copy explicit numeric values in this input, ONLY for furniture or leaf previews; never invent them. No save/purchase/collect. Added fields x,z,rotation allowed only for those previews."};
        if(turns.Sum(AiClient.Estimate)>preferences.ContextBudget) throw new InvalidDataException("动作识别内容超过上下文预算，未发送。");
        return turns;
    }
    public static IReadOnlyList<CompanionIntent> Parse(string text,string? input=null)
    {
        try
        {
            if(text.Length>4096) throw new JsonException();
            using var doc = JsonDocument.Parse(text,new(){MaxDepth=4});
            if(doc.RootElement.ValueKind!=JsonValueKind.Array || doc.RootElement.GetArrayLength()>3) throw new JsonException();
            var result = new List<CompanionIntent>();
            foreach(var item in doc.RootElement.EnumerateArray())
            {
                if(item.ValueKind!=JsonValueKind.Object) throw new JsonException();
                var fields = item.EnumerateObject().ToArray();
                if(fields.Any(x=>x.Name is not ("type" or "target" or "mode" or "count" or "x" or "z" or "rotation")) || fields.Select(x=>x.Name).Distinct().Count()!=fields.Length) throw new JsonException();
                if(!item.TryGetProperty("type",out var type) || type.ValueKind!=JsonValueKind.String || !Enum.TryParse<CompanionIntentType>(type.GetString(),out var parsed) || !Enum.IsDefined(parsed) || parsed==CompanionIntentType.Shortcut) throw new JsonException();
                string? read(string key) => item.TryGetProperty(key,out var value) && value.ValueKind!=JsonValueKind.Null ? value.GetString() : null;
                int? count = item.TryGetProperty("count",out var number) && number.ValueKind!=JsonValueKind.Null ? number.GetInt32() : null;
                double? coordinate(string key)=>item.TryGetProperty(key,out var value)&&value.ValueKind!=JsonValueKind.Null?value.GetDouble():null;
                double? x=coordinate("x"),z=coordinate("z");
                int? rotation=item.TryGetProperty("rotation",out var angle)&&angle.ValueKind!=JsonValueKind.Null?angle.GetInt32():null;
                var intent = new CompanionIntent(parsed,read("target"),read("mode"),count,1,"ai",X:x,Z:z,Rotation:rotation);
                if(parsed is CompanionIntentType.Stroll or CompanionIntentType.Dance or CompanionIntentType.Idle or CompanionIntentType.Recall or CompanionIntentType.Stop or CompanionIntentType.Touch && intent.Target is not null)throw new JsonException();
                if(parsed is CompanionIntentType.Recall or CompanionIntentType.Stop or CompanionIntentType.Interact or CompanionIntentType.ChangeScene or CompanionIntentType.ChangeModel or CompanionIntentType.UseToy or CompanionIntentType.Life or CompanionIntentType.NameBoat && intent.Mode is not null)throw new JsonException();
                if(parsed==CompanionIntentType.NameBoat&&(input is null||intent.Target is null||!input.Contains(intent.Target,StringComparison.Ordinal)))throw new JsonException();
                if(x is not null||z is not null||rotation is not null)
                {
                    if(!(parsed==CompanionIntentType.LayoutPreview&&intent.Mode=="furniture"||parsed==CompanionIntentType.BoatConfiguration&&intent.Target is "1" or "2" or "3")||input is null)throw new JsonException();
                    foreach(var numeric in new double?[]{x,z,rotation})
                        if(numeric is {} numberValue&&(!double.IsFinite(numberValue)||!System.Text.RegularExpressions.Regex.IsMatch(input,@"(?<![\d.eE+\-])"+System.Text.RegularExpressions.Regex.Escape(numberValue.ToString(System.Globalization.CultureInfo.InvariantCulture))+@"(?![\d.eE])",System.Text.RegularExpressions.RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50))))throw new JsonException();
                }
                if(count is not null && (parsed!=CompanionIntentType.Stroll || count is <1 or >2)) throw new JsonException();
                if(parsed==CompanionIntentType.GoTo && intent.Mode is not (null or "walk" or "run")) throw new JsonException();
                var plan = ActionPlanner.Build([intent],SceneKind.Indoor);
                var grants = new Preferences {AllowPetControl=true,AllowSceneControl=true,AllowPlayControl=true,AllowLayoutPreview=true};
                if(ActionPlanner.Preflight(plan,grants) is not null) throw new JsonException();
                result.Add(intent);
            }
            return result;
        }
        catch(Exception ex) when(ex is JsonException or InvalidOperationException or FormatException or OverflowException or ActionFailureException)
        { throw new ActionFailureException(ActionResultCode.NotRecognized,"未获得完整合法的动作意图，没有执行；请换一种明确说法。"); }
    }
}
