using System.IO;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Security;

namespace xOpenTerm.Services;

/// <summary>
/// 将 PEM/OpenSSH 私钥转为 PuTTY .ppk 格式，纯进程内完成、无交互。支持 RSA、DSA、ECDSA、Ed25519。
/// </summary>
public static class PemToPpkConverter
{
    /// <summary>
    /// 将密钥文件转为 PPK 并写入 <paramref name="ppkPath"/>。使用 Encryption: none，不加密 PPK。
    /// </summary>
    /// <returns>转换成功返回 true，否则 false（如密钥类型不支持或加载失败）。</returns>
    public static bool TryConvert(string keyPath, string ppkPath, string? keyPassphrase)
    {
        try
        {
            using var keyFile = new PrivateKeyFile(keyPath, keyPassphrase ?? "");
            var key = keyFile.Key;
            if (key == null) return false;

            var keyType = key.GetType();
            var name = keyType.Name;
            if (name == "RsaKey") return TryWriteRsaPpk(key, ppkPath);
            if (name == "DsaKey") return TryWriteDsaPpk(key, ppkPath);
            if (name == "EcdsaKey") return TryWriteEcdsaPpk(key, ppkPath);
            if (name == "ED25519Key") return TryWriteEd25519Ppk(key, ppkPath);
            return false;
        }
        catch (Exception ex)
        {
            ExceptionLog.WriteInfo($"[PemToPpk] 加载或转换失败: {ex.Message}");
            return false;
        }
    }

    private static bool TryWriteRsaPpk(Key key, string ppkPath)
    {
        var t = key.GetType();
        var modulus = GetBigInt(t, key, "Modulus", "_modulus", "modulus");
        var exponent = GetBigInt(t, key, "Exponent", "_exponent", "exponent");
        var d = GetBigInt(t, key, "D", "_d", "d");
        var p = GetBigInt(t, key, "P", "_p", "p");
        var q = GetBigInt(t, key, "Q", "_q", "q");
        var inverseQ = GetBigInt(t, key, "InverseQ", "_inverseQ", "inverseQ");

        if (modulus == null || exponent == null || d == null || p == null || q == null || inverseQ == null)
            return false;

        const string algorithmName = "ssh-rsa";
        const string comment = "converted-by-xOpenTerm";

        var publicBlob = BuildSshRsaPublicBlob(exponent.Value, modulus.Value);
        // PuTTY 实际写入顺序为 d, p, q, iqmp（与附录 C 描述不同，以参考 PPK 为准）
        var privateBlob = BuildPpkRsaPrivateBlob(d.Value, p.Value, q.Value, inverseQ.Value);

        WritePpkFile(ppkPath, algorithmName, publicBlob, privateBlob, comment);
        return true;
    }

    private static bool TryWriteDsaPpk(Key key, string ppkPath)
    {
        var t = key.GetType();
        var p = GetBigInt(t, key, "P", "_p", "p");
        var q = GetBigInt(t, key, "Q", "_q", "q");
        var g = GetBigInt(t, key, "G", "_g", "g");
        var y = GetBigInt(t, key, "Y", "_y", "y");
        var x = GetBigInt(t, key, "X", "_x", "x");
        if (p == null || q == null || g == null || y == null || x == null) return false;

        const string algorithmName = "ssh-dss";
        const string comment = "converted-by-xOpenTerm";
        var publicBlob = BuildSshDssPublicBlob(p.Value, q.Value, g.Value, y.Value);
        var privateBlob = ToMpint(x.Value);
        WritePpkFile(ppkPath, algorithmName, publicBlob, privateBlob, comment);
        return true;
    }

    private static bool TryWriteEcdsaPpk(Key key, string ppkPath)
    {
        var t = key.GetType();
        var algorithmName = key.ToString();
        if (string.IsNullOrEmpty(algorithmName) || !algorithmName.StartsWith("ecdsa-sha2-nistp", StringComparison.Ordinal))
            return false;
        var curveName = GetString(t, key, "Curve", "_curve", "curve", "Name", "_name")
            ?? (algorithmName.StartsWith("ecdsa-sha2-", StringComparison.Ordinal) ? algorithmName["ecdsa-sha2-".Length..] : null);
        if (string.IsNullOrEmpty(curveName)) curveName = "nistp256";
        var publicKey = GetByteArray(t, key, "PublicKey", "_publicKey", "Public", "Q", "_q");
        var privateKey = GetByteArray(t, key, "PrivateKey", "_privateKey", "Private", "PrivateExponent", "_privateExponent");
        if (publicKey == null || publicKey.Length == 0 || privateKey == null || privateKey.Length == 0)
            return false;
        var curveBytes = Encoding.ASCII.GetBytes(curveName);
        var publicBlob = BuildSshEcdsaPublicBlob(algorithmName, curveBytes, publicKey);
        var privateExp = BytesToBigInteger(privateKey);
        var privateBlob = ToMpint(privateExp);
        const string comment = "converted-by-xOpenTerm";
        WritePpkFile(ppkPath, algorithmName, publicBlob, privateBlob, comment);
        return true;
    }

