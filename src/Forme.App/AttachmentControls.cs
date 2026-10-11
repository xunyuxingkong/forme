using Forme.Core;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Forme.App;

internal static class AttachmentControls
{
    public static Button PickButton(Controller c,Window owner)
    {
        var button=Ui.AsyncButton("添加附件",async()=>
        {
            if(c.Busy)return;
            var picker=new OpenFileDialog{Title="添加图片或文件 · 点击发送才会上传",Multiselect=true,Filter="支持的附件|*.png;*.jpg;*.jpeg;*.webp;*.pdf;*.docx;*.txt;*.md;*.csv;*.json;*.log;*.xml;*.yaml;*.yml;*.cs;*.py;*.js;*.ts;*.html;*.css;*.sql|图片|*.png;*.jpg;*.jpeg;*.webp|文档|*.pdf;*.docx;*.txt;*.md;*.csv;*.json"};
            if(picker.ShowDialog(owner)==true)await c.AddAttachmentFiles(picker.FileNames);
        });
        button.IsEnabled=!c.Busy;return button;
    }
    public static UIElement List(Controller c,Window owner)
    {
        var list=Ui.Row();
        foreach(var item in c.Attachments)
        {
            var name=Ui.Button(item.Name,()=>Preview(item,owner));name.ToolTip=item.Label;name.MaxWidth=180;
            var remove=Ui.Button("移除",()=>c.RemoveAttachment(item));remove.IsEnabled=!c.Busy;
            foreach(var b in new[]{name,remove}){b.FontSize=11;b.Padding=new Thickness(7,3,7,3);b.MinHeight=24;}
            list.Children.Add(Ui.Row(name,remove));
        }
        return new ScrollViewer{Content=list,MaxHeight=48,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Visibility=c.Attachments.Count==0?Visibility.Collapsed:Visibility.Visible};
    }
    public static void Preview(AiAttachment item,Window owner)
    {
        UIElement body;
        if(item.Text is {} content){var text=Ui.Input(content,true,100000,"附件提取文字");text.IsReadOnly=true;body=text;}
        else if(item.IsImage && item.MediaType!="image/webp")
        {
            try
            {
                string data=item.DataUrl!;using var stream=new MemoryStream(Convert.FromBase64String(data[(data.IndexOf(',')+1)..]));
                var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=640;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();
                body=new Image{Source=bitmap,MaxHeight=360,Stretch=System.Windows.Media.Stretch.Uniform};
            }
            catch(Exception ex) when(ex is NotSupportedException or FileFormatException or ArgumentException){body=Ui.Text("无法显示图片预览，请检查图片是否有效。",13);}
        }
        else body=Ui.Text(item.IsImage?"WebP将按原文件发送，当前预览不解码。":"PDF将按原文件发送，需服务商支持PDF文件输入。",13);
        var window=new Window{Title="附件预览 · 尚未上传",Owner=owner,Width=640,Height=520,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        window.Content=Ui.Card(new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=Ui.Stack(Ui.Text(item.Label,13),Ui.Text(item.Text is null?"原文件随本次消息发送；原内容不保存到聊天记录。":"仅发送提取文字。DOCX中的图片、批注和页眉页脚不发送；原内容不保存到聊天记录。",12,Ui.Muted),body)});window.ShowDialog();
    }
}
