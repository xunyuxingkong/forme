using Forme.Core;

namespace Forme.App;

internal static class CrashDiagnostics
{
    public static void Write(string directory,Exception error)
    {
        try
        {
            string root=Path.Combine(directory,"diagnostics");Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root,DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+"-"+Guid.NewGuid().ToString("N")+".json"),OperationErrors.Diagnostic(error));
            foreach(var file in new DirectoryInfo(root).GetFiles("*.json").OrderByDescending(f=>f.LastWriteTimeUtc).Skip(10))file.Delete();
        }
        catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
