using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using xOpenTerm.Models;

namespace xOpenTerm.Services;

/// <summary>节点、凭证、隧道的 YAML 持久化</summary>
public class StorageService
{
    /// <summary>解析配置目录：先尝试工作目录下的 config，不存在则使用 exe 所在目录下的 config。</summary>
    public static string GetConfigDir()
    {
        var workConfig = Path.Combine(Environment.CurrentDirectory, "config");
        var exeConfig = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),
            "config");
        return Directory.Exists(workConfig) ? workConfig : exeConfig;
    }

    private const string NodesFile = "nodes.yaml";
    private const string CredentialsFile = "credentials.yaml";
    private const string TunnelsFile = "tunnels.yaml";
    private const string SettingsFile = "settings.yaml";

    private readonly string _nodesPath;
    private readonly string _credentialsPath;
    private readonly string _tunnelsPath;
    private readonly string _settingsPath;
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    public StorageService()
    {
        var configDir = GetConfigDir();
        Directory.CreateDirectory(configDir);
        _nodesPath = Path.Combine(configDir, NodesFile);
        _credentialsPath = Path.Combine(configDir, CredentialsFile);
        _tunnelsPath = Path.Combine(configDir, TunnelsFile);
        _settingsPath = Path.Combine(configDir, SettingsFile);

        _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public List<Node> LoadNodes()
    {
        if (!File.Exists(_nodesPath)) return new List<Node>();
        try
        {
            var yaml = File.ReadAllText(_nodesPath);
            var list = TryLoadNodesFile(yaml);
            DecryptNodes(list);
            return list;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[xOpenTerm] 加载节点失败: {ex.Message}");
            return new List<Node>();
        }
    }

    public void SaveNodes(IEnumerable<Node> nodes)
    {
        var list = nodes.ToList();
        EncryptNodes(list, SecretService.CurrentConfigVersion);
        try
        {
            var wrapper = new NodesFile { Version = SecretService.CurrentConfigVersion, Nodes = list };
            var yaml = _serializer.Serialize(wrapper);
            File.WriteAllText(_nodesPath, yaml);
            ConfigBackupService.BackupIfNeeded();
        }
        finally
        {
            DecryptNodes(list);
        }
    }

    private List<Node> TryLoadNodesFile(string yaml)
    {
        try
        {
            var wrapper = _deserializer.Deserialize<NodesFile>(yaml);
            if (wrapper?.Nodes != null)
                return wrapper.Nodes;
        }
        catch
        {
            // 旧格式：根节点为列表
        }
        var list = _deserializer.Deserialize<List<Node>>(yaml);
        return list ?? new List<Node>();
    }

    public List<Credential> LoadCredentials()
    {
        if (!File.Exists(_credentialsPath)) return new List<Credential>();
        try
        {
            var yaml = File.ReadAllText(_credentialsPath);
            var list = TryLoadCredentialsFile(yaml);
            DecryptCredentials(list);
            return list;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[xOpenTerm] 加载凭证失败: {ex.Message}");
            return new List<Credential>();
        }
    }

    public void SaveCredentials(IEnumerable<Credential> credentials)
    {
        var list = credentials.ToList();
        EncryptCredentials(list, SecretService.CurrentConfigVersion);
        try
        {
            var wrapper = new CredentialsFile { Version = SecretService.CurrentConfigVersion, Credentials = list };
            var yaml = _serializer.Serialize(wrapper);
            File.WriteAllText(_credentialsPath, yaml);
            ConfigBackupService.BackupIfNeeded();
        }
        finally
        {
            DecryptCredentials(list);
        }
    }

    private List<Credential> TryLoadCredentialsFile(string yaml)
    {
        try
        {
            var wrapper = _deserializer.Deserialize<CredentialsFile>(yaml);
            if (wrapper?.Credentials != null)
                return wrapper.Credentials;
        }
        catch
        {
            // 旧格式：根节点为列表
        }
        var list = _deserializer.Deserialize<List<Credential>>(yaml);
        return list ?? new List<Credential>();
    }

    public List<Tunnel> LoadTunnels()
    {
        if (!File.Exists(_tunnelsPath)) return new List<Tunnel>();
        try
        {
            var yaml = File.ReadAllText(_tunnelsPath);
            var list = TryLoadTunnelsFile(yaml);
            DecryptTunnels(list);
            return list;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[xOpenTerm] 加载隧道失败: {ex.Message}");
            return new List<Tunnel>();
        }
    }

    public void SaveTunnels(IEnumerable<Tunnel> tunnels)
    {
        var list = tunnels.ToList();
        EncryptTunnels(list, SecretService.CurrentConfigVersion);
        try
        {
            var wrapper = new TunnelsFile { Version = SecretService.CurrentConfigVersion, Tunnels = list };
            var yaml = _serializer.Serialize(wrapper);
            File.WriteAllText(_tunnelsPath, yaml);
            ConfigBackupService.BackupIfNeeded();
        }
        finally
        {
            DecryptTunnels(list);
        }
    }

    private List<Tunnel> TryLoadTunnelsFile(string yaml)
    {
        try
        {
            var wrapper = _deserializer.Deserialize<TunnelsFile>(yaml);
            if (wrapper?.Tunnels != null)
                return wrapper.Tunnels;
        }
        catch
        {
            // 旧格式：根节点为列表
        }
        var list = _deserializer.Deserialize<List<Tunnel>>(yaml);
        return list ?? new List<Tunnel>();
    }

    public AppSettings LoadAppSettings()
    {
        if (!File.Exists(_settingsPath)) return new AppSettings();
        try
        {
            var yaml = File.ReadAllText(_settingsPath);
            var settings = _deserializer.Deserialize<AppSettings>(yaml);
            return settings ?? new AppSettings();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[xOpenTerm] 加载设置失败: {ex.Message}");
            return new AppSettings();
        }
    }

    public void SaveAppSettings(AppSettings settings)
    {
        var yaml = _serializer.Serialize(settings);
        File.WriteAllText(_settingsPath, yaml);
        ConfigBackupService.BackupIfNeeded();
    }

    /// <summary>将导出数据序列化为 YAML 字符串（节点与凭证均为明文）。</summary>
    public string SerializeExport(ExportYamlRoot data)
    {
        return _serializer.Serialize(data);
    }

    /// <summary>从 YAML 字符串反序列化导出数据。</summary>
    public ExportYamlRoot? DeserializeExport(string yaml)
    {
        return _deserializer.Deserialize<ExportYamlRoot>(yaml);
    }

    private static void DecryptNodes(List<Node> list)
    {
        foreach (var node in list)
        {
            if (node.Config != null)
                DecryptConnectionConfig(node.Config);
        }
    }

    private static void EncryptNodes(List<Node> list, int configVersion)
    {
        foreach (var node in list)
        {
            if (node.Config != null)
                EncryptConnectionConfig(node.Config, configVersion);
        }
    }

    private static void DecryptConnectionConfig(ConnectionConfig c)
    {
        c.Password = SecretService.Decrypt(c.Password);
        c.KeyPassphrase = SecretService.Decrypt(c.KeyPassphrase);
        c.TencentSecretId = SecretService.Decrypt(c.TencentSecretId);
        c.TencentSecretKey = SecretService.Decrypt(c.TencentSecretKey);
        c.AliAccessKeyId = SecretService.Decrypt(c.AliAccessKeyId);
        c.AliAccessKeySecret = SecretService.Decrypt(c.AliAccessKeySecret);
        if (c.Tunnel != null)
        {
            foreach (var hop in c.Tunnel)
                DecryptTunnelHop(hop);
        }
    }

    private static void EncryptConnectionConfig(ConnectionConfig c, int configVersion)
    {
        c.Password = SecretService.Encrypt(c.Password, configVersion);
        c.KeyPassphrase = SecretService.Encrypt(c.KeyPassphrase, configVersion);
        c.TencentSecretId = SecretService.Encrypt(c.TencentSecretId, configVersion);
        c.TencentSecretKey = SecretService.Encrypt(c.TencentSecretKey, configVersion);
        c.AliAccessKeyId = SecretService.Encrypt(c.AliAccessKeyId, configVersion);
        c.AliAccessKeySecret = SecretService.Encrypt(c.AliAccessKeySecret, configVersion);
        if (c.Tunnel != null)
        {
            foreach (var hop in c.Tunnel)
                EncryptTunnelHop(hop, configVersion);
        }
    }

    private static void DecryptTunnelHop(TunnelHop h)
    {
        h.Password = SecretService.Decrypt(h.Password);
        h.KeyPassphrase = SecretService.Decrypt(h.KeyPassphrase);
    }

    private static void EncryptTunnelHop(TunnelHop h, int configVersion)
    {
        h.Password = SecretService.Encrypt(h.Password, configVersion);
        h.KeyPassphrase = SecretService.Encrypt(h.KeyPassphrase, configVersion);
    }

    private static void DecryptCredentials(List<Credential> list)
    {
        foreach (var cred in list)
        {
            cred.Password = SecretService.Decrypt(cred.Password);
            cred.KeyPassphrase = SecretService.Decrypt(cred.KeyPassphrase);
            if (cred.Tunnel != null)
            {
                foreach (var hop in cred.Tunnel)
                    DecryptTunnelHop(hop);
            }
        }
    }

    private static void EncryptCredentials(List<Credential> list, int configVersion)
    {
        foreach (var cred in list)
        {
            cred.Password = SecretService.Encrypt(cred.Password, configVersion);
            cred.KeyPassphrase = SecretService.Encrypt(cred.KeyPassphrase, configVersion);
            if (cred.Tunnel != null)
            {
                foreach (var hop in cred.Tunnel)
                    EncryptTunnelHop(hop, configVersion);
            }
        }
    }

    private static void DecryptTunnels(List<Tunnel> list)
    {
        foreach (var t in list)
        {
            t.Password = SecretService.Decrypt(t.Password);
            t.KeyPassphrase = SecretService.Decrypt(t.KeyPassphrase);
        }
    }

    private static void EncryptTunnels(List<Tunnel> list, int configVersion)
    {
        foreach (var t in list)
        {
            t.Password = SecretService.Encrypt(t.Password, configVersion);
            t.KeyPassphrase = SecretService.Encrypt(t.KeyPassphrase, configVersion);
        }
    }
}
