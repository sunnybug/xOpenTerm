namespace xOpenTerm2.Models;

/// <summary>远程文件/目录项，用于列表展示</summary>
public class RemoteFileItem
{
    public string Name { get; set; } = "";
    public bool IsDirectory { get; set; }
    public long Length { get; set; }
    public string? FullName { get; set; }
    /// <summary>用于列表显示：目录 / 文件</summary>
    public string DisplayType => IsDirectory ? "目录" : "文件";
    /// <summary>用于列表显示：目录不显示大小</summary>
    public string DisplaySize => IsDirectory ? "" : Length.ToString("N0");
}
