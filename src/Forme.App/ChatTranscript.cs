using Forme.Core;
using System.Windows;
using System.Windows.Controls;

namespace Forme.App;

internal static class ChatTranscript
{
    public static StackPanel Entry(string? nickname,TextBlock text,string? status=null,bool user=false)
    {
        var alignment=user?TextAlignment.Right:TextAlignment.Left;
        var entry=new StackPanel{Margin=new Thickness(0,0,0,18)};
        if(user||nickname is not null)
        {
            var heading=Ui.Text(user?"我":nickname!,11,user?Ui.Muted:Ui.Sage,true);heading.TextAlignment=alignment;heading.Margin=new Thickness(0,0,0,4);entry.Children.Add(heading);
        }
        text.TextAlignment=alignment;text.Margin=new Thickness(0);entry.Children.Add(text);
        if(status is not null){var hint=Ui.Text(status,11,Ui.Muted);hint.TextAlignment=alignment;hint.Margin=new Thickness(0,4,0,0);entry.Children.Add(hint);}
        return entry;
    }

    public static Border Frame(ScrollViewer scroll)=>new(){Child=scroll,Background=Ui.Brush("#FFFFFF"),BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(14,14,6,0)};

    public static string Text(IEnumerable<ChatMessage> messages,string nickname,ChatMessage? pending)
    {
        var entries=messages.Where(x=>x.Status!="streaming").ToList();
        if(pending is not null){entries.RemoveAll(x=>x.Id==pending.Id);entries.Add(pending);}
        return string.Join("\n\n",entries.Select(message=>
        {
            string visible=message.Role=="user"?message.Content:ChatDisplay.Message(message.Content);
            if(visible.Length==0)return "";
            return (message.Role=="user"?"我":nickname)+"\n"+visible;
        }).Where(text=>text.Length>0));
    }

    public static StackPanel Actions(Func<string> text,Action<string> editMemory,bool hasContent)
    {
        var copy=Ui.Button("复制记录",()=>Clipboard.SetText(text()));copy.IsEnabled=hasContent;
        var memory=Ui.Button("存为记忆…",()=>{string draft=text();editMemory(draft.Length>400?draft[..400]:draft);});memory.IsEnabled=hasContent;
        return Ui.Stack(Ui.Row(copy,memory),Ui.Text("作用于当前显示的记录。记忆草稿取前400字，编辑确认后保存。",11,Ui.Muted));
    }
}
