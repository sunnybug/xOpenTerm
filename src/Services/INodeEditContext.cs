using xOpenTerm.Models;

namespace xOpenTerm.Services;

/// <summary>节点/凭证/隧道编辑上下文：提供当前列表的读写与持久化，便于编辑窗口统一入参并便于测试。</summary>
public interface INodeEditContext
{
    IList<Node> Nodes { get; }
    IList<Credential> Credentials { get; }
    IList<Tunnel> Tunnels { get; }
    void SaveNodes();
    void SaveCredentials();
    void SaveTunnels();
    /// <summary>从存储重新加载隧道列表到 Tunnels（如 TunnelManager 关闭后刷新）。</summary>
    void ReloadTunnels();
    /// <summary>从存储重新加载凭证列表到 Credentials。</summary>
    void ReloadCredentials();
}
