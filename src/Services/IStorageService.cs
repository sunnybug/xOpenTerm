using xOpenTerm.Models;

namespace xOpenTerm.Services;

/// <summary>节点、凭证、隧道的 YAML 持久化抽象，便于测试与替换实现。</summary>
public interface IStorageService
{
    /// <summary>解析配置目录：优先使用工作目录下的 config，不存在则使用 exe 所在目录下的 config。</summary>
    string GetConfigDir();

    List<Node> LoadNodes();
    void SaveNodes(IEnumerable<Node> nodes);

    List<Credential> LoadCredentials();
    void SaveCredentials(IEnumerable<Credential> credentials);

    List<Tunnel> LoadTunnels();
    void SaveTunnels(IEnumerable<Tunnel> tunnels);

    AppSettings LoadAppSettings();
    void SaveAppSettings(AppSettings settings);

    string SerializeAppSettings(AppSettings settings);
    string SerializeAppSettingsForBackup();
    string SerializeExport(ExportYamlRoot data);
    ExportYamlRoot? DeserializeExport(string yaml);
}
