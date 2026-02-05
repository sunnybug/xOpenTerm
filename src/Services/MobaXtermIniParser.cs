using System.IO;
using System.Text;
using xOpenTerm.Models;

namespace xOpenTerm.Services;

/// <summary>从 MobaXterm.ini 解析 [Bookmarks] 中的会话与文件夹结构。编码为 Windows-1252，不可用时回退为 UTF-8。</summary>
public static class MobaXtermIniParser
{
    private static Encoding GetIniEncoding()
    {
        try
        {
            return Encoding.GetEncoding(1252);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    /// <summary>解析 INI 文件，返回所有可导入的会话（SSH/RDP），按文件夹层级带路径。</summary>
    public static List<MobaXtermSessionItem> Parse(string iniPath)
    {
        if (!File.Exists(iniPath)) return new List<MobaXtermSessionItem>();
        var lines = File.ReadAllLines(iniPath, GetIniEncoding());
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

    private static MobaXtermSessionItem? ParseSessionLine(string sessionName, string value, string folderPath)
    {
        var parts = value.Split('#');
        if (parts.Length < 4) return null;
        // parts[0]="", [1]=icon, [2]=?, [3]= 第一组 % 分隔
        var firstGroup = parts[3];
        var fields = firstGroup.Split('%');
        if (fields.Length < 3) return null;

        var sessionType = fields.Length > 0 ? fields[0] : "";
        // 0=SSH, 4=RDP
        if (sessionType != "0" && sessionType != "4") return null;

        var host = fields.Length > 1 ? fields[1].Trim() : "";
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
