using System.IO;
using Renci.SshNet;
using xOpenTerm.Models;

namespace xOpenTerm.Services;

/// <summary>远程文件列表与传输：用 SFTP 列目录，用 SCP 传输。</summary>
public static class RemoteFileService
{
    /// <summary>列出远程目录内容。使用 SFTP 连接（与 SSH 会话相同的跳板链）。</summary>
    public static List<RemoteFileItem> ListDirectory(Node node, List<Node> allNodes, List<Credential> credentials, List<Tunnel> tunnels, string remotePath, out string? error)
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
                connectPort = (ushort)jumpChain[0].Port;

                for (var i = 0; i < jumpChain.Count; i++)
                {
                    var hop = jumpChain[i];
                    var conn = SessionManager.CreateConnectionInfo(connectHost, connectPort, hop.Username, hop.Password, hop.KeyPath, hop.KeyPassphrase, hop.UseAgent);
                    if (conn == null) { error = hop.UseAgent ? $"跳板机 {i + 1}：请启动 SSH Agent 并添加私钥" : $"跳板机 {i + 1} 请配置密码或私钥"; return new List<RemoteFileItem>(); }

                    var client = new SshClient(conn);
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
            if (connectionInfo == null) { error = useAgent ? "请启动 SSH Agent（OpenSSH 或 PuTTY Pageant）并添加私钥" : "请配置密码或私钥"; return new List<RemoteFileItem>(); }

            try
            {
                using var sftp = new SftpClient(connectionInfo);
                sftp.Connect();
                var entries = sftp.ListDirectory(remotePath);
                var list = new List<RemoteFileItem>();
                foreach (var e in entries)
                {
                    if (e.Name == "." || e.Name == "..") continue;
                    list.Add(new RemoteFileItem
                    {
                        Name = e.Name,
                        IsDirectory = e.IsDirectory,
                        Length = e.Length,
                        FullName = e.FullName
                    });
                }
                return list.OrderBy(x => !x.IsDirectory).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
            finally
            {
                if (chainDisposables != null)
                {
                    for (var i = chainDisposables.Count - 1; i >= 0; i--)
                    {
                        try { chainDisposables[i].Dispose(); } catch { }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return new List<RemoteFileItem>();
        }
    }

    /// <summary>使用 SCP 下载远程文件到本地。</summary>
    public static bool DownloadFile(Node node, List<Node> allNodes, List<Credential> credentials, List<Tunnel> tunnels, string remotePath, string localPath, out string? error)
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
                connectPort = (ushort)jumpChain[0].Port;
                for (var i = 0; i < jumpChain.Count; i++)
                {
                    var hop = jumpChain[i];
                    var conn = SessionManager.CreateConnectionInfo(connectHost, connectPort, hop.Username, hop.Password, hop.KeyPath, hop.KeyPassphrase, hop.UseAgent);
                    if (conn == null) { error = hop.UseAgent ? $"跳板机 {i + 1}：请启动 SSH Agent 并添加私钥" : $"跳板机 {i + 1} 请配置密码或私钥"; return false; }
                    var client = new SshClient(conn);
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
            if (connectionInfo == null) { error = useAgent ? "请启动 SSH Agent（OpenSSH 或 PuTTY Pageant）并添加私钥" : "请配置密码或私钥"; return false; }

            try
            {
                using var scp = new ScpClient(connectionInfo);
                scp.Connect();
                using (var fs = File.OpenWrite(localPath))
                    scp.Download(remotePath, fs);
                return true;
            }
            finally
            {
                if (chainDisposables != null)
                {
                    for (var i = chainDisposables.Count - 1; i >= 0; i--)
                    {
                        try { chainDisposables[i].Dispose(); } catch { }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>使用 SCP 上传本地文件到远程。</summary>
    public static bool UploadFile(Node node, List<Node> allNodes, List<Credential> credentials, List<Tunnel> tunnels, string localPath, string remotePath, out string? error)
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
                connectPort = (ushort)jumpChain[0].Port;
                for (var i = 0; i < jumpChain.Count; i++)
                {
                    var hop = jumpChain[i];
                    var conn = SessionManager.CreateConnectionInfo(connectHost, connectPort, hop.Username, hop.Password, hop.KeyPath, hop.KeyPassphrase, hop.UseAgent);
                    if (conn == null) { error = hop.UseAgent ? $"跳板机 {i + 1}：请启动 SSH Agent 并添加私钥" : $"跳板机 {i + 1} 请配置密码或私钥"; return false; }
                    var client = new SshClient(conn);
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
            if (connectionInfo == null) { error = useAgent ? "请启动 SSH Agent（OpenSSH 或 PuTTY Pageant）并添加私钥" : "请配置密码或私钥"; return false; }

            try
            {
                using var scp = new ScpClient(connectionInfo);
                scp.Connect();
                using (var fs = File.OpenRead(localPath))
                    scp.Upload(fs, remotePath);
                return true;
            }
            finally
            {
                if (chainDisposables != null)
                {
                    for (var i = chainDisposables.Count - 1; i >= 0; i--)
                    {
                        try { chainDisposables[i].Dispose(); } catch { }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
