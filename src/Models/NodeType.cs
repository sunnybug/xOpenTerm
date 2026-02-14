namespace xOpenTerm.Models;

public enum NodeType
{
    group,
    /// <summary>腾讯云组：根节点存密钥，子节点为 机房→项目→服务器，支持同步。</summary>
    tencentCloudGroup,
    /// <summary>阿里云组：根节点存密钥，子节点为 地域→服务器，支持同步。</summary>
    aliCloudGroup,
    /// <summary>金山云组：根节点存密钥，子节点为 地域→服务器，支持同步。</summary>
    kingsoftCloudGroup,
    ssh,
    rdp
}
