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
