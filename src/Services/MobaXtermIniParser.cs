using System.IO;
using System.Text;
using xOpenTerm.Models;

namespace xOpenTerm.Services;

/// <summary>从 MobaXterm.ini 解析 [Bookmarks] 中的会话与文件夹结构。自动检测编码：UTF-8 BOM → UTF-8，否则依次尝试 GBK、UTF-8，选无乱码的。</summary>
public static class MobaXtermIniParser
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>检测编码并读取所有行，避免中文乱码。有 UTF-8 BOM 用 UTF-8；否则先试 GBK，再试 UTF-8，选不出现替换符且能解析到 [Bookmarks] 的。</summary>
    private static string[] ReadAllLinesWithEncoding(string iniPath)
    {
        if (!File.Exists(iniPath)) return Array.Empty<string>();
        var bytes = File.ReadAllBytes(iniPath);
        var (encoding, skipBom) = DetectEncoding(bytes);
        var enc = Encoding.GetEncoding(encoding);
        var span = skipBom && bytes.Length >= 3 ? bytes.AsSpan(3) : bytes.AsSpan();
        var str = enc.GetString(span).Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        return str;
    }

    private static (string encoding, bool skipBom) DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == Utf8Bom[0] && bytes[1] == Utf8Bom[1] && bytes[2] == Utf8Bom[2])
            return ("utf-8", true);

        var strictUtf8 = new UTF8Encoding(false, true);
        try
        {
            var str = strictUtf8.GetString(bytes);
            if (str.IndexOf("[Bookmarks", StringComparison.OrdinalIgnoreCase) >= 0)
                return ("utf-8", false);
        }
        catch (DecoderFallbackException) { /* 非 UTF-8，使用 GBK */ }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            var gbk = Encoding.GetEncoding("GBK");
            var str = gbk.GetString(bytes);
            if (str.IndexOf("[Bookmarks", StringComparison.OrdinalIgnoreCase) >= 0)
                return ("GBK", false);
        }
        catch { /* 忽略 */ }

        return ("utf-8", false);
    }

    private static int CountChar(string s, char c)
    {
        var n = 0;
        foreach (var x in s) if (x == c) n++;
        return n;
    }

    /// <summary>解析 INI 文件，返回所有可导入的会话（SSH/RDP），按文件夹层级带路径。</summary>
    public static List<MobaXtermSessionItem> Parse(string iniPath)
    {
        if (!File.Exists(iniPath)) return new List<MobaXtermSessionItem>();
        var lines = ReadAllLinesWithEncoding(iniPath);
        var items = new List<MobaXtermSessionItem>();
        var currentFolderPath = ""; // 当前 [Bookmarks*] 的 SubRep，即 "Folder" 或 "Folder1\\Folder2"

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            if (string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith("[Bookmarks", StringComparison.OrdinalIgnoreCase))
            {
                currentFolderPath = "";
                continue;
            }

            if (line.StartsWith("SubRep=", StringComparison.OrdinalIgnoreCase))
            {
                var name = line.Substring(7).Trim();
                if (string.IsNullOrEmpty(name) || name.Equals("User sessions", StringComparison.OrdinalIgnoreCase))
                    currentFolderPath = "";
                else
                    currentFolderPath = name.Replace("\\", " / ");
                continue;
            }

            // 会话行：SessionName=#icon#0%type%host%port%...
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line.Substring(0, eq).Trim();
            var value = line.Substring(eq + 1);
            if (!value.StartsWith("#")) continue;

            var session = ParseSessionLine(key, value, currentFolderPath);
            if (session != null)
                items.Add(session);
        }

        return items;
    }

    /// <summary>将扁平会话列表按 FolderPath 构建为目录树，用于按目录多选导入。</summary>
    public static List<MobaFolderNode> BuildFolderTree(List<MobaXtermSessionItem> sessions)
    {
        var root = new MobaFolderNode { Name = "", FullPath = "" };
        foreach (var s in sessions)
        {
            var path = s.FolderPath ?? "";
            var parts = string.IsNullOrEmpty(path) ? Array.Empty<string>() : path.Split(new[] { " / " }, StringSplitOptions.None);
            var current = root;
            var currentPath = "";
            for (var i = 0; i < parts.Length; i++)
            {
                var segment = parts[i].Trim();
                if (string.IsNullOrEmpty(segment)) continue;
                currentPath = string.IsNullOrEmpty(currentPath) ? segment : currentPath + " / " + segment;
                var child = current.SubFolders.FirstOrDefault(f => f.FullPath == currentPath);
                if (child == null)
                {
                    child = new MobaFolderNode { Name = segment, FullPath = currentPath };
                    current.SubFolders.Add(child);
                }
                current = child;
            }
            current.Sessions.Add(s);
        }
        root.SortSubFoldersRecursive();
        var result = new List<MobaFolderNode>();
        if (root.Sessions.Count > 0)
        {
            var rootNode = new MobaFolderNode { Name = "(根目录)", FullPath = "" };
            rootNode.Sessions.AddRange(root.Sessions);
            result.Add(rootNode);
        }
        result.AddRange(root.SubFolders);
        return result;
    }

    private static MobaXtermSessionItem? ParseSessionLine(string sessionName, string value, string folderPath)
    {
        var parts = value.Split('#');
        // 格式: #图标#类型%host%port%username%... 仅 3 段: "", icon, "类型%host%port%..."
        if (parts.Length < 3) return null;
        var typeAndRest = parts[2];
        var fields = typeAndRest.Split('%');
        if (fields.Length < 4) return null;

        var sessionType = fields[0].Trim();
        // 0=SSH, 4=RDP
        if (sessionType != "0" && sessionType != "4") return null;

        var host = fields[1].Trim();
        if (string.IsNullOrEmpty(host) || host == "<<default>>") return null;

        var portStr = fields.Length > 2 ? fields[2] : "22";
        if (sessionType == "4") portStr = fields.Length > 2 ? fields[2] : "3389";
        if (!ushort.TryParse(portStr, out var port)) port = (ushort)(sessionType == "4" ? 3389 : 22);

        var username = fields.Length > 3 ? fields[3].Trim() : "";
        if (username == "<<default>>") username = "";

        string? keyPath = null;
        if (sessionType == "0" && fields.Length > 14 && !string.IsNullOrWhiteSpace(fields[14]))
        {
            keyPath = fields[14].Replace("_CurrentDrive_", "C:", StringComparison.OrdinalIgnoreCase).Trim();
            if (string.IsNullOrEmpty(keyPath)) keyPath = null;
        }

        return new MobaXtermSessionItem
        {
            FolderPath = folderPath ?? "",
            SessionName = sessionName,
            Host = host,
            Port = port,
            Username = username,
            KeyPath = keyPath,
            IsRdp = sessionType == "4"
        };
    }
}

