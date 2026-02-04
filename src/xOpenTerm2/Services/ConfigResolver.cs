using xOpenTerm2.Models;

namespace xOpenTerm2.Services;

/// <summary>单跳跳板机的连接参数（用于多跳隧道链）</summary>
public record JumpHop(string Host, ushort Port, string Username, string? Password, string? KeyPath, string? KeyPassphrase);

/// <summary>解析节点配置：认证来源、凭证、隧道（直连 + 多跳跳板机）</summary>
public static class ConfigResolver
{
    /// <summary>解析 RDP 连接参数（主机、端口、用户名、域、密码）。</summary>
    public static (string host, int port, string username, string domain, string? password) ResolveRdp(
        Node node, List<Node> allNodes, List<Credential> credentials)
    {
        var effective = ResolveEffectiveRdpConfig(node, allNodes);
        var host = effective.Host?.Trim() ?? "";
        var port = effective.Port ?? 3389;
        string username;
        string domain = ""; // RDP 不用域
        string? password = null;

        if (effective.AuthSource == AuthSource.credential && !string.IsNullOrEmpty(effective.CredentialId))
        {
            var cred = credentials.FirstOrDefault(c => c.Id == effective.CredentialId);
            if (cred == null) throw new InvalidOperationException($"登录凭证不存在: {effective.CredentialId}");
            username = cred.Username?.Trim() ?? "";
            if (cred.AuthType == AuthType.password) password = cred.Password;
        }
        else
        {
            username = effective.Username?.Trim() ?? "";
            password = effective.Password;
        }
        if (string.IsNullOrEmpty(username)) username = "administrator";

        return (host, port, username, domain, password);
    }

    private static ConnectionConfig ResolveEffectiveRdpConfig(Node rdpNode, List<Node> allNodes)
    {
        var config = rdpNode.Config;
        if (config == null) return new ConnectionConfig();
        if (config.AuthSource != AuthSource.parent) return config;

        var parentId = rdpNode.ParentId;
        if (string.IsNullOrEmpty(parentId)) return config;
        var parent = allNodes.FirstOrDefault(n => n.Id == parentId);
        if (parent?.Type != NodeType.group || parent.Config == null) return config;

        return new ConnectionConfig
        {
            Host = config.Host ?? parent.Config.Host,
            Port = config.Port ?? parent.Config.Port,
            Username = parent.Config.Username ?? config.Username,
            AuthType = parent.Config.AuthType ?? config.AuthType,
            Password = parent.Config.Password ?? config.Password,
            KeyPath = parent.Config.KeyPath ?? config.KeyPath,
            KeyPassphrase = parent.Config.KeyPassphrase ?? config.KeyPassphrase,
            AuthSource = parent.Config.AuthSource,
            CredentialId = parent.Config.CredentialId ?? config.CredentialId,
            Domain = parent.Config.Domain ?? config.Domain
        };
    }

    /// <summary>解析 SSH 连接参数（支持直连或经 TunnelIds 多跳），并解析隧道链。</summary>
    public static (string host, ushort port, string username, string? password, string? keyPath, string? keyPassphrase, List<JumpHop>? jumpChain) ResolveSsh(
        Node node, List<Node> allNodes, List<Credential> credentials, List<Tunnel> tunnels)
    {
        var effective = ResolveEffectiveSshConfig(node, allNodes);
        var host = effective.Host ?? "";
        var port = (ushort)(effective.Port ?? 22);
        string username;
        string? password = null;
        string? keyPath = null;
        string? keyPassphrase = null;

        if (effective.AuthSource == AuthSource.credential && !string.IsNullOrEmpty(effective.CredentialId))
        {
            var cred = credentials.FirstOrDefault(c => c.Id == effective.CredentialId);
            if (cred == null) throw new InvalidOperationException($"登录凭证不存在: {effective.CredentialId}");
            username = cred.Username;
            switch (cred.AuthType)
            {
                case AuthType.password: password = cred.Password; break;
                case AuthType.key: keyPath = cred.KeyPath; keyPassphrase = cred.KeyPassphrase; break;
            }
        }
        else
        {
            username = effective.Username ?? "";
            switch (effective.AuthType ?? AuthType.password)
            {
                case AuthType.password: password = effective.Password; break;
                case AuthType.key: keyPath = effective.KeyPath; keyPassphrase = effective.KeyPassphrase; break;
            }
        }

        List<JumpHop>? jumpChain = ResolveTunnelChain(effective.TunnelIds, tunnels, credentials);
        return (host, port, username, password, keyPath, keyPassphrase, jumpChain);
    }

