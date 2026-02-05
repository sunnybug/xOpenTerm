using YamlDotNet.Serialization;

namespace xOpenTerm.Models;

/// <summary>登录凭证：可被多个节点复用的认证配置</summary>
public class Credential
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = "";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "username")]
    public string Username { get; set; } = "";

    [YamlMember(Alias = "authType")]
    public AuthType AuthType { get; set; }

    [YamlMember(Alias = "password")]
    public string? Password { get; set; }

    [YamlMember(Alias = "keyPath")]
    public string? KeyPath { get; set; }

    [YamlMember(Alias = "keyPassphrase")]
    public string? KeyPassphrase { get; set; }

    [YamlMember(Alias = "agentForwarding")]
    public bool? AgentForwarding { get; set; }

    [YamlMember(Alias = "tunnel")]
    public List<TunnelHop>? Tunnel { get; set; }
}
