namespace Forme.Core;

public static class EnvironmentFeedback
{
    // Invoked only after an explicit exploration click; no timer, reward or network call.
    public static string Explore(EnvironmentContext context,Random? random=null)
    {
        string[] choices=context.Weather switch
        {
            "snow"=>["雪花落在草地上，伙伴抬头看了看。","风铃旁积了薄薄的雪，伙伴安静地望着。"],
            "rain"=>["池塘泛起小小水纹，伙伴静静看着。","雨滴落在树叶上，伙伴听了一会儿。"],
            _ when context.IsNight=>["微风吹过草地，伙伴抬头看看夜空。","池塘映着夜色，伙伴在旁边安静陪你。"],
            _=>["一只蝴蝶掠过草地，伙伴抬头看了看。","风吹动叶片，伙伴在花园里看看。","小鸟飞过凉亭，伙伴望了一会儿。"]
        };
        return choices[(random??Random.Shared).Next(choices.Length)]+"稍后再探索新的收藏。";
    }
}