/// <summary>按目录组织的节点，用于树形多选（以目录为单位）。</summary>
public class MobaFolderNode
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public List<MobaFolderNode> SubFolders { get; } = new();
    public List<MobaXtermSessionItem> Sessions { get; } = new();
    /// <summary>是否被勾选导入（用于树形多选）。</summary>
    public bool IsSelected { get; set; }

    /// <summary>递归统计本目录及子目录下的会话总数。</summary>
    public int TotalSessionCount => Sessions.Count + SubFolders.Sum(f => f.TotalSessionCount);

    /// <summary>树节点显示：目录名 (会话数)。</summary>
    public string DisplayText => TotalSessionCount > 0 ? $"{Name} ({TotalSessionCount})" : Name;

    /// <summary>递归收集本目录及子目录下全部会话，保持原有 FolderPath。</summary>
    public void CollectAllSessions(List<MobaXtermSessionItem> into)
    {
        into.AddRange(Sessions);
        foreach (var sub in SubFolders)
            sub.CollectAllSessions(into);
    }

    internal void SortSubFoldersRecursive()
    {
        SubFolders.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        foreach (var sub in SubFolders)
            sub.SortSubFoldersRecursive();
    }
}

/// <summary>从 MobaXterm.ini 解析出的单条会话，用于导入选择。</summary>
public class MobaXtermSessionItem
{
    public string FolderPath { get; set; } = "";
    public string SessionName { get; set; } = "";
    public string Host { get; set; } = "";
    public ushort Port { get; set; }
    public string Username { get; set; } = "";
    public string? KeyPath { get; set; }
    public bool IsRdp { get; set; }

    /// <summary>列表显示用：文件夹 | 会话名 (host:port)</summary>
    public string DisplayText =>
        string.IsNullOrEmpty(FolderPath)
            ? $"{SessionName} ({Host}:{Port})"
            : $"{FolderPath} | {SessionName} ({Host}:{Port})";

    public Node ToNode(string parentId)
    {
        var type = IsRdp ? NodeType.rdp : NodeType.ssh;
        var config = new ConnectionConfig
        {
            Host = Host,
            Port = Port,
            Username = string.IsNullOrEmpty(Username) ? null : Username,
            Protocol = Protocol.ssh,
            AuthType = !string.IsNullOrEmpty(KeyPath) ? AuthType.key : AuthType.password,
            KeyPath = KeyPath
        };
        var name = string.IsNullOrWhiteSpace(SessionName) ? Host : SessionName.Trim();
        return new Node
        {
            Id = Guid.NewGuid().ToString(),
            ParentId = parentId,
            Type = type,
            Name = name,
            Config = config
        };
    }
}