    private static bool TryWriteEd25519Ppk(Key key, string ppkPath)
    {
        var t = key.GetType();
        var algorithmName = key.ToString();
        if (string.IsNullOrEmpty(algorithmName) || (algorithmName != "ssh-ed25519" && algorithmName != "ssh-ed448"))
            return false;
        var publicKey = GetByteArray(t, key, "PublicKey", "_publicKey", "Public", "Key");
        var privateKey = GetByteArray(t, key, "PrivateKey", "_privateKey", "Private", "PrivateExponent");
        if (publicKey == null || publicKey.Length == 0 || privateKey == null || privateKey.Length == 0)
            return false;
        var publicBlob = BuildSshEd25519PublicBlob(algorithmName, publicKey);
        var privateExp = BytesToBigInteger(privateKey);
        var privateBlob = ToMpint(privateExp);
        const string comment = "converted-by-xOpenTerm";
        WritePpkFile(ppkPath, algorithmName, publicBlob, privateBlob, comment);
        return true;
    }

    /// <summary>输出 PPK v2 格式（与 puttygen 及多数 PuTTY 兼容），MAC 为 HMAC-SHA1。</summary>
    private static void WritePpkFile(string ppkPath, string algorithmName, byte[] publicBlob, byte[] privateBlob, string comment)
    {
        const string encryptionType = "none";
        var mac = ComputePpkMacV2(privateBlob, publicBlob, comment, encryptionType, algorithmName);
        var publicB64 = Convert.ToBase64String(publicBlob);
        var privateB64 = Convert.ToBase64String(privateBlob);
        var sb = new StringBuilder();
        sb.Append("PuTTY-User-Key-File-2: ").Append(algorithmName).Append('\n');
        sb.Append("Encryption: ").Append(encryptionType).Append('\n');
        sb.Append("Comment: ").Append(comment).Append('\n');
        sb.Append("Public-Lines: ").Append((publicB64.Length + 63) / 64).Append('\n');
        for (var i = 0; i < publicB64.Length; i += 64)
            sb.Append(publicB64.Length - i >= 64 ? publicB64.AsSpan(i, 64) : publicB64.AsSpan(i)).Append('\n');
        sb.Append("Private-Lines: ").Append((privateB64.Length + 63) / 64).Append('\n');
        for (var i = 0; i < privateB64.Length; i += 64)
            sb.Append(privateB64.Length - i >= 64 ? privateB64.AsSpan(i, 64) : privateB64.AsSpan(i)).Append('\n');
        sb.Append("Private-MAC: ").Append(BitConverter.ToString(mac).Replace("-", "").ToLowerInvariant()).Append('\n');
        var dir = Path.GetDirectoryName(ppkPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(ppkPath, sb.ToString(), Encoding.ASCII);
    }

    /// <summary>PPK v2 MAC: HMAC-SHA1(key, preimage)。key = SHA1(passphrase + "putty-private-key-file-mac-key")，无加密时 passphrase 为空。</summary>
    private static byte[] ComputePpkMacV2(byte[] privateBlob, byte[] publicBlob, string comment, string encryptionType, string algorithmName)
    {
        var macKeyData = Encoding.ASCII.GetBytes("putty-private-key-file-mac-key");
        var macKey = SHA1.HashData(macKeyData);
        var preimage = BuildMacPreimage(privateBlob, publicBlob, comment, encryptionType, algorithmName);
        using var hmac = new HMACSHA1(macKey);
        return hmac.ComputeHash(preimage);
    }

    /// <summary>PPK v2 MAC preimage 顺序与文件头一致：algo, enc, comment, public, private（与附录 C 文字描述不同，以实际验证为准）。</summary>
    private static byte[] BuildMacPreimage(byte[] privateBlob, byte[] publicBlob, string comment, string encryptionType, string algorithmName)
    {
        using var ms = new MemoryStream();
        WriteString(ms, Encoding.ASCII.GetBytes(algorithmName));
        WriteString(ms, Encoding.ASCII.GetBytes(encryptionType));
        WriteString(ms, Encoding.UTF8.GetBytes(comment));
        WriteString(ms, publicBlob);
        WriteString(ms, privateBlob);
        return ms.ToArray();
    }

    private static BigInteger BytesToBigInteger(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return BigInteger.Zero;
        var needReverse = BitConverter.IsLittleEndian;
        var b = (byte[])bytes.Clone();
        if (needReverse) Array.Reverse(b);
        return new BigInteger(b, isUnsigned: true);
    }

    private static byte[]? GetByteArray(Type type, object instance, params string[] names)
    {
        foreach (var name in names)
        {
            var f = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null && f.FieldType == typeof(byte[]))
            {
                var v = f.GetValue(instance) as byte[];
                if (v != null) return v;
            }
            var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null && p.PropertyType == typeof(byte[]) && p.CanRead)
            {
                var v = p.GetValue(instance) as byte[];
                if (v != null) return v;
            }
        }
        return null;
    }

    private static string? GetString(Type type, object instance, params string[] names)
    {
        foreach (var name in names)
        {
            var f = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null && f.FieldType == typeof(string))
            {
                var v = f.GetValue(instance) as string;
                if (!string.IsNullOrEmpty(v)) return v;
            }
            var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null && p.PropertyType == typeof(string) && p.CanRead)
            {
                var v = p.GetValue(instance) as string;
                if (!string.IsNullOrEmpty(v)) return v;
            }
        }
        return null;
    }

    private static BigInteger? GetBigInt(Type type, object instance, params string[] names)
    {
        var bigIntType = Type.GetType("System.Numerics.BigInteger, System.Runtime.Numerics");
        if (bigIntType == null) return null;

        foreach (var name in names)
        {
            var f = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null && f.FieldType == bigIntType)
            {
                var v = f.GetValue(instance);
                if (v != null) return (BigInteger)v;
            }
            var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null && p.PropertyType == bigIntType && p.CanRead)
            {
                var v = p.GetValue(instance);
                if (v != null) return (BigInteger)v;
            }
        }
        return null;
    }

    private static byte[] BuildSshRsaPublicBlob(BigInteger e, BigInteger n)
    {
        var algo = Encoding.ASCII.GetBytes("ssh-rsa");
        var eBytes = ToMpint(e);
        var nBytes = ToMpint(n);
        using var ms = new MemoryStream();
        WriteUInt32(ms, (uint)algo.Length);
        ms.Write(algo, 0, algo.Length);
        ms.Write(eBytes, 0, eBytes.Length);
        ms.Write(nBytes, 0, nBytes.Length);
        return ms.ToArray();
    }

    /// <summary>PPK RSA private 顺序：d, p, q, iqmp（与 PuTTY/puttygen 实际写入一致）。</summary>
    private static byte[] BuildPpkRsaPrivateBlob(BigInteger d, BigInteger p, BigInteger q, BigInteger iqmp)
    {
        using var ms = new MemoryStream();
        foreach (var mpint in new[] { ToMpint(d), ToMpint(p), ToMpint(q), ToMpint(iqmp) })
            ms.Write(mpint, 0, mpint.Length);
        return ms.ToArray();
    }

    /// <summary>SSH wire: "ssh-dss", mpint p, q, g, y.</summary>
    private static byte[] BuildSshDssPublicBlob(BigInteger p, BigInteger q, BigInteger g, BigInteger y)
    {
        var algo = Encoding.ASCII.GetBytes("ssh-dss");
        using var ms = new MemoryStream();
        WriteUInt32(ms, (uint)algo.Length);
        ms.Write(algo, 0, algo.Length);
        foreach (var mpint in new[] { ToMpint(p), ToMpint(q), ToMpint(g), ToMpint(y) })
            ms.Write(mpint, 0, mpint.Length);
        return ms.ToArray();
    }

    /// <summary>SSH wire: string algo, string curve, string Q.</summary>
    private static byte[] BuildSshEcdsaPublicBlob(string algorithmName, byte[] curveName, byte[] publicPoint)
    {
        var algo = Encoding.ASCII.GetBytes(algorithmName);
        using var ms = new MemoryStream();
        WriteUInt32(ms, (uint)algo.Length);
        ms.Write(algo, 0, algo.Length);
        WriteUInt32(ms, (uint)curveName.Length);
        ms.Write(curveName, 0, curveName.Length);
        WriteUInt32(ms, (uint)publicPoint.Length);
        ms.Write(publicPoint, 0, publicPoint.Length);
        return ms.ToArray();
    }

    /// <summary>SSH wire: string algo, string public_key.</summary>
    private static byte[] BuildSshEd25519PublicBlob(string algorithmName, byte[] publicKey)
    {
        var algo = Encoding.ASCII.GetBytes(algorithmName);
        using var ms = new MemoryStream();
        WriteUInt32(ms, (uint)algo.Length);
        ms.Write(algo, 0, algo.Length);
        WriteUInt32(ms, (uint)publicKey.Length);
        ms.Write(publicKey, 0, publicKey.Length);
        return ms.ToArray();
    }

    /// <summary>SSH mpint：4 字节长度 + 大端字节；正数首字节高位须为 0，否则前插 0x00。</summary>
    private static byte[] ToMpint(BigInteger n)
    {
        var bytes = n.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (bytes.Length == 0) bytes = Array.Empty<byte>();
        else if ((bytes[0] & 0x80) != 0)
        {
            var withZero = new byte[bytes.Length + 1];
            withZero[0] = 0;
            Buffer.BlockCopy(bytes, 0, withZero, 1, bytes.Length);
            bytes = withZero;
        }
        using var ms = new MemoryStream();
        WriteUInt32(ms, (uint)bytes.Length);
        if (bytes.Length > 0) ms.Write(bytes, 0, bytes.Length);
        return ms.ToArray();
    }

    private static void WriteUInt32(Stream s, uint value)
    {
        var b = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        s.Write(b, 0, 4);
    }

    private static void WriteString(Stream s, byte[] value)
    {
        WriteUInt32(s, (uint)value.Length);
        s.Write(value, 0, value.Length);
    }
}
