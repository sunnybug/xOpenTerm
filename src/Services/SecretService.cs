using System.Security.Cryptography;

namespace xOpenTerm.Services;

/// <summary>使用 Windows DPAPI 对配置中的密码等敏感字段加密存储，仅当前用户可解密。</summary>
public static class SecretService
{
    private const string Prefix = "xot1:";

    /// <summary>加密明文。null 或空字符串返回 null；否则返回带前缀的 Base64 密文。</summary>
    public static string? Encrypt(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return plain;
        try
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(plain);
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(protectedBytes);
        }
        catch
        {
            return plain;
        }
    }

    /// <summary>解密。若值带 xot1: 前缀则解密，否则视为历史明文原样返回。</summary>
    public static string? Decrypt(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (!value.StartsWith(Prefix, StringComparison.Ordinal)) return value;
        try
        {
            var base64 = value.Substring(Prefix.Length);
            var protectedBytes = Convert.FromBase64String(base64);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return value;
        }
    }
}
