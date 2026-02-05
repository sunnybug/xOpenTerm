using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using xOpenTerm.Models;

namespace xOpenTerm.Services;

/// <summary>节点、凭证、隧道的 YAML 持久化</summary>
public class StorageService
{
    private static readonly string ConfigDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),
        "config");
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
        Directory.CreateDirectory(ConfigDir);
        _nodesPath = Path.Combine(ConfigDir, NodesFile);
        _credentialsPath = Path.Combine(ConfigDir, CredentialsFile);
        _tunnelsPath = Path.Combine(ConfigDir, TunnelsFile);
        _settingsPath = Path.Combine(ConfigDir, SettingsFile);

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
            var list = _deserializer.Deserialize<List<Node>>(yaml);
            var result = list ?? new List<Node>();
            DecryptNodes(result);
            return result;
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
        EncryptNodes(list);
        try
        {
            var yaml = _serializer.Serialize(list);
            File.WriteAllText(_nodesPath, yaml);
        }
        finally
        {
            DecryptNodes(list);
        }
    }

    public List<Credential> LoadCredentials()
    {
        if (!File.Exists(_credentialsPath)) return new List<Credential>();
        try
        {
            var yaml = File.ReadAllText(_credentialsPath);
            var list = _deserializer.Deserialize<List<Credential>>(yaml);
            var result = list ?? new List<Credential>();
            DecryptCredentials(result);
            return result;
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
        EncryptCredentials(list);
        try
        {
            var yaml = _serializer.Serialize(list);
            File.WriteAllText(_credentialsPath, yaml);
        }
        finally
        {
            DecryptCredentials(list);
        }
    }

    public List<Tunnel> LoadTunnels()
    {
        if (!File.Exists(_tunnelsPath)) return new List<Tunnel>();
        try
        {
            var yaml = File.ReadAllText(_tunnelsPath);
            var list = _deserializer.Deserialize<List<Tunnel>>(yaml);
            var result = list ?? new List<Tunnel>();
            DecryptTunnels(result);
            return result;
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
        EncryptTunnels(list);
        try
        {
            var yaml = _serializer.Serialize(list);
            File.WriteAllText(_tunnelsPath, yaml);
        }
        finally
        {
            DecryptTunnels(list);
        }
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
    }

    private static void DecryptNodes(List<Node> list)
    {
        foreach (var node in list)
        {
            if (node.Config != null)
                DecryptConnectionConfig(node.Config);
        }
    }

    private static void EncryptNodes(List<Node> list)
    {
        foreach (var node in list)
        {
            if (node.Config != null)
                EncryptConnectionConfig(node.Config);
        }
    }

    private static void DecryptConnectionConfig(ConnectionConfig c)
    {
        c.Password = SecretService.Decrypt(c.Password);
        c.KeyPassphrase = SecretService.Decrypt(c.KeyPassphrase);
        if (c.Tunnel != null)
        {
            foreach (var hop in c.Tunnel)
                DecryptTunnelHop(hop);
        }
    }

    private static void EncryptConnectionConfig(ConnectionConfig c)
    {
        c.Password = SecretService.Encrypt(c.Password);
        c.KeyPassphrase = SecretService.Encrypt(c.KeyPassphrase);
        if (c.Tunnel != null)
        {
            foreach (var hop in c.Tunnel)
                EncryptTunnelHop(hop);
        }
    }

    private static void DecryptTunnelHop(TunnelHop h)
    {
        h.Password = SecretService.Decrypt(h.Password);
        h.KeyPassphrase = SecretService.Decrypt(h.KeyPassphrase);
    }

    private static void EncryptTunnelHop(TunnelHop h)
    {
        h.Password = SecretService.Encrypt(h.Password);
        h.KeyPassphrase = SecretService.Encrypt(h.KeyPassphrase);
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

    private static void EncryptCredentials(List<Credential> list)
    {
        foreach (var cred in list)
        {
            cred.Password = SecretService.Encrypt(cred.Password);
            cred.KeyPassphrase = SecretService.Encrypt(cred.KeyPassphrase);
            if (cred.Tunnel != null)
            {
                foreach (var hop in cred.Tunnel)
                    EncryptTunnelHop(hop);
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

    private static void EncryptTunnels(List<Tunnel> list)
    {
        foreach (var t in list)
        {
            t.Password = SecretService.Encrypt(t.Password);
            t.KeyPassphrase = SecretService.Encrypt(t.KeyPassphrase);
        }
    }
}
