using YamlDotNet.Serialization;

namespace xOpenTerm.Models;

/// <summary>单跳隧道配置（ProxyJump 的一跳）</summary>
public class TunnelHop
{
    [YamlMember(Alias = "host")]
    public string Host { get; set; } = "";

    [YamlMember(Alias = "port")]
    public ushort? Port { get; set; }

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

    [YamlMember(Alias = "credentialId")]
    public string? CredentialId { get; set; }
}
