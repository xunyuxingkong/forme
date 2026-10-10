using System.Text.RegularExpressions;

namespace Forme.Core;

public sealed record IntentResolution(IReadOnlyList<CompanionIntent> Intents, bool IsActionRequest, bool Suppressed = false);
public static class IntentResolver
{
    private const RegexOptions Options = RegexOptions.CultureInvariant;
    private static bool Matches(string value,string pattern) => Regex.IsMatch(value,pattern,Options,TimeSpan.FromMilliseconds(50));
    public static bool Suppressed(string text) => Matches(text.Replace("能不能","可以",StringComparison.Ordinal),"[\"“”‘’「」『』`]|如果|假如|要是|解释|什么意思|支持吗|支持么|为什么|刚才|之前|昨天|他说|她说|我说|讨论|指令|命令|如何|怎么|不要|不用|不想|别|不需要|不能|不可以|不.*(?:去|走|跑|舞|动|玩|睡)");
    public static IntentResolution Resolve(string input,ActionRules? rules = null,string? petName = null)
    {
        if(input.Length > 2000) return new([],false,true);
        string text = input.Trim();
        // Stop is a deliberate negative command; all other negatives, quotes and hypotheticals are data.
        if(Matches(text,"^(?:请|你)?(?:别动|不要动|停下|停止动作|别跑了|别走了)[。！!？?吧]*$")) return new([new(CompanionIntentType.Stop)],true);
        if(Suppressed(text)) return new([],false,true);
        if(rules?.Match(text) is {} fast) return new(fast.Select(ActionPlanner.FromCommand).ToArray(),true);
        if(petName is {Length:>0} && text.StartsWith(petName,StringComparison.Ordinal)) text = text[petName.Length..].TrimStart('，',',',' ');
        var parts = Regex.Split(text,"(?:[，,；;]?(?:然后|接着|再)|[，,；;])",Options,TimeSpan.FromMilliseconds(50));
        if(parts.Length > 3) return new([],true);
        var intents = new List<CompanionIntent>();
        foreach(string part in parts)
        {
            string phrase = Clean(part);
            var intent = Parse(phrase);
            if(intent is null) return new([],LooksLikeRequest(text));
            intents.Add(intent);
        }
        return new(intents,true);
    }
    public static bool LooksLikeRequest(string text,string? petName=null)
    {
        if(petName is {Length:>0}&&text.StartsWith(petName,StringComparison.Ordinal))text=text[petName.Length..].TrimStart('，',',',' ');
        return !Suppressed(text)&&Matches(text,"^(?:(?:你能不能|能不能|你可以|你能|你|能否|可以|麻烦你|请你|请|帮我|先|依次|让伙伴|让宠物|让它|自己|再|试试|出去|到附近|在附近|在周围))*?(?:去|到|走|跑|溜达|转悠|逛|活动|跳|舞|睡|回来|过来|换成|打开|关闭|关灯|玩|散步|拍拍|揉揉|停|别动|不要动|布置|看|吃|读|浇水)");
    }
    private static string Clean(string text)
    {
        text = Regex.Replace(text.Trim(),"[\\s。！？!?]","",Options,TimeSpan.FromMilliseconds(50));
        text = Regex.Replace(text,"^(?:(?:你能不能|能不能|你可以|你能|你|能否|可以|麻烦你|请你|请|帮我|先|让伙伴|让宠物|让它|自己))+","",Options,TimeSpan.FromMilliseconds(50));
        return Regex.Replace(text,"(?:给我看看|好不好|可以吗|一下吧|一下|一会儿|一会|会儿|待会|看看|吗|吧|呢|了)+$","",Options,TimeSpan.FromMilliseconds(50));
    }
    private static CompanionIntent? Parse(string text)
    {
        if(Matches(text,"^(?:停|停下|停止|别动|不要动)$")) return new(CompanionIntentType.Stop);
        if(Matches(text,"^(?:过来|回来|回到我身边)$")) return new(CompanionIntentType.Recall);
        if(Matches(text,"^(?:去户外|到户外|去花园|回小屋|回屋里)$")) return new(CompanionIntentType.ChangeScene,text.StartsWith("回",StringComparison.Ordinal)?"indoor":"outdoor");
        var targets = TargetCatalog.All.Where(t => t.Aliases.Any(alias => text.Contains(alias,StringComparison.Ordinal))).ToArray();
        if(targets.Length > 1) return null;
        if(targets.Length == 1)
        {
            var target = targets[0];
            string alias = target.Aliases.Where(a => text.Contains(a,StringComparison.Ordinal)).OrderByDescending(a => a.Length).First();
            string shape = text.Replace(alias,"@",StringComparison.Ordinal);
            if(Matches(shape,"^(?:去|到|走到|走去|跑到|跑去|过去|前往|去找)(?:看看)?@(?:旁边|那边|那里|边|旁|去)?(?:待|待会|待一会|转悠|溜达|走走)?$"))
                return new(CompanionIntentType.GoTo,target.Id,text.Contains('跑')?"run":"walk");
            if(Matches(shape,"^(?:看看|看|读|吃|摸摸|玩|使用|和)@(?:互动)?$") && target.Capabilities.HasFlag(TargetCapabilities.Interact))
                return new(CompanionIntentType.Interact,target.Id);
        }
        if(Matches(text,"^(?:看看水|看水)$")) return new(CompanionIntentType.Interact,"pond");
        if(Matches(text,"^(?:浇水|给植物浇水)$")) return new(CompanionIntentType.Interact,"water");
        if(Matches(text,"^(?:出去|到附近|在附近|在周围|附近|周围|四处|到处)?(?:走走|走两步|走几步|走一圈|散步|散散步|溜达|转悠|逛|活动活动|活动活动腿脚)$")) return new(CompanionIntentType.Stroll,Mode:"walk");
        if(Matches(text,"^(?:出去|附近)?(?:跑跑|跑两步|跑几步|奔跑)$")) return new(CompanionIntentType.Stroll,Mode:"run");
        if(Matches(text,"^(?:跳舞|跳个舞|跳支舞|摇摆舞|转圈|转一圈|转个圈|蹦跳舞)$")) return new(CompanionIntentType.Dance,Mode:text.Contains('转')?"spin":text.Contains('蹦')?"hop":"sway");
        if(Matches(text,"^(?:睡觉|睡|睡一会|休息)$")) return new(CompanionIntentType.Idle,Mode:"sleep");
        if(Matches(text,"^(?:随机|自由)(?:走动|奔跑)$")) return new(CompanionIntentType.Idle,Mode:text.Contains("奔跑",StringComparison.Ordinal)?"run":"walk");
        return null;
    }
}
