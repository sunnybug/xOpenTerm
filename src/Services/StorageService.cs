using System.IO;
using System.Windows;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm.Services;

/// <summary>节点、凭证、隧道的 YAML 持久化</summary>
public class StorageService
{
    /// <summary>解析配置目录：先尝试 .run/config，不存在则使用工作目录下的 config，最后使用 exe 所在目录下的 config。</summary>
    public static string GetConfigDir()
    {
        var workConfig = Path.Combine(Environment.CurrentDirectory, "config");
        var exeConfig = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),
            "config");
        var runConfig = Path.Combine(Environment.CurrentDirectory, ".run", "config");

        // 写入调试信息到日志文件
        ExceptionLog.Debug("解析配置目录开始");
        ExceptionLog.Debug($"当前工作目录: {Environment.CurrentDirectory}");
        ExceptionLog.Debug($"可执行文件目录: {AppDomain.CurrentDomain.BaseDirectory}");
        ExceptionLog.Debug($"检查配置目录 - Run目录: {runConfig} (存在: {Directory.Exists(runConfig)})");
        ExceptionLog.Debug($"检查配置目录 - 工作目录: {workConfig} (存在: {Directory.Exists(workConfig)})");
        ExceptionLog.Debug($"检查配置目录 - 可执行文件目录: {exeConfig} (存在: {Directory.Exists(exeConfig)})");

        // 优先检查 .run/config 目录
        if (Directory.Exists(runConfig))
        {
            ExceptionLog.Debug($"使用配置目录: {runConfig}");
            return runConfig;
        }
        if (Directory.Exists(workConfig))
        {
            ExceptionLog.Debug($"使用配置目录: {workConfig}");
            return workConfig;
        }
        if (Directory.Exists(exeConfig))
        {
            ExceptionLog.Debug($"使用配置目录: {exeConfig}");
            return exeConfig;
        }
        ExceptionLog.Debug($"未找到配置目录，使用工作目录下的config: {workConfig}");
        return workConfig;
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
        // 写入调试信息到日志文件
        ExceptionLog.Debug("加载节点开始");
        ExceptionLog.Debug($"节点文件路径: {_nodesPath}");
        ExceptionLog.Debug($"文件是否存在: {File.Exists(_nodesPath)}");
        if (File.Exists(_nodesPath))
        {
            try
            {
                var fileInfo = new FileInfo(_nodesPath);
                ExceptionLog.Debug($"文件大小: {fileInfo.Length} bytes");
            }
            catch (Exception ex)
            {
                ExceptionLog.Debug($"获取文件信息失败: {ex.Message}");
            }
        }

        if (!File.Exists(_nodesPath))
        {
            ExceptionLog.Debug("文件不存在，返回空列表");
            return new List<Node>();
        }

        try
        {
            var yaml = File.ReadAllText(_nodesPath);
            ExceptionLog.Debug($"读取文件成功，内容长度: {yaml.Length} bytes");
            var list = TryLoadNodesFile(yaml);
            DecryptNodes(list);
            ExceptionLog.Debug($"加载节点成功，节点数量: {list.Count}");
            return list;
        }
        catch (Exception ex)
        {
            ExceptionLog.Error($"加载节点失败: {ex.Message}");
            ExceptionLog.Error($"异常堆栈: {ex.StackTrace}");
            System.Diagnostics.Debug.WriteLine($"[xOpenTerm] 加载节点失败: {ex.Message}");
            ExceptionLog.Write(ex, "加载节点配置文件失败");
            System.Windows.MessageBox.Show(
                "加载节点配置文件失败，详情已写入日志。\n\n" + ex.Message +
                "\n\n日志目录：\n" + ExceptionLog.LogDirectory,
                "xOpenTerm",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
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
            var logPath = Path.Combine(Environment.CurrentDirectory, ".run", "log", "xOpenTerm_debug.log");
            var logDir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(logDir))
            {
                Directory.CreateDirectory(logDir);
            }
            try
            {
                using (var writer = new StreamWriter(logPath, true))
                {
                    writer.WriteLine($"[{DateTime.Now}] 尝试解析为 NodesFile 类型");
                }
                var wrapper = _deserializer.Deserialize<NodesFile>(yaml);
                if (wrapper?.Nodes != null)
                {
                    using (var writer = new StreamWriter(logPath, true))
                    {
                        writer.WriteLine($"[{DateTime.Now}] 解析为 NodesFile 类型成功，节点数量: {wrapper.Nodes.Count}");
                    }
                    return wrapper.Nodes;
                }
                else
                {
                    using (var writer = new StreamWriter(logPath, true))
                    {
                        writer.WriteLine($"[{DateTime.Now}] 解析为 NodesFile 类型成功，但 wrapper 或 Nodes 为 null");
                    }
                }
            }
            catch (Exception ex)
            {
                using (var writer = new StreamWriter(logPath, true))
                {
                    writer.WriteLine($"[{DateTime.Now}] 解析为 NodesFile 类型失败: {ex.Message}");
                    writer.WriteLine($"[{DateTime.Now}] 异常堆栈: {ex.StackTrace}");
                    if (ex.InnerException != null)
                    {
                        writer.WriteLine($"[{DateTime.Now}] 内部异常: {ex.InnerException.Message}");
                        writer.WriteLine($"[{DateTime.Now}] 内部异常堆栈: {ex.InnerException.StackTrace}");
                    }
                }
                // 旧格式：根节点为列表
            }

            try
            {
                using (var writer = new StreamWriter(logPath, true))
                {
                    writer.WriteLine($"[{DateTime.Now}] 尝试解析为 List<Node> 类型");
                }
                var list = _deserializer.Deserialize<List<Node>>(yaml);
                using (var writer = new StreamWriter(logPath, true))
                {
                    writer.WriteLine($"[{DateTime.Now}] 解析为 List<Node> 类型成功，节点数量: {list?.Count ?? 0}");
                }
                return list ?? new List<Node>();
            }
            catch (Exception ex)
            {
                using (var writer = new StreamWriter(logPath, true))
                {
                    writer.WriteLine($"[{DateTime.Now}] 解析为 List<Node> 类型失败: {ex.Message}");
                    writer.WriteLine($"[{DateTime.Now}] 异常堆栈: {ex.StackTrace}");
                    if (ex.InnerException != null)
                    {
                        writer.WriteLine($"[{DateTime.Now}] 内部异常: {ex.InnerException.Message}");
                        writer.WriteLine($"[{DateTime.Now}] 内部异常堆栈: {ex.InnerException.StackTrace}");
                    }
                }
                return new List<Node>();
            }
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
            ExceptionLog.Write(ex, "加载凭证配置文件失败");
            System.Windows.MessageBox.Show(
                "加载凭证配置文件失败，详情已写入日志。\n\n" + ex.Message +
                "\n\n日志目录：\n" + ExceptionLog.LogDirectory,
                "xOpenTerm",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
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
            ExceptionLog.Write(ex, "加载隧道配置文件失败");
            System.Windows.MessageBox.Show(
                "加载隧道配置文件失败，详情已写入日志。\n\n" + ex.Message +
                "\n\n日志目录：\n" + ExceptionLog.LogDirectory,
                "xOpenTerm",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
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
            ExceptionLog.Write(ex, "加载应用设置配置文件失败");
            System.Windows.MessageBox.Show(
                "加载应用设置配置文件失败，详情已写入日志。\n\n" + ex.Message +
                "\n\n日志目录：\n" + ExceptionLog.LogDirectory,
                "xOpenTerm",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            return new AppSettings();
        }
    }

    public void SaveAppSettings(AppSettings settings)
    {
        var yaml = _serializer.Serialize(settings);
        File.WriteAllText(_settingsPath, yaml);
        ConfigBackupService.BackupIfNeeded();
    }

    /// <summary>将应用设置序列化为 YAML 字符串（用于备份等）。</summary>
    public string SerializeAppSettings(AppSettings settings)
    {
        return _serializer.Serialize(settings);
    }

    /// <summary>返回不包含主密码相关字段的设置 YAML，供配置备份使用。</summary>
    public string SerializeAppSettingsForBackup()
    {
        var settings = LoadAppSettings();
        settings.MasterPasswordAsked = false;
        settings.MasterPasswordSalt = null;
        settings.MasterPasswordVerifier = null;
        settings.MasterPasswordSkipped = false;
        return _serializer.Serialize(settings);
    }

    /// <summary>将导出数据序列化为 YAML 字符串（节点与凭证均为解密后的明文，便于迁移与备份）。</summary>
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
        c.KsyunAccessKeyId = SecretService.Decrypt(c.KsyunAccessKeyId);
        c.KsyunAccessKeySecret = SecretService.Decrypt(c.KsyunAccessKeySecret);
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
        c.KsyunAccessKeyId = SecretService.Encrypt(c.KsyunAccessKeyId, configVersion);
        c.KsyunAccessKeySecret = SecretService.Encrypt(c.KsyunAccessKeySecret, configVersion);
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
