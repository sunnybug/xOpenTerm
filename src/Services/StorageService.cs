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
            return list ?? new List<Node>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[xOpenTerm] 加载节点失败: {ex.Message}");
            return new List<Node>();
        }
    }

    public void SaveNodes(IEnumerable<Node> nodes)
    {
        var yaml = _serializer.Serialize(nodes.ToList());
        File.WriteAllText(_nodesPath, yaml);
    }

    public List<Credential> LoadCredentials()
    {
        if (!File.Exists(_credentialsPath)) return new List<Credential>();
        try
        {
            var yaml = File.ReadAllText(_credentialsPath);
            var list = _deserializer.Deserialize<List<Credential>>(yaml);
            return list ?? new List<Credential>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[xOpenTerm] 加载凭证失败: {ex.Message}");
            return new List<Credential>();
        }
    }

    public void SaveCredentials(IEnumerable<Credential> credentials)
    {
        var yaml = _serializer.Serialize(credentials.ToList());
        File.WriteAllText(_credentialsPath, yaml);
    }

    public List<Tunnel> LoadTunnels()
    {
        if (!File.Exists(_tunnelsPath)) return new List<Tunnel>();
        try
        {
            var yaml = File.ReadAllText(_tunnelsPath);
            var list = _deserializer.Deserialize<List<Tunnel>>(yaml);
            return list ?? new List<Tunnel>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[xOpenTerm] 加载隧道失败: {ex.Message}");
            return new List<Tunnel>();
        }
    }

    public void SaveTunnels(IEnumerable<Tunnel> tunnels)
    {
        var yaml = _serializer.Serialize(tunnels.ToList());
        File.WriteAllText(_tunnelsPath, yaml);
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
}
