using System.Runtime.InteropServices;
using System.Text;

namespace Forme.App;

internal sealed class Secrets(string directory) : Forme.Core.ISecretStore
{
    private string PathName => Path.Combine(directory,"ai.secret");
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll",SetLastError=true)] private static extern bool CryptProtectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("crypt32.dll",SetLastError=true)] private static extern bool CryptUnprotectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr data);
    private static byte[] Transform(byte[] bytes,bool protect)
    {
        var input=new Blob {Length=bytes.Length,Data=Marshal.AllocHGlobal(bytes.Length)};
        try
        {
            Marshal.Copy(bytes,0,input.Data,bytes.Length);
            Blob output; bool ok=protect?CryptProtectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output):CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
            if(!ok) throw new IOException("Windows 凭据保护失败，请重新填写密钥。");
            try {var result=new byte[output.Length];Marshal.Copy(output.Data,result,0,result.Length);return result;} finally{LocalFree(output.Data);}
        }
        finally {for(int i=0;i<input.Length;i++)Marshal.WriteByte(input.Data,i,0);Marshal.FreeHGlobal(input.Data);}
    }
    public string Read() => File.Exists(PathName)?Encoding.UTF8.GetString(Transform(File.ReadAllBytes(PathName),false)):"";
    public bool Exists => File.Exists(PathName);
    public void Save(string key)
    {
        if(string.IsNullOrWhiteSpace(key)){Delete();return;}
        var temp=PathName+".tmp";File.WriteAllBytes(temp,Transform(Encoding.UTF8.GetBytes(key),true));File.Move(temp,PathName,true);
    }
    public void Delete(){if(File.Exists(PathName))File.Delete(PathName);if(File.Exists(PathName+".tmp"))File.Delete(PathName+".tmp");}
}
