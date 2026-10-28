using System.Security.Cryptography;
using Forme.Core;

namespace Forme.App;

// Optional model data belongs to this installation's data directory, never the
// assembly or release payload. The downloaded avatar forbids redistribution.
internal static class LocalPetModels
{
    internal const string HuTaoId="genshin-hu-tao";
    internal const string HuTaoSha256="E6E86FB91C28417864ED30305A434541FB9CF214DBCB6F2555AF8795A5E374C7";
    internal const string HuTaoSource="https://github.com/AashooSharma/VRM-models/blob/main/Genshin/Hu%20Tao.vrm";
    internal static string DirectoryPath {get;set;}=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Forme","Models");
    internal static string HuTaoPath=>Path.Combine(DirectoryPath,"genshin-hu-tao.vrm");
    private static (string Path,long Length,long Modified,bool Valid)? _validated;

    internal static bool HuTaoAvailable
    {
        get
        {
            try
            {
                var file=new FileInfo(HuTaoPath);if(!file.Exists)return false;
                if(_validated is {} cached&&cached.Path==file.FullName&&cached.Length==file.Length&&cached.Modified==file.LastWriteTimeUtc.Ticks)return cached.Valid;
                bool valid=file.Length==7_721_368&&Hash(file.FullName)==HuTaoSha256;
                _validated=(file.FullName,file.Length,file.LastWriteTimeUtc.Ticks,valid);return valid;
            }
            catch(IOException){return false;}
            catch(UnauthorizedAccessException){return false;}
        }
    }

    internal static void InstallHuTao(string source)
    {
        var file=new FileInfo(source);
        if(!file.Exists||file.Length!=7_721_368||Hash(source)!=HuTaoSha256)
            throw new OperationFailureException("这不是已适配的胡桃 VRM 文件，请使用模型说明中的资源版本。原有模型保持不变。");
        Directory.CreateDirectory(DirectoryPath);
        if(Path.GetFullPath(source).Equals(Path.GetFullPath(HuTaoPath),StringComparison.OrdinalIgnoreCase))return;
        string temporary=HuTaoPath+".tmp";
        try{File.Copy(source,temporary,true);File.Move(temporary,HuTaoPath,true);_validated=null;}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }

    internal static void RequireAvailable(string? id)
    {
        if(id==HuTaoId&&!HuTaoAvailable)throw new ActionFailureException(ActionResultCode.TargetUnavailable,"胡桃模型尚未导入或文件已损坏，请到设置中的伙伴模型处导入；本次没有切换形象。");
    }

    private static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
}