    /// <summary>根据 TunnelIds 解析出有序的跳板机连接参数列表（多跳链）。</summary>
    public static List<JumpHop>? ResolveTunnelChain(List<string>? tunnelIds, List<Tunnel> tunnels, List<Credential> credentials)
    {
        if (tunnelIds == null || tunnelIds.Count == 0) return null;
        var chain = new List<JumpHop>();
        foreach (var id in tunnelIds)
        {
            var t = tunnels.FirstOrDefault(x => x.Id == id);
            if (t == null) continue;
            var hop = ResolveTunnelAuth(t, credentials);
            chain.Add(hop);
        }
        return chain.Count == 0 ? null : chain;
    }

    /// <summary>解析单个隧道（跳板机）的认证参数。</summary>
    public static JumpHop ResolveTunnelAuth(Tunnel t, List<Credential> credentials)
    {
        var port = (ushort)(t.Port ?? 22);
        string username = t.Username ?? "";
        string? password = null;
        string? keyPath = null;
        string? keyPassphrase = null;
        if (!string.IsNullOrEmpty(t.CredentialId))
        {
            var cred = credentials.FirstOrDefault(c => c.Id == t.CredentialId);
            if (cred != null)
            {
                username = cred.Username;
                switch (cred.AuthType)
                {
                    case AuthType.password: password = cred.Password; break;
                    case AuthType.key: keyPath = cred.KeyPath; keyPassphrase = cred.KeyPassphrase; break;
                }
            }
        }
        if (string.IsNullOrEmpty(password) && string.IsNullOrEmpty(keyPath))
        {
            switch (t.AuthType)
            {
                case AuthType.password: password = t.Password; break;
                case AuthType.key: keyPath = t.KeyPath; keyPassphrase = t.KeyPassphrase; break;
            }
        }
        return new JumpHop(t.Host ?? "", port, username, password, keyPath, keyPassphrase);
    }

    private static ConnectionConfig ResolveEffectiveSshConfig(Node sshNode, List<Node> allNodes)
    {
        var config = sshNode.Config;
        if (config == null) return new ConnectionConfig();
        if (config.AuthSource != AuthSource.parent) return config;

        var parentId = sshNode.ParentId;
        if (string.IsNullOrEmpty(parentId)) return config;
        var parent = allNodes.FirstOrDefault(n => n.Id == parentId);
        if (parent?.Type != NodeType.group || parent.Config == null) return config;

        return new ConnectionConfig
        {
            Host = config.Host,
            Port = config.Port,
            Username = parent.Config.Username ?? config.Username,
            AuthType = parent.Config.AuthType ?? config.AuthType,
            Password = parent.Config.Password ?? config.Password,
            KeyPath = parent.Config.KeyPath ?? config.KeyPath,
            KeyPassphrase = parent.Config.KeyPassphrase ?? config.KeyPassphrase,
            AuthSource = parent.Config.AuthSource,
            CredentialId = parent.Config.CredentialId ?? config.CredentialId,
            TunnelIds = parent.Config.TunnelIds ?? config.TunnelIds,
            TunnelId = parent.Config.TunnelId ?? config.TunnelId,
            Tunnel = parent.Config.Tunnel ?? config.Tunnel
        };
    }
}
