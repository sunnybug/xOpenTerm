using Renci.SshNet;
using xOpenTerm.Models;

namespace xOpenTerm.Services;

/// <summary>按节点复用的 SFTP 长连接池。同一 nodeId 下切换目录只发 SFTP 请求，不重复建连；该节点所有 tab 关闭时需调用 ClearSession。</summary>
public static class SftpSessionManager
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, HeldSession> Sessions = new();

    /// <summary>持有 SFTP 客户端及跳板链资源，析构时一并释放。</summary>
    private sealed class HeldSession : IDisposable
    {
        internal SftpClient Sftp { get; }
        private readonly List<IDisposable>? _chainDisposables;

        internal HeldSession(SftpClient sftp, List<IDisposable>? chainDisposables)
        {
            Sftp = sftp;
            _chainDisposables = chainDisposables;
        }

        public void Dispose()
        {
            try { Sftp?.Dispose(); } catch { }
            if (_chainDisposables != null)
            {
                for (var i = _chainDisposables.Count - 1; i >= 0; i--)
                {
                    try { _chainDisposables[i].Dispose(); } catch { }
                }
            }
        }
    }

    /// <summary>构建到目标节点的 SSH 连接信息（含可选跳板链）。成功时返回 (ConnectionInfo, 跳板链资源)；失败时 error 非空且返回 (null, null)。</summary>
    private static (ConnectionInfo? connectionInfo, List<IDisposable>? chainDisposables) BuildConnection(
        Node node, IList<Node> allNodes, IList<Credential> credentials, IList<Tunnel> tunnels, out string? error)
    {
        error = null;
        try
        {
            var (host, port, username, password, keyPath, keyPassphrase, jumpChain, useAgent) =
                ConfigResolver.ResolveSsh(node, allNodes, credentials, tunnels);

            string connectHost = host;
            ushort connectPort = port;
            List<IDisposable>? chainDisposables = null;

            if (jumpChain != null && jumpChain.Count > 0)
            {
                chainDisposables = new List<IDisposable>();
                connectHost = jumpChain[0].Host;
                connectPort = jumpChain[0].Port;

                for (var i = 0; i < jumpChain.Count; i++)
                {
                    var hop = jumpChain[i];
                    var conn = SessionManager.CreateConnectionInfo(connectHost, connectPort, hop.Username, hop.Password, hop.KeyPath, hop.KeyPassphrase, hop.UseAgent);
                    if (conn == null)
                    {
                        error = hop.UseAgent ? $"跳板机 {i + 1}：请启动 SSH Agent 并添加私钥" : $"跳板机 {i + 1} 请配置密码或私钥";
                        DisposeChain(chainDisposables);
                        return (null, null);
                    }

                    var client = new SshClient(conn);
                    SessionManager.AcceptAnyHostKey(client);
                    client.Connect();
                    chainDisposables.Add(client);

                    var nextHost = i + 1 < jumpChain.Count ? jumpChain[i + 1].Host : host;
                    var nextPort = (ushort)(i + 1 < jumpChain.Count ? jumpChain[i + 1].Port : port);
                    var fwd = new ForwardedPortLocal("127.0.0.1", 0, nextHost, nextPort);
                    client.AddForwardedPort(fwd);
                    fwd.Start();
                    chainDisposables.Add(fwd);

                    connectHost = "127.0.0.1";
                    connectPort = (ushort)fwd.BoundPort;
                }
            }

            var connectionInfo = SessionManager.CreateConnectionInfo(connectHost, connectPort, username, password, keyPath, keyPassphrase, useAgent);
            if (connectionInfo == null)
            {
                error = useAgent ? "请启动 SSH Agent（OpenSSH 或 PuTTY Pageant）并添加私钥" : "请配置密码或私钥";
                DisposeChain(chainDisposables);
                return (null, null);
            }

            return (connectionInfo, chainDisposables);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return (null, null);
        }
    }

    private static void DisposeChain(List<IDisposable>? chain)
    {
        if (chain == null) return;
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            try { chain[i].Dispose(); } catch { }
        }
    }

    /// <summary>获取或创建该节点的 SFTP 会话。连接失败时返回 null 并设置 error。</summary>
    private static HeldSession? GetOrCreate(string nodeId, Node node, IList<Node> allNodes, IList<Credential> credentials, IList<Tunnel> tunnels, out string? error)
    {
        error = null;
        lock (Gate)
        {
            if (Sessions.TryGetValue(nodeId, out var existing) && existing.Sftp.IsConnected)
                return existing;

            if (existing != null)
            {
                Sessions.Remove(nodeId);
                try { existing.Dispose(); } catch { }
            }

            var (connectionInfo, chainDisposables) = BuildConnection(node, allNodes, credentials, tunnels, out error);
            if (connectionInfo == null)
                return null;

            try
            {
                var sftp = new SftpClient(connectionInfo);
                SessionManager.AcceptAnyHostKey(sftp);
                sftp.Connect();
                var held = new HeldSession(sftp, chainDisposables);
                Sessions[nodeId] = held;
                return held;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                DisposeChain(chainDisposables);
                return null;
            }
        }
    }

    /// <summary>使用该节点的 SFTP 连接执行操作并返回结果。若连接断开或操作抛错会移除会话并设置 error，下次调用将自动重连。</summary>
    internal static T? ExecuteWithSftp<T>(string nodeId, Node node, IList<Node> allNodes, IList<Credential> credentials, IList<Tunnel> tunnels,
        Func<SftpClient, T> action, out string? error)
    {
        error = null;
        var session = GetOrCreate(nodeId, node, allNodes, credentials, tunnels, out error);
        if (session == null)
            return default;

        try
        {
            return action(session.Sftp);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            ClearSession(nodeId);
            return default;
        }
    }

    /// <summary>使用该节点的 SFTP 连接执行操作（返回值结构体）。若连接断开或操作抛错会移除会话并设置 error。</summary>
    internal static bool ExecuteWithSftp(string nodeId, Node node, IList<Node> allNodes, IList<Credential> credentials, IList<Tunnel> tunnels,
        Action<SftpClient> action, out string? error)
    {
        error = null;
        var session = GetOrCreate(nodeId, node, allNodes, credentials, tunnels, out error);
        if (session == null)
            return false;

        try
        {
            action(session.Sftp);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            ClearSession(nodeId);
            return false;
        }
    }

    /// <summary>关闭并移除该节点的 SFTP 长连接。应在该节点对应所有 tab 关闭时调用（与远程文件缓存清理时机一致）。</summary>
    public static void ClearSession(string? nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return;
        lock (Gate)
        {
            if (!Sessions.TryGetValue(nodeId, out var held)) return;
            Sessions.Remove(nodeId);
            try { held.Dispose(); } catch { }
        }
    }
}
