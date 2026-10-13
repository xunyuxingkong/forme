using System.Globalization;
using System.Text.Json;

namespace Forme.Core;

public static class ProductivityIntentProtocol
{
    public const string Marker="```forme-productivity";
    public static string Instructions()
    {
        var now=DateTimeOffset.Now;
        return $"若用户明确要求记事、查询本地记事、创建工作待办、列出今日待办或设置提醒，且不是闲聊/举例/提问/否定，只输出一个 ```forme-productivity 代码块，内容为JSON数组，最多3项，不附带解释。仅识别本轮用户明确给出的内容，不读取或假设本地已有数据。当前本机时间为 {now:O}，相对日期据此转换为含时区的 ISO 8601。支持 intent: CreateNote(title,content,tags), SearchNote(query), CreateTask(title,description,dueAt,remindAt,priority), ListTodayTasks(), CreateReminder(title,dueAt)。字段名严格按括号内；时间使用完整 ISO 8601 或 null；priority 为 normal/important/urgent。仅创建用户明确要求保存的内容；不确定时间或标题时输出空数组。删除、修改、完成操作不在当前支持范围内。绝不要输出记事内容、待办标题等用户数据。";
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
                string kind=Text("intent",40,true)!;ProductivityIntent intent;
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
