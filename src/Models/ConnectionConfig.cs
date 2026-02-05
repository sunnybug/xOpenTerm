using YamlDotNet.Serialization;

namespace xOpenTerm.Models;

public class ConnectionConfig
{
    [YamlMember(Alias = "host")]
    public string? Host { get; set; }

    [YamlMember(Alias = "port")]
    public ushort? Port { get; set; }

    [YamlMember(Alias = "username")]
    public string? Username { get; set; }

    [YamlMember(Alias = "authType")]
    public AuthType? AuthType { get; set; }

    [YamlMember(Alias = "password")]
    public string? Password { get; set; }

    [YamlMember(Alias = "keyPath")]
    public string? KeyPath { get; set; }

    [YamlMember(Alias = "keyPassphrase")]
    public string? KeyPassphrase { get; set; }

    [YamlMember(Alias = "protocol")]
    public Protocol? Protocol { get; set; }

    [YamlMember(Alias = "agentForwarding")]
    public bool? AgentForwarding { get; set; }

    [YamlMember(Alias = "authSource")]
    public AuthSource? AuthSource { get; set; }

    [YamlMember(Alias = "credentialId")]
    public string? CredentialId { get; set; }

    [YamlMember(Alias = "tunnelIds")]
    public List<string>? TunnelIds { get; set; }

    [YamlMember(Alias = "tunnelId")]
    public string? TunnelId { get; set; }

    [YamlMember(Alias = "tunnel")]
    public List<TunnelHop>? Tunnel { get; set; }

    [YamlMember(Alias = "domain")]
    public string? Domain { get; set; }
}
