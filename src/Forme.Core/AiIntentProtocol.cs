using System.Text.Json;

namespace Forme.Core;

public static class AiIntentProtocol
{
    private const string ActionInstruction = "只识别本次用户明确要求的宠物动作，不执行、不聊天、不声称完成。只输出JSON数组，最多3项；不明确或否定、引用、讨论、假设则输出[]。";
    public static string RoutingInstructions(ChatCapabilitySnapshot? snapshot=null)
    {
        string targets=string.Join(",",TargetCatalog.All.Select(x=>x.Id));
        var shared=snapshot is {Shared:true}?snapshot:null;
        string companion="闲聊自然答；本轮明确提出/承接操作才输出单一代码块JSON数组≤3项，不附完成宣称。否定/引用/讨论/假设、记忆和助手承诺不触发。"+
            "伙伴 ```forme-actions：type及目录参数；view=room/desktop按语义。目录："+CapabilityCatalog.ModelSignatures(CapabilityDomain.Companion,shared)+
            "。GoTo/Interact target仅"+targets+"。屋内先ChangeScene(indoor)+Stroll(mode)；count仅明确屏幕环绕(Stroll)或舞步(Dance)；Idle及屏幕环绕固定desktop。坐标/名字仅复制本轮明确值，禁脚本与越权。";
        string state=shared is null?"":"\n本轮已授权的伙伴快照："+shared.PublicContext()+"。activities映射：walk/run→Stroll(mode)，sway→Dance(sway)，visit-X→GoTo(X)，use-X→Interact(X)，chain-X→Life(X)。仅按本轮目标选，可询问/不行动。";
        string goal=snapshot?.GoalPlans==true?"联合```forme-plan混排type/intent，展开≤3步；限走动/舞步/互动/叫回/切场景/停止；末步可StartPlay(hide,pet-hides|boat,open/start)。失败即停。":"";
        return companion+ProductivityIntentProtocol.Instructions()+state+goal;
    }
    public static bool TryExtractActions(string response,out string json)
    {
        json="";int start=response.IndexOf(CompanionCommands.Marker,StringComparison.Ordinal);
        if(start<0)return CompanionCommands.TryExtractLegacyActionPlan(response,out json);
        if(CompanionCommands.TryExtractLegacyActionPlan(response,out _))throw new ActionFailureException(ActionResultCode.NotRecognized,"AI返回了多个动作计划，没有执行。");
        if(response.IndexOf(CompanionCommands.Marker,start+CompanionCommands.Marker.Length,StringComparison.Ordinal)>=0)throw new ActionFailureException(ActionResultCode.NotRecognized,"AI返回了多个动作计划，没有执行。");
        int end=response.IndexOf("```",start+CompanionCommands.Marker.Length,StringComparison.Ordinal);
        if(end<0)throw new ActionFailureException(ActionResultCode.NotRecognized,"AI动作计划格式不完整，没有执行。");
        json=response[(start+CompanionCommands.Marker.Length)..end].Trim();
        return true;
    }
    public static List<AiTurn> BuildContext(Preferences preferences,string input)
    {
        if(string.IsNullOrWhiteSpace(input) || input.EnumerateRunes().Count()>2000) throw new InvalidDataException("请输入内容，最多2,000个字符。");
        string targets = string.Join(",",TargetCatalog.All.Select(x=>x.Id));
        var turns = new List<AiTurn> {
            new("system",$"{ActionInstruction}每项仅有type,target,mode,count。type: {CapabilityCatalog.ModelNames(CapabilityDomain.Companion)}。GoTo/Interact的target只能为{targets}。小屋/屋子/房间里的散步先输出ChangeScene target=indoor，再输出Stroll mode=walk或run且不带count。绕屏幕/屏幕上走一圈或两圈才用Stroll count=1或2。其他散步使用Stroll mode=walk/run且不带count。Dance mode为sway/hop/spin；仅在用户明确提出跳跃或舞步次数时才用count=1..3，否则省略。Idle mode为idle/sleep/walk/run。ChangeScene target为indoor/outdoor。ChangeWeather mode为clear/rain/snow。ChangeModel target为sprout/cat/fox/penguin。ToggleDevice target为light/fireplace且mode为on/off。StartPlay target为boat且mode为open/start/pause/resume/retrieve，或target为hide且mode为pet-hides/pet-seeks。禁止移动坐标、路径、脚本、数据库、奖励、购买、记忆修改。去有水的地方对应GoTo pond。"),
            new("user",input)
        };
        turns[0]=turns[0] with{Content=turns[0].Content+" Also support: Touch(mode:pat/rub); UseToy(target:ball-yellow/feather-mint/yarn-rose/frisbee-sky/paper-plane/bell-brass); Life(target:"+string.Join('/',LifeRules.Ids)+"); LayoutPreview(mode:reading/undo, or mode:furniture,target:explicit furniture ID,x,z,rotation); BoatConfiguration(target:shape,mode:swift/wide; target:color,mode:sun/rose/mint; target:wind,mode:left/forward/right; target:1/2/3 leaf,x,z,rotation); NameBoat(target:user's explicit title). x,z,rotation ONLY copy explicit numeric values in this input, ONLY for furniture or leaf previews; never invent them. No save/purchase/collect. Added fields x,z,rotation allowed only for those previews."};
        turns[0]=turns[0] with{Content=turns[0].Content.Replace("每项仅有type,target,mode,count。","每项仅有type,target,mode,count,view。",StringComparison.Ordinal)+" 可额外添加view字段：room表示小屋/户外伙伴，desktop表示桌面伙伴。依据用户语义选择；桌面走几步使用Stroll mode=walk/run view=desktop；小屋活动使用view=room；count只能用于桌面环绕。Idle固定为桌面待机。"};
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
                if(fields.Any(x=>x.Name is not ("type" or "target" or "mode" or "count" or "x" or "z" or "rotation" or "view")) || fields.Select(x=>x.Name).Distinct().Count()!=fields.Length) throw new JsonException();
                if(!item.TryGetProperty("type",out var type) || type.ValueKind!=JsonValueKind.String || !CapabilityCatalog.AllowsModel(CapabilityDomain.Companion,type.GetString()??"") || !Enum.TryParse<CompanionIntentType>(type.GetString(),out var parsed) || !Enum.IsDefined(parsed)) throw new JsonException();
                string? read(string key) => item.TryGetProperty(key,out var value) && value.ValueKind!=JsonValueKind.Null ? value.GetString() : null;
                int? count = item.TryGetProperty("count",out var number) && number.ValueKind!=JsonValueKind.Null ? number.GetInt32() : null;
                double? coordinate(string key)=>item.TryGetProperty(key,out var value)&&value.ValueKind!=JsonValueKind.Null?value.GetDouble():null;
                double? x=coordinate("x"),z=coordinate("z");
                int? rotation=item.TryGetProperty("rotation",out var angle)&&angle.ValueKind!=JsonValueKind.Null?angle.GetInt32():null;
                CompanionView? view=read("view") switch{null=>null,"room"=>CompanionView.Room,"desktop"=>CompanionView.Desktop,_=>throw new JsonException()};
                var intent = new CompanionIntent(parsed,read("target"),read("mode"),count,1,"ai",X:x,Z:z,Rotation:rotation,View:view);
                if(parsed is CompanionIntentType.Stroll or CompanionIntentType.Dance or CompanionIntentType.Idle or CompanionIntentType.Recall or CompanionIntentType.Stop or CompanionIntentType.Touch && intent.Target is not null)throw new JsonException();
                if(parsed is CompanionIntentType.Recall or CompanionIntentType.Stop or CompanionIntentType.Interact or CompanionIntentType.ChangeScene or CompanionIntentType.ChangeModel or CompanionIntentType.UseToy or CompanionIntentType.Life or CompanionIntentType.NameBoat && intent.Mode is not null)throw new JsonException();
                if(parsed==CompanionIntentType.NameBoat&&(input is null||intent.Target is null||!input.Contains(intent.Target,StringComparison.Ordinal)))throw new JsonException();
                if(x is not null||z is not null||rotation is not null)
                {
                    if(!(parsed==CompanionIntentType.LayoutPreview&&intent.Mode=="furniture"||parsed==CompanionIntentType.BoatConfiguration&&intent.Target is "1" or "2" or "3")||input is null)throw new JsonException();
                    foreach(var numeric in new double?[]{x,z,rotation})
                        if(numeric is {} numberValue&&(!double.IsFinite(numberValue)||!System.Text.RegularExpressions.Regex.IsMatch(input,@"(?<![\d.eE+\-])"+System.Text.RegularExpressions.Regex.Escape(numberValue.ToString(System.Globalization.CultureInfo.InvariantCulture))+@"(?![\d.eE])",System.Text.RegularExpressions.RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50))))throw new JsonException();
                }
                if(count is not null && (!(parsed==CompanionIntentType.Stroll&&count is >=1 and <=2)&&!(parsed==CompanionIntentType.Dance&&count is >=1 and <=3))) throw new JsonException();
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
