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

    /// <summary>分组节点：子节点选「同父节点」且为 SSH 时使用的默认凭证。</summary>
    [YamlMember(Alias = "sshCredentialId")]
    public string? SshCredentialId { get; set; }

    /// <summary>分组节点：子节点选「同父节点」且为 RDP 时使用的默认凭证。</summary>
    [YamlMember(Alias = "rdpCredentialId")]
    public string? RdpCredentialId { get; set; }

    [YamlMember(Alias = "tunnelSource")]
    public AuthSource? TunnelSource { get; set; }

    [YamlMember(Alias = "tunnelIds")]
    public List<string>? TunnelIds { get; set; }

    [YamlMember(Alias = "tunnelId")]
    public string? TunnelId { get; set; }

    [YamlMember(Alias = "tunnel")]
    public List<TunnelHop>? Tunnel { get; set; }

    [YamlMember(Alias = "domain")]
    public string? Domain { get; set; }

    #region RDP 扩展选项（参考 mRemoteNG）

    /// <summary>RDP：使用控制台会话（/admin）。</summary>
    [YamlMember(Alias = "rdpUseConsoleSession")]
    public bool? RdpUseConsoleSession { get; set; }

    /// <summary>RDP：重定向剪贴板。</summary>
    [YamlMember(Alias = "rdpRedirectClipboard")]
    public bool? RdpRedirectClipboard { get; set; }

    /// <summary>RDP：智能缩放（SmartSizing），随窗口缩放远程桌面。</summary>
    [YamlMember(Alias = "rdpSmartSizing")]
    public bool? RdpSmartSizing { get; set; }

    /// <summary>RD Gateway 主机名。</summary>
    [YamlMember(Alias = "rdpGatewayHostname")]
    public string? RdpGatewayHostname { get; set; }

    /// <summary>RD Gateway 使用方式：0=从不，1=始终，2=自动检测。</summary>
    [YamlMember(Alias = "rdpGatewayUsageMethod")]
    public int? RdpGatewayUsageMethod { get; set; }

    /// <summary>RD Gateway 是否使用连接凭据：0=否（单独填网关账号），1=是。</summary>
    [YamlMember(Alias = "rdpGatewayUseConnectionCredentials")]
    public int? RdpGatewayUseConnectionCredentials { get; set; }

    [YamlMember(Alias = "rdpGatewayUsername")]
    public string? RdpGatewayUsername { get; set; }

    /// <summary>RD Gateway 密码（加密存储）。</summary>
    [YamlMember(Alias = "rdpGatewayPassword")]
    public string? RdpGatewayPassword { get; set; }

    [YamlMember(Alias = "rdpGatewayDomain")]
    public string? RdpGatewayDomain { get; set; }

    #endregion

    /// <summary>腾讯云等云厂商的实例资源 ID，用于唯一标记服务器、做同步时比对。</summary>
    [YamlMember(Alias = "resourceId")]
    public string? ResourceId { get; set; }

    /// <summary>腾讯云组节点：API 密钥 SecretId（加密存储）。</summary>
    [YamlMember(Alias = "tencentSecretId")]
    public string? TencentSecretId { get; set; }

    /// <summary>腾讯云组节点：API 密钥 SecretKey（加密存储）。</summary>
    [YamlMember(Alias = "tencentSecretKey")]
    public string? TencentSecretKey { get; set; }

    /// <summary>阿里云组节点：AccessKey Id（加密存储）。</summary>
    [YamlMember(Alias = "aliAccessKeyId")]
    public string? AliAccessKeyId { get; set; }

    /// <summary>阿里云组节点：AccessKey Secret（加密存储）。</summary>
    [YamlMember(Alias = "aliAccessKeySecret")]
    public string? AliAccessKeySecret { get; set; }

    /// <summary>金山云组节点：AccessKey Id（加密存储）。</summary>
    [YamlMember(Alias = "ksyunAccessKeyId")]
    public string? KsyunAccessKeyId { get; set; }

    /// <summary>金山云组节点：AccessKey Secret（加密存储）。</summary>
    [YamlMember(Alias = "ksyunAccessKeySecret")]
    public string? KsyunAccessKeySecret { get; set; }
}
