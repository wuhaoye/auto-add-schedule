using System.Security.Cryptography;
using System.Text;

namespace AutoAddSchedule.Core;

/// <summary>
/// 使用 Windows DPAPI（当前用户作用域）加密本地密钥。只有同一 Windows 用户能解密。
/// </summary>
public static class SecretProtector
{
    public static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(plainText);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(string? protectedBase64)
    {
        if (string.IsNullOrWhiteSpace(protectedBase64))
        {
            return string.Empty;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(protectedBase64);
            var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            AppLog.Warn("解密本地密钥失败（可能换了 Windows 用户）：" + ex.Message);
            return string.Empty;
        }
    }
}
