using xOpenTerm.Models;

namespace xOpenTerm.Services;

/// <summary>INodeEditContext 的默认实现：持有节点/凭证/隧道列表与 IStorageService，保存时写回存储。</summary>
public sealed class NodeEditContext : INodeEditContext
{
    public IList<Node> Nodes { get; }
    public IList<Credential> Credentials { get; }
    public IList<Tunnel> Tunnels { get; }
    private readonly IStorageService _storage;

    public NodeEditContext(IList<Node> nodes, IList<Credential> credentials, IList<Tunnel> tunnels, IStorageService storage)
    {
        Nodes = nodes;
        Credentials = credentials;
        Tunnels = tunnels;
        _storage = storage;
    }

    public void SaveNodes() => _storage.SaveNodes(Nodes);
    public void SaveCredentials() => _storage.SaveCredentials(Credentials);
    public void SaveTunnels() => _storage.SaveTunnels(Tunnels);

    public void ReloadTunnels()
    {
        Tunnels.Clear();
        foreach (var t in _storage.LoadTunnels())
            Tunnels.Add(t);
    }

    public void ReloadCredentials()
    {
        Credentials.Clear();
        foreach (var c in _storage.LoadCredentials())
            Credentials.Add(c);
    }
}
