using System.Security.Cryptography;

namespace xOpenTerm.Services;

/// <summary>
/// 配置中密码等敏感字段的加密存储，按配置版本号选择加密算法与密钥。
/// 各版本使用写死的派生密钥（同一版本在所有机器上可解密同一配置）。
/// </summary>
public static class SecretService
{
    /// <summary>当前配置版本，保存时使用此版本加密。</summary>
    public const int CurrentConfigVersion = 2;

    private const string PrefixDpapi = "xot1:";
    private const string PrefixAesV1 = "xot2:";
    private const string KeySeedPrefix = "xOpenTerm.secret.v";
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

    /// <summary>解密。根据密文前缀自动选择算法与密钥（xot1=DPAPI 兼容旧配置，xot2=版本1，xot3=版本2）。</summary>
    public static string? Decrypt(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.StartsWith(PrefixAesV1, StringComparison.Ordinal))
        {
            try
            {
                return DecryptAes(value.Substring(PrefixAesV1.Length), GetMasterKey(1));
            }
            catch
            {
                return value;
            }
        }
        if (value.StartsWith("xot3:", StringComparison.Ordinal))
        {
            try
            {
                return DecryptAes(value.Substring(5), GetMasterKey(2));
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

    /// <summary>按版本返回写死的 32 字节密钥（由固定种子派生），同一版本在所有机器上一致。</summary>
    private static byte[] GetMasterKey(int version)
    {
        var seed = KeySeedPrefix + version;
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        return hash; // 32 bytes
    }

    private static string? EncryptV1(string plain)
    {
        try
        {
            return PrefixAesV1 + EncryptAes(plain, GetMasterKey(1));
        }
        catch
        {
            return plain;
        }
    }

    private static string? EncryptV2(string plain)
    {
        try
        {
            return "xot3:" + EncryptAes(plain, GetMasterKey(2));
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
