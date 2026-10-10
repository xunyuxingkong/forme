using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Forme.Core;

public sealed record ActionRule(string[] Phrases,CompanionCommand[] Commands);
public sealed record ActionRules(int Version,ActionRule[] Rules)
{
    // Version remains readable by old files; schema and built-in content evolve independently.
    public int SchemaVersion {get;init;}=1;
    public int BuiltinRevision {get;init;}
    public const int CurrentBuiltinRevision=4;
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=8};
    public static ActionRules Default()=>new(1,[
        new(["走两步","走几步","散散步"],[new("stroll","walk")]),
        new(["跑几步","跑两步"],[new("stroll","run")]),
        new(["整个屏幕跑一圈","整个屏幕跑1圈","绕屏幕跑一圈","绕屏幕跑1圈","沿屏幕跑一圈","围着屏幕跑一圈","在屏幕上跑一圈","绕着屏幕跑一圈"],[new("stroll","lap1")]),
        new(["整个屏幕跑两圈","整个屏幕跑2圈","绕屏幕跑两圈","绕屏幕跑2圈","沿屏幕跑两圈","围着屏幕跑两圈","在屏幕上跑两圈","绕着屏幕跑两圈"],[new("stroll","lap2")]),
        new(["过来","回来"],[new("recall")]),
        new(["别动","停下","停止动作"],[new("stop")]),
        new(["跳个舞","跳舞","摇摆舞"],[new("dance","sway")]),
        new(["转圈舞","转一圈","转个圈","转圈"],[new("dance","spin")]),new(["蹦跳舞"],[new("dance","hop")]),
        new(["睡觉","睡一会儿"],[new("idle","sleep")]),new(["醒醒","原地待机"],[new("idle","idle")]),
        new(["随机走动","自由走动"],[new("idle","walk")]),new(["随机奔跑","自由奔跑"],[new("idle","run")]),
        new(["去书架旁","去书架那边"],[new("go","book")]),new(["去窗边"],[new("go","window")]),
        new(["去小窝旁"],[new("go","sleep")]),new(["去鱼缸旁"],[new("go","fish")]),
        new(["换成小猫","切换为小猫"],[new("model","cat")]),new(["换成芽芽"],[new("model","sprout")]),
        new(["玩藏物","陪我玩藏物","你藏我找"],[new("hide","pet-hides")]),new(["我藏你找"],[new("hide","pet-seeks")]),
        new(["玩纸船","陪我玩纸船"],[new("boat","open")]),new(["放船","开始纸船旅行"],[new("boat","start")]),
        new(["暂停纸船"],[new("boat","pause")]),new(["继续纸船"],[new("boat","resume")]),
        new(["布置阅读角"],[new("layout","reading")]),
        new(["读书","读一会儿书"],[new("interact","book")]),new(["吃零食"],[new("interact","feed")]),
        new(["看小鱼"],[new("go","fish")]),new(["浇水"],[new("interact","water")]),
        new(["玩小球","抛个球"],[new("throw",X:0,Z:2.5)]),new(["体验阅读角"],[new("life","reading")]),
        new(["去户外"],[new("scene","outdoor")]),new(["回小屋"],[new("scene","indoor")]),
        new(["打开灯"],[new("light","on")]),new(["关灯"],[new("light","off")])
    ]) {BuiltinRevision=CurrentBuiltinRevision};
    public string Serialize()=>JsonSerializer.Serialize(this,Json);
    private static readonly IReadOnlySet<string> LegacyBuiltinFingerprints=new HashSet<string>(StringComparer.Ordinal)
    {
        "FA9D342718F31790F6DF32434FE69C63D639917A2D97CEEB9FF534527E77CFA7", // revision 4, frozen for future migrations
        "0D4C49FCB049754E11AA8D3F6E199C05F0AEC1066ECDABAB3149F468390BD939", // before screen laps
        "BF8FAA30B641DF90E0C5FEF406CB6E3DB6775910961D418B2C8BAD5A5303C4B4", // before spin aliases and screen laps
        "B11295EB7C104A021E76D1F155D1C15506987E440C7D2A611AD8B95CA474F300"  // screen laps, before spin aliases
    };
    private string ContentFingerprint()=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(Rules.Select(r=>new{r.Phrases,Commands=r.Commands.Select(c=>new{c.Action,c.Value,c.X,c.Z,c.Rotation,c.Motion})}),new JsonSerializerOptions{Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}))));
    public ActionRules UpgradeDefaults()
    {
        var current=Default();string fingerprint=ContentFingerprint();
        // Freeze old fingerprints once. Later revisions do not reconstruct historical snapshots.
        // Metadata alone never authorizes overwriting a user's edited file.
        if(fingerprint==current.ContentFingerprint())return BuiltinRevision==CurrentBuiltinRevision?this:current;
        return LegacyBuiltinFingerprints.Contains(fingerprint)?current:this;
    }
    public static ActionRules Parse(string text)
    {
        if(System.Text.Encoding.UTF8.GetByteCount(text)>32768)throw new InvalidDataException("动作规则文件最多32KB。");
        ActionRules rules;
        try{rules=JsonSerializer.Deserialize<ActionRules>(text,Json)??throw new JsonException();}
        catch(JsonException){throw new InvalidDataException("动作规则JSON格式无效或包含未知字段。");}
        if(rules.Version!=1||rules.SchemaVersion!=1||rules.BuiltinRevision<0||rules.BuiltinRevision>CurrentBuiltinRevision||rules.Rules is null||rules.Rules.Length>40)throw new InvalidDataException("规则结构版本需为1，内置修订版本无效或超过40条。");
        var seen=new HashSet<string>();var grants=new Preferences{AllowPetControl=true,AllowSceneControl=true,AllowPlayControl=true,AllowLayoutPreview=true};
        foreach(var rule in rules.Rules)
        {
            if(rule is null||rule.Phrases is null||rule.Phrases.Length is <1 or >10||rule.Commands is null||rule.Commands.Length is <1 or >3||rule.Commands.Any(c=>c is null||!CompanionCommands.Allowed(c,grants)))throw new InvalidDataException("规则必须包含1–10句表达和1–3个白名单动作。");
            foreach(var phrase in rule.Phrases)
                if(string.IsNullOrWhiteSpace(phrase)||phrase.Length>40||string.IsNullOrWhiteSpace(Normalize(phrase))||!seen.Add(Normalize(phrase)))throw new InvalidDataException("规则表达为空、重复或超过40字。");
        }
        return rules;
    }
    public IReadOnlyList<CompanionCommand>? Match(string input)
    {
        if(input.Length>160)return null;
        if(IntentResolver.Suppressed(input)&&!Regex.IsMatch(input.Trim(),"^(?:请|你)?(?:别动|不要动)[。！!？?吧]*$",RegexOptions.CultureInvariant))return null;
        var whole=Rules.FirstOrDefault(r=>r.Phrases.Any(p=>Normalize(p)==Normalize(input)));
        if(whole is not null)return whole.Commands;
        if(ScreenLap(Normalize(input)) is {} lap)return [new("stroll",lap)];
        var parts=Regex.Split(input.Trim(),"(?:[，,；;]?(?:然后|接着|再)|[，,；;])",RegexOptions.CultureInvariant);
        if(parts.Length is <1 or >3)return null;
        var result=new List<CompanionCommand>();
        foreach(string part in parts)
        {
            string phrase=Normalize(part);var rule=Rules.FirstOrDefault(r=>r.Phrases.Any(p=>Normalize(p)==phrase));
            if(rule is null)return null;result.AddRange(rule.Commands);if(result.Count>3)return null;
        }
        return result;
    }
    private static string Normalize(string text)
    {
        text=Regex.Replace(text.Trim(),"[\\s。！？!?，,、；;：:]","");
        string[] prefixes=["你能不能","能不能","你可以","你能","能否","可以帮我","我想让它","我想要它","麻烦你帮我","请你帮我","你帮我","麻烦你","请你","帮我","让它","让宠物","让伙伴","让小伙伴","带它","叫它","可以","请"];
        for(int i=0;i<3;i++){bool removed=false;foreach(string prefix in prefixes)if(text.StartsWith(prefix,StringComparison.Ordinal)){text=text[prefix.Length..];removed=true;break;}if(!removed)break;}
        for(int i=0;i<3;i++){bool removed=false;foreach(string suffix in new[]{"给我看看","好不好","可以吗","一下","看看","吗","吧","呢"})if(text.EndsWith(suffix,StringComparison.Ordinal)){text=text[..^suffix.Length];removed=true;break;}if(!removed)break;}
        return text;
    }
    private static string? ScreenLap(string text)
    {
        if(text.Length==0||text.Contains('“')||text.Contains('”')||text.Contains('「')||text.Contains('」')||text.Contains('"')||text.Contains('\''))return null;
        if(Regex.IsMatch(text,"(?:不要|別|别|不能|不想|不用|停止|取消|解释|解釋|分析|讨论|討論|意思|支持|功能|玩法|如果|假如|模拟|模擬|想象|想像|假装|假裝|上次|刚才|之前|已经|已經|他说|她说|引用|是否|怎么|如何)",RegexOptions.CultureInvariant))return null;
        if(!Regex.IsMatch(text,"(?:屏幕|螢幕|全屏)",RegexOptions.CultureInvariant)||!Regex.IsMatch(text,"(?:绕|環绕|环绕|围着|围绕|沿着|跑|奔跑|跑动)",RegexOptions.CultureInvariant))return null;
        if(Regex.IsMatch(text,"(?:两|二|2)圈",RegexOptions.CultureInvariant))return "lap2";
        if(Regex.IsMatch(text,"(?:一|1)圈",RegexOptions.CultureInvariant))return "lap1";
        return null;
    }
}
