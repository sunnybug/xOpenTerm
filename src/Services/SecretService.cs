using System.Security.Cryptography;

namespace xOpenTerm.Services;

/// <summary>
/// 配置中密码等敏感字段的加密存储。
/// 优先使用环境变量 XOPENTERM_MASTER_KEY（Base64 的 32 字节密钥），同一密钥在不同机器上可解密同一配置；
/// 未设置时使用 Windows DPAPI（仅当前机器当前用户可解密）。
/// </summary>
public static class SecretService
{
    private const string PrefixDpapi = "xot1:";
    private const string PrefixAes = "xot2:";
    private const string EnvMasterKey = "XOPENTERM_MASTER_KEY";
    private const int AesKeySizeBytes = 32;
    private const int AesBlockSizeBytes = 16;

    /// <summary>加密明文。null 或空字符串返回 null；否则返回带前缀的密文。</summary>
    public static string? Encrypt(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return plain;
        var key = GetMasterKey();
        if (key != null)
        {
            try
            {
                return PrefixAes + EncryptAes(plain, key);
            }
            catch
            {
                return plain;
            }
        }
        try
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(plain);
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return PrefixDpapi + Convert.ToBase64String(protectedBytes);
        }
        catch
        {
            return plain;
        }
    }

    /// <summary>解密。xot2: 用主密钥解密，xot1: 用 DPAPI，否则视为明文。</summary>
    public static string? Decrypt(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.StartsWith(PrefixAes, StringComparison.Ordinal))
        {
            var key = GetMasterKey();
            if (key == null) return value;
            try
            {
                return DecryptAes(value.Substring(PrefixAes.Length), key);
            }
            catch
            {
                return value;
            }
        }
        if (value.StartsWith(PrefixDpapi, StringComparison.Ordinal))
        {
            try
            {
                var base64 = value.Substring(PrefixDpapi.Length);
                var protectedBytes = Convert.FromBase64String(base64);
                var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return System.Text.Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return value;
            }
        }
        return value;
    }

    /// <summary>生成新的 32 字节主密钥的 Base64 字符串，可用于设置环境变量 XOPENTERM_MASTER_KEY。</summary>
    public static string GenerateMasterKeyBase64()
    {
        var key = new byte[AesKeySizeBytes];
        RandomNumberGenerator.Fill(key);
        return Convert.ToBase64String(key);
    }

    /// <summary>从环境变量读取主密钥（Base64，须为 32 字节）。未设置或格式错误返回 null。</summary>
    public static byte[]? GetMasterKey()
    {
        var raw = Environment.GetEnvironmentVariable(EnvMasterKey);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            var key = Convert.FromBase64String(raw.Trim());
            return key.Length == AesKeySizeBytes ? key : null;
        }
        catch
        {
            return null;
        }
    }

    private static string EncryptAes(string plain, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        using var enc = aes.CreateEncryptor();
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plain);
        var cipher = enc.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        return Convert.ToBase64String(aes.IV) + ":" + Convert.ToBase64String(cipher);
    }

    private static string DecryptAes(string payload, byte[] key)
    {
        var colon = payload.IndexOf(':');
        if (colon <= 0) throw new InvalidOperationException("Invalid xot2 payload.");
        var iv = Convert.FromBase64String(payload.Substring(0, colon));
        var cipher = Convert.FromBase64String(payload.Substring(colon + 1));
        if (iv.Length != AesBlockSizeBytes) throw new InvalidOperationException("Invalid IV length.");
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        using var dec = aes.CreateDecryptor();
        var plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
        return System.Text.Encoding.UTF8.GetString(plain);
    }
}
