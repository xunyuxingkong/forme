using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Forme.Core;

public sealed class OperationFailureException(string message):Exception(message);

public static class OperationErrors
{
    public static bool Expected(Exception error)=>error is ActionFailureException or OperationFailureException or InvalidDataException or ArgumentException or IOException or UnauthorizedAccessException or TimeoutException or OperationCanceledException or SqliteException or System.Security.Cryptography.CryptographicException;
    public static string Message(Exception error)=>error switch
    {
        ActionFailureException or OperationFailureException or InvalidDataException or TimeoutException=>error.Message,
        OperationCanceledException=>"操作已取消。",
        SqliteException=>"本机数据库操作失败，请检查存储空间、权限或稍后重试。",
        IOException or UnauthorizedAccessException=>"本机文件操作失败，请检查存储空间和权限。",
        System.Security.Cryptography.CryptographicException=>"凭据无法解密，请重新配置本机密钥。",
        _=>"输入或操作参数无效。"
    };
    // Never serialize exception messages, Data, paths, HTTP payloads or inner exceptions.
    public static string Diagnostic(Exception error)=>JsonSerializer.Serialize(new
    {
        At=DateTimeOffset.UtcNow,Type=error.GetType().FullName,error.HResult,
        Version=typeof(OperationErrors).Assembly.GetName().Version?.ToString(),
        Frames=new StackTrace(error,false).GetFrames()?.Take(24).Select(f=>new{Type=f.GetMethod()?.DeclaringType?.FullName,Method=f.GetMethod()?.Name}).ToArray()
    });
}
