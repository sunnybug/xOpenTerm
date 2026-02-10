using System.IO;
using System.Security.Cryptography;

namespace xOpenTerm.Services;

/// <summary>
/// 主密码服务：从用户主密码派生加密密钥，并生成/校验验证码（用于启动时验证主密码是否正确）。
/// 使用 PBKDF2 派生 32 字节密钥，验证码为密钥的 SHA256，仅用于校验不参与解密。
/// 支持将派生密钥用 DPAPI 加密后保存到本地文件，下次启动时自动读取，无需再次输入主密码。
/// </summary>
public static class MasterPasswordService
{
    private const int KeySizeBytes = 32;
    private const int SaltSizeBytes = 16;
    private const int Pbkdf2Iterations = 120000;
    private const string SavedKeyFileName = "masterkey.dat";

    /// <summary>保存密钥的文件路径：%LocalAppData%\xOpenTerm\masterkey.dat（仅当前用户可解密）。</summary>
    public static string GetSavedKeyPath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "xOpenTerm");
        return Path.Combine(dir, SavedKeyFileName);
    }

    /// <summary>生成随机盐（用于首次设置主密码）。</summary>
    public static byte[] GenerateSalt()
    {
        return RandomNumberGenerator.GetBytes(SaltSizeBytes);
    }

    /// <summary>从主密码和盐派生 32 字节密钥（用于加解密配置中的密码与 SecretKey）。</summary>
    public static byte[] DeriveKey(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            System.Text.Encoding.UTF8.GetBytes(password),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            KeySizeBytes);
    }

    /// <summary>从密钥生成验证码（存储到设置，下次启动时校验主密码）。</summary>
    public static byte[] GetVerifierFromKey(byte[] key)
    {
        return SHA256.HashData(key);
    }

    /// <summary>校验主密码：用同一盐派生密钥，比较验证码是否一致。</summary>
    public static bool VerifyPassword(string password, byte[] salt, byte[] storedVerifier)
    {
        if (storedVerifier == null || storedVerifier.Length != 32) return false;
        var key = DeriveKey(password, salt);
        var verifier = GetVerifierFromKey(key);
        return CryptographicOperations.FixedTimeEquals(verifier, storedVerifier);
    }

    /// <summary>将派生密钥用 DPAPI 加密后保存到本地文件，以后启动时可自动读取无需再输入主密码。</summary>
    public static void SaveKeyToFile(byte[] key)
    {
        if (key == null || key.Length != KeySizeBytes) return;
        var path = GetSavedKeyPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var protectedBytes = ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, protectedBytes);
    }

    /// <summary>删除本地保存的密钥文件（清除主密码时调用）。</summary>
    public static void DeleteSavedKeyFile()
    {
        var path = GetSavedKeyPath();
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { /* 忽略 */ }
        }
    }

    /// <summary>从本地文件读取并解密密钥；用 storedVerifier 校验是否与当前配置一致，一致则返回密钥否则返回 null。</summary>
    public static byte[]? TryLoadKeyFromFile(byte[] storedVerifier)
    {
        if (storedVerifier == null || storedVerifier.Length != 32) return null;
        var path = GetSavedKeyPath();
        if (!File.Exists(path)) return null;
        try
        {
            var protectedBytes = File.ReadAllBytes(path);
            var key = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            if (key.Length != KeySizeBytes) return null;
            var verifier = GetVerifierFromKey(key);
            if (!CryptographicOperations.FixedTimeEquals(verifier, storedVerifier))
                return null;
            return key;
        }
        catch
        {
            return null;
        }
    }
}
