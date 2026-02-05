using YamlDotNet.Serialization;

namespace xOpenTerm.Models;

/// <summary>SSH 隧道（单跳跳板机）</summary>
public class Tunnel
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = "";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

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
