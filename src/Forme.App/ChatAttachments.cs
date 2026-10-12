using Forme.Core;

namespace Forme.App;

internal sealed partial class Controller
{
    private readonly List<AiAttachment> _attachments=[];
    public IReadOnlyList<AiAttachment> Attachments=>_attachments.AsReadOnly();
    private CancellationTokenSource? _attachmentRead;
    private AttachmentRetry? _attachmentRetry;
    private sealed record AttachmentRetry(string Id,string Input,string Endpoint,IReadOnlyList<AiAttachment> Items);
    public async Task AddAttachmentFiles(IReadOnlyList<string> paths)
    {
        if(Busy)throw new OperationFailureException("请等待当前操作结束。");
        if(_attachments.Count+paths.Count>AiAttachment.MaxCount)throw new InvalidDataException("每次最多4个附件。");
        bool renderPdf=AiClient.RequiresPdfImages(Preferences.Endpoint);
        var existing=_attachments.ToArray();using var cancel=new CancellationTokenSource();_attachmentRead=cancel;ChatStatus=renderPdf?"正在本地读取附件；PDF将转为页面图片（最多4页），可停止，尚未上传。":"正在本地读取附件，可点击停止；尚未上传。";Changed?.Invoke();
        try
        {
            var items=await Task.Run(async()=>
            {
                var loaded=new List<AiAttachment>();
                foreach(string path in paths){cancel.Token.ThrowIfCancellationRequested();var item=await AiAttachment.Read(path,cancel.Token);AiAttachment.Validate(existing.Concat(loaded).Append(item).ToArray());if(renderPdf&&item.MediaType=="application/pdf")item=await PdfAttachments.Render(item,cancel.Token);loaded.Add(item);AiAttachment.Validate(existing.Concat(loaded).ToArray());}
                return loaded;
            },cancel.Token);
            cancel.Token.ThrowIfCancellationRequested();_attachments.AddRange(items);ChatStatus="附件已添加，点击发送才会上传；可在发送预览查看。";
        }
        catch(OperationCanceledException){ChatStatus="已取消读取附件，未上传。";}
        catch(Exception ex) when(OperationErrors.Expected(ex)){ChatStatus="附件未添加："+OperationErrors.Message(ex);throw;}
        finally{_attachmentRead=null;Changed?.Invoke();}
    }
    internal void AddAttachments(IReadOnlyList<AiAttachment> items)
    {if(Busy)throw new OperationFailureException("请等待当前操作结束。");AiAttachment.Validate(_attachments.Concat(items).ToArray());_attachments.AddRange(items);Changed?.Invoke();}
    public void RemoveAttachment(AiAttachment item){if(Busy)return;_attachments.Remove(item);Changed?.Invoke();}
    private void ClearAttachments(){_attachments.Clear();_attachmentRetry=null;}
}
