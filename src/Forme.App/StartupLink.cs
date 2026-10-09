using System.Runtime.InteropServices;

namespace Forme.App;

internal static class StartupLink
{
    private static string Location=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),"Forme.lnk");
    public static bool Exists=>File.Exists(Location);
    public static void Set(bool enabled)
    {
        if(!enabled){if(File.Exists(Location))File.Delete(Location);return;}
        var type=Type.GetTypeFromProgID("WScript.Shell")??throw new IOException("Windows 快捷方式组件不可用。");
        dynamic shell=Activator.CreateInstance(type)!;dynamic link=shell.CreateShortcut(Location);
        try{link.TargetPath=Environment.ProcessPath!;link.WorkingDirectory=AppContext.BaseDirectory;link.Description="Forme 陪伴小屋";link.Save();}
        finally{Marshal.FinalReleaseComObject(link);Marshal.FinalReleaseComObject(shell);}
    }
}
