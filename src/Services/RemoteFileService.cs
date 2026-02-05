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

    /// <summary>使用 SFTP 删除远程文件或目录（目录会递归删除）。</summary>
    public static bool DeletePath(Node node, List<Node> allNodes, List<Credential> credentials, List<Tunnel> tunnels, string remotePath, bool isDirectory, out string? error)
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
                using var sftp = new SftpClient(connectionInfo);
                sftp.Connect();
                if (isDirectory)
                    DeleteDirectoryRecursive(sftp, remotePath);
                else
                    sftp.DeleteFile(remotePath);
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

    /// <summary>使用 SFTP 重命名远程文件或目录。</summary>
    public static bool RenamePath(Node node, List<Node> allNodes, List<Credential> credentials, List<Tunnel> tunnels, string oldRemotePath, string newRemotePath, out string? error)
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
                using var sftp = new SftpClient(connectionInfo);
                sftp.Connect();
                sftp.RenameFile(oldRemotePath, newRemotePath);
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

    private static void DeleteDirectoryRecursive(SftpClient sftp, string path)
    {
        var entries = sftp.ListDirectory(path);
        foreach (var e in entries)
        {
            if (e.Name == "." || e.Name == "..") continue;
            var full = path.TrimEnd('/') + "/" + e.Name;
            if (e.IsDirectory)
                DeleteDirectoryRecursive(sftp, full);
            else
                sftp.DeleteFile(full);
        }
        sftp.DeleteDirectory(path);
    }

    /// <summary>获取远程文件/目录的 Unix 权限（低 9 位，如 644）。</summary>
    public static bool GetFilePermissions(Node node, List<Node> allNodes, List<Credential> credentials, List<Tunnel> tunnels, string remotePath, out int mode, out string? error)
    {
        mode = 0;
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
                using var sftp = new SftpClient(connectionInfo);
                sftp.Connect();
                var a = sftp.GetAttributes(remotePath);
                int owner = (a.OwnerCanRead ? 4 : 0) + (a.OwnerCanWrite ? 2 : 0) + (a.OwnerCanExecute ? 1 : 0);
                int group = (a.GroupCanRead ? 4 : 0) + (a.GroupCanWrite ? 2 : 0) + (a.GroupCanExecute ? 1 : 0);
                int other = (a.OthersCanRead ? 4 : 0) + (a.OthersCanWrite ? 2 : 0) + (a.OthersCanExecute ? 1 : 0);
                mode = owner * 64 + group * 8 + other;
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

    /// <summary>设置远程文件/目录的 Unix 权限（仅修改低 9 位 rwxrwxrwx）。</summary>
    public static bool SetFilePermissions(Node node, List<Node> allNodes, List<Credential> credentials, List<Tunnel> tunnels, string remotePath, int mode, out string? error)
    {
        error = null;
        mode &= 0x1FF;
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
                using var sftp = new SftpClient(connectionInfo);
                sftp.Connect();
                sftp.ChangePermissions(remotePath, (short)mode);
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
