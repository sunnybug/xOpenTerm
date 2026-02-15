using System.IO;
using Renci.SshNet;
using xOpenTerm.Models;

namespace xOpenTerm.Services;

/// <summary>远程文件列表与传输：用 SFTP 列目录与传输（按节点复用长连接）。</summary>
public static class RemoteFileService
{
    /// <summary>列出远程目录内容。优先使用该节点的 SFTP 长连接，无连接或断线时自动建连并复用。</summary>
    public static List<RemoteFileItem> ListDirectory(Node node, List<Node> allNodes, List<Credential> credentials, List<Tunnel> tunnels, string remotePath, out string? error)
    {
        var list = SftpSessionManager.ExecuteWithSftp(node.Id, node, allNodes, credentials, tunnels, sftp =>
        {
            var entries = sftp.ListDirectory(remotePath);
            var outList = new List<RemoteFileItem>();
            foreach (var e in entries)
            {
                if (e.Name == "." || e.Name == "..") continue;
                outList.Add(new RemoteFileItem
                {
                    Name = e.Name,
                    IsDirectory = e.IsDirectory,
                    Length = e.Length,
                    FullName = e.FullName,
                    LastWriteTime = e.LastWriteTime
                });
            }
            return outList.OrderBy(x => !x.IsDirectory).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }, out error);
        return list ?? new List<RemoteFileItem>();
    }

    /// <summary>使用 SFTP 下载远程文件到本地（复用该节点长连接）。</summary>
    public static bool DownloadFile(Node node, List<Node> allNodes, List<Credential> credentials, List<Tunnel> tunnels, string remotePath, string localPath, out string? error)
    {
        return SftpSessionManager.ExecuteWithSftp(node.Id, node, allNodes, credentials, tunnels, sftp =>
        {
            using var fs = File.OpenWrite(localPath);
            sftp.DownloadFile(remotePath, fs);
        }, out error);
    }

    /// <summary>使用 SFTP 上传本地文件到远程（复用该节点长连接）。</summary>
    public static bool UploadFile(Node node, List<Node> allNodes, List<Credential> credentials, List<Tunnel> tunnels, string localPath, string remotePath, out string? error)
    {
        return SftpSessionManager.ExecuteWithSftp(node.Id, node, allNodes, credentials, tunnels, sftp =>
        {
            using var fs = File.OpenRead(localPath);
            sftp.UploadFile(fs, remotePath);
        }, out error);
    }

    /// <summary>使用 SFTP 删除远程文件或目录（目录会递归删除，复用该节点长连接）。</summary>
    public static bool DeletePath(Node node, List<Node> allNodes, List<Credential> credentials, List<Tunnel> tunnels, string remotePath, bool isDirectory, out string? error)
    {
        return SftpSessionManager.ExecuteWithSftp(node.Id, node, allNodes, credentials, tunnels, sftp =>
        {
            if (isDirectory)
                DeleteDirectoryRecursive(sftp, remotePath);
            else
                sftp.DeleteFile(remotePath);
        }, out error);
    }

    /// <summary>使用 SFTP 重命名远程文件或目录（复用该节点长连接）。</summary>
    public static bool RenamePath(Node node, List<Node> allNodes, List<Credential> credentials, List<Tunnel> tunnels, string oldRemotePath, string newRemotePath, out string? error)
    {
        return SftpSessionManager.ExecuteWithSftp(node.Id, node, allNodes, credentials, tunnels, sftp =>
            sftp.RenameFile(oldRemotePath, newRemotePath), out error);
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

    /// <summary>获取远程文件/目录的 Unix 权限（低 9 位，如 644，复用该节点长连接）。</summary>
    public static bool GetFilePermissions(Node node, List<Node> allNodes, List<Credential> credentials, List<Tunnel> tunnels, string remotePath, out int mode, out string? error)
    {
        mode = 0;
        var modeVal = SftpSessionManager.ExecuteWithSftp(node.Id, node, allNodes, credentials, tunnels, sftp =>
        {
            var a = sftp.GetAttributes(remotePath);
            int owner = (a.OwnerCanRead ? 4 : 0) + (a.OwnerCanWrite ? 2 : 0) + (a.OwnerCanExecute ? 1 : 0);
            int group = (a.GroupCanRead ? 4 : 0) + (a.GroupCanWrite ? 2 : 0) + (a.GroupCanExecute ? 1 : 0);
            int other = (a.OthersCanRead ? 4 : 0) + (a.OthersCanWrite ? 2 : 0) + (a.OthersCanExecute ? 1 : 0);
            return owner * 64 + group * 8 + other;
        }, out error);
        if (modeVal is { } m)
        {
            mode = m;
            return true;
        }
        return false;
    }

    /// <summary>设置远程文件/目录的 Unix 权限（仅修改低 9 位 rwxrwxrwx，复用该节点长连接）。</summary>
    public static bool SetFilePermissions(Node node, List<Node> allNodes, List<Credential> credentials, List<Tunnel> tunnels, string remotePath, int mode, out string? error)
    {
        mode &= 0x1FF;
        return SftpSessionManager.ExecuteWithSftp(node.Id, node, allNodes, credentials, tunnels, sftp =>
            sftp.ChangePermissions(remotePath, (short)mode), out error);
    }
}
