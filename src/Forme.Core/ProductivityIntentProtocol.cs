using System.Globalization;
using System.Text.Json;

namespace Forme.Core;

public static class ProductivityIntentProtocol
{
    public const string Marker="```forme-productivity";
    public static string Instructions()
    {
        var now=DateTimeOffset.Now;
        return $"工作块 ```forme-productivity：项含intent和目录参数，intent目录：{CapabilityCatalog.ModelSignatures(CapabilityDomain.Productivity)}。当前本机时间{now:O}；相对时间据此转为带时区ISO 8601或null，priority=normal/important/urgent。仅保存本轮明确要求的内容；标题或必要时间不明返回[]。删除/修改/完成不支持；既有记录不发送给模型，查询只在本机执行。";
    }
    public static bool TryExtract(string response,out string json)
    {
        json="";int start=response.IndexOf(Marker,StringComparison.Ordinal);if(start<0)return false;
        if(response.IndexOf(Marker,start+Marker.Length,StringComparison.Ordinal)>=0)throw new InvalidDataException("AI返回了多个本地记事/待办操作，没有执行。");
        int end=response.IndexOf("```",start+Marker.Length,StringComparison.Ordinal);
        if(end<0||!string.IsNullOrWhiteSpace(response[..start])||!string.IsNullOrWhiteSpace(response[(end+3)..]))throw new InvalidDataException("AI本地操作格式不完整，没有执行。");
        json=response[(start+Marker.Length)..end].Trim();return true;
    }
    public static IReadOnlyList<ProductivityIntent> Parse(string json,string input)
    {
        try
        {
            if(json.Length>8192)throw new JsonException();using var doc=JsonDocument.Parse(json,new(){MaxDepth=5});
            if(doc.RootElement.ValueKind!=JsonValueKind.Array||doc.RootElement.GetArrayLength()>3)throw new JsonException();
            var result=new List<ProductivityIntent>();
            foreach(var element in doc.RootElement.EnumerateArray())
            {
                if(element.ValueKind!=JsonValueKind.Object)throw new JsonException();var props=element.EnumerateObject().ToArray();
                if(props.Select(x=>x.Name).Distinct().Count()!=props.Length||props.Any(x=>x.Name is not ("intent" or "title" or "content" or "query" or "description" or "dueAt" or "remindAt" or "priority" or "tags")))throw new JsonException();
                string? Text(string name,int max,bool required=false)
                {
                    if(!element.TryGetProperty(name,out var value)||value.ValueKind==JsonValueKind.Null)return required?throw new JsonException():null;
                    if(value.ValueKind!=JsonValueKind.String)throw new JsonException();var text=value.GetString();if(text is null||text.Length>max||required&&string.IsNullOrWhiteSpace(text))throw new JsonException();return text;
                }
                string kind=Text("intent",40,true)!;if(!CapabilityCatalog.AllowsModel(CapabilityDomain.Productivity,kind))throw new JsonException();ProductivityIntent intent;
                switch(kind)
                {
                    case "CreateNote":
                        if(props.Any(x=>x.Name is not ("intent" or "title" or "content" or "tags")))throw new JsonException();
                        var tags=new List<string>();if(element.TryGetProperty("tags",out var tagArray)){if(tagArray.ValueKind!=JsonValueKind.Array||tagArray.GetArrayLength()>20)throw new JsonException();foreach(var tag in tagArray.EnumerateArray()){if(tag.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(tag.GetString())||tag.GetString()!.Length>32)throw new JsonException();tags.Add(tag.GetString()!);}}
                        intent=new(ProductivityIntentType.CreateNote,Text("title",120,true),Text("content",10000,true),Tags:tags);break;
                    case "SearchNote":
                        if(props.Any(x=>x.Name is not ("intent" or "query")))throw new JsonException();intent=new(ProductivityIntentType.SearchNote,Query:Text("query",120,true));break;
                    case "CreateTask":
                        if(props.Any(x=>x.Name is not ("intent" or "title" or "description" or "dueAt" or "remindAt" or "priority")))throw new JsonException();
                        string? priority=Text("priority",16);if(priority is not (null or "normal" or "important" or "urgent"))throw new JsonException();
                        intent=new(ProductivityIntentType.CreateTask,Text("title",200,true),Description:Text("description",2000)??"",DueAt:DateText("dueAt"),RemindAt:DateText("remindAt"),Priority:priority??"normal");break;
                    case "CreateReminder":
                        if(props.Any(x=>x.Name is not ("intent" or "title" or "dueAt")))throw new JsonException();intent=new(ProductivityIntentType.CreateReminder,Text("title",200,true),DueAt:DateText("dueAt",true));break;
                    case "ListTodayTasks":
                        if(props.Length!=1)throw new JsonException();intent=new(ProductivityIntentType.ListTodayTasks);break;
                    default:throw new JsonException();
                }
                string? DateText(string name,bool required=false)
                {
                    var value=Text(name,50,required);if(value is null)return null;
                    if(!DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var date)||date<DateTimeOffset.Now.AddMinutes(-1))throw new JsonException();return date.ToString("O",CultureInfo.InvariantCulture);
                }
                result.Add(intent);
            }
            if(result.Count>0&&string.IsNullOrWhiteSpace(input))throw new JsonException();return result;
        }
        catch(Exception ex) when(ex is JsonException or InvalidOperationException or FormatException or OverflowException)
        {throw new InvalidDataException("没有得到完整合法的记事/待办意图，没有执行；请换一种明确说法。",ex);}
    }
}
