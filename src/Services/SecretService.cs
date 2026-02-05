using System.Security.Cryptography;

namespace xOpenTerm.Services;

/// <summary>
/// 配置中密码等敏感字段的加密存储，按配置版本号选择加密算法与密钥。
/// 版本 1：优先环境变量 XOPENTERM_MASTER_KEY（AES），否则 Windows DPAPI。
/// 更高版本可绑定不同环境变量与算法（如版本 2 使用 XOPENTERM_MASTER_KEY_V2 等）。
/// </summary>
public static class SecretService
{
    /// <summary>当前配置版本，保存时使用此版本加密。</summary>
    public const int CurrentConfigVersion = 1;

    private const string PrefixDpapi = "xot1:";
    private const string PrefixAesV1 = "xot2:";
    private const string EnvMasterKeyV1 = "XOPENTERM_MASTER_KEY";
    private const string EnvMasterKeyV2 = "XOPENTERM_MASTER_KEY_V2";
    private const int AesKeySizeBytes = 32;
    private const int AesBlockSizeBytes = 16;

    /// <summary>加密明文。version 决定使用的算法与密钥；null 或空返回 null。</summary>
    public static string? Encrypt(string? plain, int configVersion)
    {
        if (string.IsNullOrEmpty(plain)) return plain;
        switch (configVersion)
        {
            case 1:
                return EncryptV1(plain);
            case 2:
                return EncryptV2(plain);
            default:
                return EncryptV1(plain);
        }
    }

    /// <summary>解密。根据密文前缀自动选择算法与密钥（xot1=DPAPI，xot2=版本1 主密钥，xot3=版本2 主密钥）。</summary>
    public static string? Decrypt(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.StartsWith(PrefixAesV1, StringComparison.Ordinal))
        {
            var key = GetMasterKey(1);
            if (key == null) return value;
            try
            {
                return DecryptAes(value.Substring(PrefixAesV1.Length), key);
            }
            catch
            {
                return value;
            }
        }
        if (value.StartsWith("xot3:", StringComparison.Ordinal))
        {
            var key = GetMasterKey(2);
            if (key == null) return value;
            try
            {
                return DecryptAes(value.Substring(5), key);
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

    /// <summary>生成新的 32 字节主密钥的 Base64，可用于环境变量。</summary>
    public static string GenerateMasterKeyBase64()
    {
        var key = new byte[AesKeySizeBytes];
        RandomNumberGenerator.Fill(key);
        return Convert.ToBase64String(key);
    }

    /// <summary>读取指定版本的主密钥（Base64，32 字节）。版本 1→XOPENTERM_MASTER_KEY，2→XOPENTERM_MASTER_KEY_V2。</summary>
    public static byte[]? GetMasterKey(int version)
    {
        var envName = version == 2 ? EnvMasterKeyV2 : EnvMasterKeyV1;
        var raw = Environment.GetEnvironmentVariable(envName);
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

    private static string? EncryptV1(string plain)
    {
        var key = GetMasterKey(1);
        if (key != null)
        {
            try
            {
                return PrefixAesV1 + EncryptAes(plain, key);
            }
            catch
            {
                // fallback to DPAPI
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

    private static string? EncryptV2(string plain)
    {
        var key = GetMasterKey(2);
        if (key == null) return plain;
        try
        {
            return "xot3:" + EncryptAes(plain, key);
        }
        catch
        {
            return plain;
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
        if (colon <= 0) throw new InvalidOperationException("Invalid AES payload.");
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
