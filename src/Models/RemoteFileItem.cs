using System.Windows.Media;

namespace xOpenTerm.Models;

/// <summary>远程文件/目录项，用于列表展示</summary>
public class RemoteFileItem
{
    // 各类型图标颜色（深色背景下可见，冻结单例避免重复创建）
    private static readonly Brush IconFolder = CreateFrozenBrush("#FFB900");   // 文件夹 黄
    private static readonly Brush IconPicture = CreateFrozenBrush("#2D7D46");  // 图片 绿
    private static readonly Brush IconVideo = CreateFrozenBrush("#9B4F96");   // 视频 紫
    private static readonly Brush IconAudio = CreateFrozenBrush("#E74856");    // 音频 红
    private static readonly Brush IconPackage = CreateFrozenBrush("#F7630C");  // 压缩包 橙
    private static readonly Brush IconPdf = CreateFrozenBrush("#D13438");     // PDF 红
    private static readonly Brush IconCode = CreateFrozenBrush("#0078D4");    // 代码 蓝
    private static readonly Brush IconWeb = CreateFrozenBrush("#F7630C");    // 网页 橙
    private static readonly Brush IconStyle = CreateFrozenBrush("#0078D4");  // 样式 蓝
    private static readonly Brush IconRead = CreateFrozenBrush("#5C2D91");    // Markdown 紫
    private static readonly Brush IconScript = CreateFrozenBrush("#2D7D46");  // 脚本 绿
    private static readonly Brush IconDocument = CreateFrozenBrush("#A0A0A0"); // 文档 灰

    private static Brush CreateFrozenBrush(string hex)
    {
        var b = (Brush)new BrushConverter().ConvertFrom(hex)!;
        b.Freeze();
        return b;
    }
    public string Name { get; set; } = "";
    public bool IsDirectory { get; set; }
    public long Length { get; set; }
    public string? FullName { get; set; }
    /// <summary>远程文件/目录的修改时间（来自 SFTP）。</summary>
    public DateTime? LastWriteTime { get; set; }
    /// <summary>用于列表显示：目录不显示大小；按大小自动选用 B/KB/MB/GB/TB</summary>
    public string DisplaySize => IsDirectory ? "" : FormatFileSize(Length);
    /// <summary>用于列表显示：修改时间，无则为空</summary>
    public string DisplayModifiedTime => LastWriteTime.HasValue ? LastWriteTime.Value.ToString("yyyy-MM-dd HH:mm") : "";
    /// <summary>Segoe MDL2 Assets 图标：按类型/扩展名返回对应字形</summary>
    public string IconGlyph => GetIconGlyphForItem();
    /// <summary>图标前景色（按文件类型区分）</summary>
    public Brush IconForeground => GetIconForegroundForItem();

    private string GetIconGlyphForItem()
    {
        if (IsDirectory) return "\uE8B7"; // Folder
        var ext = GetExtension();
        if (string.IsNullOrEmpty(ext)) return "\uE8A5"; // Document (default)
        return GetIconGlyphForExtension(ext);
    }

    private Brush GetIconForegroundForItem()
    {
        if (IsDirectory) return IconFolder;
        var ext = GetExtension();
        if (string.IsNullOrEmpty(ext)) return IconDocument;
        return GetIconForegroundForExtension(ext);
    }

    private static string GetExtension(string name)
    {
        var i = name.LastIndexOf('.');
        return i >= 0 && i < name.Length - 1 ? name.Substring(i + 1) : "";
    }

    private string GetExtension() => GetExtension(Name);

    private static Brush GetIconForegroundForExtension(string ext)
    {
        var e = ext.Trim().ToLowerInvariant();
        switch (e)
        {
            case "jpg": case "jpeg": case "png": case "gif": case "bmp": case "webp": case "ico": case "svg": case "tiff": case "tif":
                return IconPicture;
            case "mp4": case "mkv": case "avi": case "mov": case "wmv": case "webm": case "flv": case "m4v": case "mpeg": case "mpg":
                return IconVideo;
            case "mp3": case "wav": case "flac": case "ogg": case "m4a": case "aac": case "wma": case "opus":
                return IconAudio;
            case "zip": case "rar": case "7z": case "tar": case "gz": case "bz2": case "xz": case "zst":
                return IconPackage;
            case "pdf":
                return IconPdf;
            case "py": case "js": case "ts": case "jsx": case "tsx": case "cs": case "java": case "kt":
            case "cpp": case "c": case "h": case "hpp": case "go": case "rs": case "rb": case "php":
            case "swift": case "m": case "mm": case "vue": case "svelte":
                return IconCode;
            case "html": case "htm": case "xhtml":
                return IconWeb;
            case "css": case "scss": case "less":
                return IconStyle;
            case "json": case "xml": case "yaml": case "yml": case "toml": case "ini": case "cfg": case "conf":
                return IconCode;
            case "md": case "markdown":
                return IconRead;
            case "sh": case "bash": case "zsh": case "ps1": case "bat": case "cmd":
                return IconScript;
            default:
                return IconDocument;
        }
    }

    /// <summary>常见文件类型对应的 Segoe MDL2 字形（小写扩展名 -> Unicode 字符）</summary>
    private static string GetIconGlyphForExtension(string ext)
    {
        var e = ext.Trim().ToLowerInvariant();
        switch (e)
        {
            case "jpg": case "jpeg": case "png": case "gif": case "bmp": case "webp": case "ico": case "svg": case "tiff": case "tif":
                return "\uE8B9"; // Picture
            case "mp4": case "mkv": case "avi": case "mov": case "wmv": case "webm": case "flv": case "m4v": case "mpeg": case "mpg":
                return "\uE8B2"; // Movies
            case "mp3": case "wav": case "flac": case "ogg": case "m4a": case "aac": case "wma": case "opus":
                return "\uE8D6"; // Audio
            case "zip": case "rar": case "7z": case "tar": case "gz": case "bz2": case "xz": case "zst":
                return "\uE7B8"; // Package
            case "pdf":
                return "\uEA90"; // PDF
            case "py": case "js": case "ts": case "jsx": case "tsx": case "cs": case "java": case "kt":
            case "cpp": case "c": case "h": case "hpp": case "go": case "rs": case "rb": case "php":
            case "swift": case "m": case "mm": case "vue": case "svelte":
                return "\uE943"; // Code
            case "html": case "htm": case "xhtml":
                return "\uE8A1"; // PreviewLink
            case "css": case "scss": case "less":
                return "\uE890"; // View
            case "json": case "xml": case "yaml": case "yml": case "toml": case "ini": case "cfg": case "conf":
                return "\uE943"; // Code
            case "md": case "markdown":
                return "\uE8C3"; // Read
            case "sh": case "bash": case "zsh": case "ps1": case "bat": case "cmd":
                return "\uE756"; // CommandPrompt
            default:
                return "\uE8A5"; // Document
        }
    }

    private static string FormatFileSize(long bytes)
    {
        const long KB = 1024L;
        const long MB = KB * 1024;
        const long GB = MB * 1024;
        const long TB = GB * 1024;
        if (bytes >= TB) return $"{bytes / (double)TB:F2} TB";
        if (bytes >= GB) return $"{bytes / (double)GB:F2} GB";
        if (bytes >= MB) return $"{bytes / (double)MB:F2} MB";
        if (bytes >= KB) return $"{bytes / (double)KB:F2} KB";
        return $"{bytes} B";
    }
}
