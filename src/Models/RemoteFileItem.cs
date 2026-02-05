namespace xOpenTerm.Models;

/// <summary>远程文件/目录项，用于列表展示</summary>
public class RemoteFileItem
{
    public string Name { get; set; } = "";
    public bool IsDirectory { get; set; }
    public long Length { get; set; }
    public string? FullName { get; set; }
    /// <summary>用于列表显示：目录 / 文件</summary>
    public string DisplayType => IsDirectory ? "目录" : "文件";
    /// <summary>用于列表显示：目录不显示大小；按大小自动选用 B/KB/MB/GB/TB</summary>
    public string DisplaySize => IsDirectory ? "" : FormatFileSize(Length);

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
