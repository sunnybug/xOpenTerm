using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace xOpenTerm.Services;

/// <summary>从 Windows Shell 按扩展名获取文件/文件夹图标，转为 WPF ImageSource 并缓存。</summary>
[SupportedOSPlatform("windows")]
internal static class ShellIconService
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiSmallIcon = 0x000000001;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeNormal = 0x00000080;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref ShFileInfo psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private static readonly ConcurrentDictionary<string, ImageSource?> FileIconCache = new();
    private static ImageSource? _folderIcon;
    private static ImageSource? _genericFileIcon;

    /// <summary>获取文件夹图标（小尺寸，用于列表）。</summary>
    public static ImageSource? GetFolderIcon()
    {
        if (_folderIcon != null) return _folderIcon;
        _folderIcon = GetIconFromPath(".", isDirectory: true) ?? GetIconFromPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows), isDirectory: true) ?? GetGenericFileIcon();
        return _folderIcon;
    }

    /// <summary>根据扩展名获取文件类型图标（如 "pdf"、"txt"），无扩展名时返回通用文档图标。</summary>
    public static ImageSource? GetFileIcon(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return GetGenericFileIcon();
        var ext = extension.TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) return GetGenericFileIcon();
        return FileIconCache.GetOrAdd(ext, e =>
        {
            var icon = GetIconFromPath("dummy." + e, isDirectory: false);
            return icon ?? GetGenericFileIcon();
        });
    }

    private static ImageSource? GetGenericFileIcon()
    {
        if (_genericFileIcon != null) return _genericFileIcon;
        _genericFileIcon = GetIconFromPath("dummy.txt", isDirectory: false);
        return _genericFileIcon;
    }

    private static ImageSource? GetIconFromPath(string path, bool isDirectory)
    {
        uint flags = ShgfiIcon | ShgfiSmallIcon | ShgfiUseFileAttributes;
        if (isDirectory) flags |= 0x000000004; // SHGFI_OPENICON 可选，这里用普通目录即可
        var shfi = new ShFileInfo();
        IntPtr hr = SHGetFileInfo(path, isDirectory ? 0x00000010u : FileAttributeNormal, ref shfi, (uint)Marshal.SizeOf<ShFileInfo>(), flags);
        if (hr == IntPtr.Zero || shfi.hIcon == IntPtr.Zero) return null;
        try
        {
            var bs = Imaging.CreateBitmapSourceFromHIcon(shfi.hIcon, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bs.Freeze();
            return bs;
        }
        finally
        {
            DestroyIcon(shfi.hIcon);
        }
    }
}
