using YamlDotNet.Serialization;

namespace xOpenTerm.Models;

/// <summary>节点配置文件根结构，带版本号以支持按版本切换加密算法与密钥。</summary>
public class NodesFile
{
    [YamlMember(Alias = "version")]
    public int Version { get; set; } = 1;

    [YamlMember(Alias = "nodes")]
    public List<Node> Nodes { get; set; } = new();
}

/// <summary>凭证配置文件根结构。</summary>
public class CredentialsFile
{
    [YamlMember(Alias = "version")]
    public int Version { get; set; } = 1;

    [YamlMember(Alias = "credentials")]
    public List<Credential> Credentials { get; set; } = new();
}

/// <summary>隧道配置文件根结构。</summary>
public class TunnelsFile
{
    [YamlMember(Alias = "version")]
    public int Version { get; set; } = 1;

    [YamlMember(Alias = "tunnels")]
    public List<Tunnel> Tunnels { get; set; } = new();
}

/// <summary>导出 YAML 根结构：节点树 + 被引用的登录凭证（明文，便于迁移）。</summary>
public class ExportYamlRoot
{
    [YamlMember(Alias = "version")]
    public int Version { get; set; } = 1;

    [YamlMember(Alias = "nodes")]
    public List<Node> Nodes { get; set; } = new();

    [YamlMember(Alias = "credentials")]
    public List<Credential> Credentials { get; set; } = new();
}
