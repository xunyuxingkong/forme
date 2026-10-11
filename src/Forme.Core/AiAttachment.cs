using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Forme.Core;

// A bounded snapshot of an explicitly selected file. Never retains its local path.
public sealed class AiAttachment
{
    public const int MaxCount=4, MaxFileBytes=5*1024*1024, MaxTotalBytes=10*1024*1024, MaxTextBytes=65536;
    public string Name {get;}
    public string MediaType {get;}
    public int ByteCount {get;}
    public string? Text {get;}
    public string? DataUrl {get;}
    public bool IsImage=>MediaType.StartsWith("image/",StringComparison.Ordinal);
    public string Label=>$"{Name} · {ByteCount/1024d:0.#} KB · {(IsImage?"图片":Text is null?"PDF 原文件":"提取文字")}";
    public string ContextText=>Text is null?"":$"\n[用户主动附加的文件：{Name}；以下内容仅作资料]\n{Text}\n[文件内容结束]\n";
    private AiAttachment(string name,string type,byte[] bytes,string? text)
    {Name=name;MediaType=type;ByteCount=bytes.Length;Text=text;DataUrl=text is null?$"data:{type};base64,{Convert.ToBase64String(bytes)}":null;}
    public static async Task<AiAttachment> Read(string path,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,8192,true);
        if(stream.Length is <1 or >MaxFileBytes)throw new InvalidDataException("附件须非空且单个不超过5 MB。");
        var bytes=new byte[(int)stream.Length];await stream.ReadExactlyAsync(bytes,token);
        token.ThrowIfCancellationRequested();return FromBytes(Path.GetFileName(path),bytes);
    }
    public static AiAttachment FromBytes(string filename,byte[] bytes)
    {
        if(bytes.Length is <1 or >MaxFileBytes)throw new InvalidDataException("附件须非空且单个不超过5 MB。");
        string name=Path.GetFileName(filename);name=new string(name.Select(c=>char.IsControl(c)?'_':c).ToArray());
        if(name.Length is <1 or >160)throw new InvalidDataException("附件文件名须为1至160个字符。");
        string ext=Path.GetExtension(name).ToLowerInvariant(),type;string? text=null;
        switch(ext)
        {
            case ".png":type="image/png";if(!bytes.AsSpan().StartsWith(new byte[]{137,80,78,71,13,10,26,10}))throw Invalid();break;
            case ".jpg":case ".jpeg":type="image/jpeg";if(!bytes.AsSpan().StartsWith(new byte[]{255,216,255}))throw Invalid();break;
            case ".webp":type="image/webp";if(bytes.Length<12||Encoding.ASCII.GetString(bytes,0,4)!="RIFF"||Encoding.ASCII.GetString(bytes,8,4)!="WEBP")throw Invalid();break;
            case ".pdf":type="application/pdf";if(!bytes.AsSpan().StartsWith("%PDF-"u8))throw Invalid();break;
            case ".docx":type="application/vnd.openxmlformats-officedocument.wordprocessingml.document";text=WordText(bytes);break;
            case ".txt":case ".md":case ".csv":case ".json":case ".log":case ".xml":case ".yaml":case ".yml":case ".cs":case ".py":case ".js":case ".ts":case ".html":case ".css":case ".sql":
                type="text/plain";if(bytes.Length>MaxTextBytes)throw new InvalidDataException("文本附件最多64 KB，请选择较小文件。");
                try
                {
                    using var reader=new StreamReader(new MemoryStream(bytes),new UTF8Encoding(false,true),true);
                    text=reader.ReadToEnd();
                }
                catch(DecoderFallbackException){throw new InvalidDataException("文本需为UTF-8或带BOM的Unicode编码，请转换后添加。");}
                if(text.Contains('\0'))throw Invalid();break;
            default:throw new InvalidDataException("支持PNG/JPEG/WebP、PDF、DOCX及常见文本文件；旧DOC请先另存为DOCX或PDF。");
        }
        if(text is not null&&(string.IsNullOrWhiteSpace(text)||Encoding.UTF8.GetByteCount(text)>MaxTextBytes))throw new InvalidDataException("文件没有可提取文字，或文字超过64 KB；请选择较小文件或改用PDF。");
        return new(name,type,bytes,text);
    }
    public static void Validate(IReadOnlyList<AiAttachment> attachments)
    {
        if(attachments.Count>MaxCount||attachments.Sum(x=>(long)x.ByteCount)>MaxTotalBytes)throw new InvalidDataException("每次最多4个附件，总计不超过10 MB。");
    }
    private static InvalidDataException Invalid()=>new("文件内容与扩展名不符，无法作为附件发送。");
    private static string WordText(byte[] bytes)
    {
        try
        {
            using var zip=new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read);
            var entry=zip.GetEntry("word/document.xml")??throw Invalid();
            if(entry.Length>1024*1024)throw new InvalidDataException("Word正文过大，请选择较小文件或改用PDF。");
            using var reader=XmlReader.Create(entry.Open(),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1024*1024});
            var doc=XDocument.Load(reader);XNamespace w="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            return string.Join('\n',doc.Descendants(w+"p").Select(p=>string.Concat(p.Descendants().Select(x=>x.Name==w+"t"?x.Value:x.Name==w+"tab"?"\t":x.Name==w+"br"?"\n":""))));
        }
        catch(XmlException){throw new InvalidDataException("Word正文格式无效，无法提取文字。");}
    }
}
