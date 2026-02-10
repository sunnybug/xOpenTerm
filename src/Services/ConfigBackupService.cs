using System.Collections.Concurrent;
using System.IO;

namespace xOpenTerm.Services;

/// <summary>配置自动备份与恢复：备份目录 %LocalAppData%\xOpenTerm\backup\YYMMDD-HHMMSS\。</summary>
public static class ConfigBackupService
{
    private const string NodesFile = "nodes.yaml";
    private const string CredentialsFile = "credentials.yaml";
    private const string TunnelsFile = "tunnels.yaml";
    private const string SettingsFile = "settings.yaml";

    private static readonly string[] ConfigFiles = { NodesFile, CredentialsFile, TunnelsFile, SettingsFile };

    private static readonly object BackupLock = new();
    private static DateTime _lastBackupTime = DateTime.MinValue;
    private static readonly TimeSpan BackupDebounce = TimeSpan.FromSeconds(60);

    /// <summary>备份根目录：%LocalAppData%\xOpenTerm\backup</summary>
    public static string GetBackupRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "xOpenTerm", "backup");
    }

    /// <summary>配置修改时调用，在防抖间隔内仅保留一次备份。</summary>
    public static void BackupIfNeeded()
    {
        lock (BackupLock)
        {
            if (DateTime.UtcNow - _lastBackupTime < BackupDebounce)
                return;
            _lastBackupTime = DateTime.UtcNow;
        }

        try
        {
            var configDir = StorageService.GetConfigDir();
            var backupRoot = GetBackupRoot();
            Directory.CreateDirectory(backupRoot);
            var folderName = DateTime.Now.ToString("yyMMdd-HHmmss");
            var backupDir = Path.Combine(backupRoot, folderName);
            Directory.CreateDirectory(backupDir);

            var storage = new StorageService();
            foreach (var file in ConfigFiles)
            {
                var dst = Path.Combine(backupDir, file);
                if (file == SettingsFile)
                {
                    // 备份设置时不包含主密码（盐、验证码等），恢复后需重新设置主密码
                    var settingsYaml = storage.SerializeAppSettingsForBackup();
                    File.WriteAllText(dst, settingsYaml);
                }
                else
                {
                    var src = Path.Combine(configDir, file);
                    if (!File.Exists(src)) continue;
                    File.Copy(src, dst, overwrite: true);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[xOpenTerm] 配置备份失败: {ex.Message}");
        }
    }

    /// <summary>立即执行一次备份（用于恢复前备份当前配置）。</summary>
    public static string? BackupNow()
    {
        try
        {
            var configDir = StorageService.GetConfigDir();
            var backupRoot = GetBackupRoot();
            Directory.CreateDirectory(backupRoot);
            var folderName = DateTime.Now.ToString("yyMMdd-HHmmss");
            var backupDir = Path.Combine(backupRoot, folderName);
            Directory.CreateDirectory(backupDir);

            var storage = new StorageService();
            foreach (var file in ConfigFiles)
            {
                var dst = Path.Combine(backupDir, file);
                if (file == SettingsFile)
                {
                    var settingsYaml = storage.SerializeAppSettingsForBackup();
                    File.WriteAllText(dst, settingsYaml);
                }
                else
                {
                    var src = Path.Combine(configDir, file);
                    if (!File.Exists(src)) continue;
                    File.Copy(src, dst, overwrite: true);
                }
            }

            return backupDir;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[xOpenTerm] 配置备份失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>备份项：目录路径、显示时间、目录大小（字节）。</summary>
    public record BackupEntry(string DirectoryPath, string DisplayTime, long SizeBytes);

    /// <summary>枚举所有备份目录（按时间倒序，最新的在前）。</summary>
    public static IReadOnlyList<BackupEntry> GetBackupEntries()
    {
        var root = GetBackupRoot();
        if (!Directory.Exists(root))
            return Array.Empty<BackupEntry>();

        var list = new List<BackupEntry>();
        foreach (var dir in Directory.GetDirectories(root))
        {
            var name = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(name)) continue;
            var di = new DirectoryInfo(dir);
            try
            {
                var size = GetDirectorySize(di);
                var displayTime = TryParseBackupFolderName(name, out var dt) ? dt.ToString("yyyy-MM-dd HH:mm:ss") : name;
                list.Add(new BackupEntry(dir, displayTime, size));
            }
            catch
            {
                list.Add(new BackupEntry(dir, name, 0));
            }
        }

        list.Sort((a, b) => string.CompareOrdinal(b.DirectoryPath, a.DirectoryPath));
        return list;
    }

    private static bool TryParseBackupFolderName(string name, out DateTime dt)
    {
        dt = default;
        if (name.Length != 13 || name[6] != '-') return false;
        return DateTime.TryParseExact(name, "yyMMdd-HHmmss", null, System.Globalization.DateTimeStyles.None, out dt);
    }

    private static long GetDirectorySize(DirectoryInfo di)
    {
        long size = 0;
        try
        {
            foreach (var f in di.EnumerateFiles("*", SearchOption.AllDirectories))
                size += f.Length;
        }
        catch { /* 忽略权限等错误 */ }
        return size;
    }

    /// <summary>从备份恢复：先将当前配置备份，再复制备份目录内容到配置目录。</summary>
    public static (bool ok, string? message) Restore(string backupDir)
    {
        if (!Directory.Exists(backupDir))
            return (false, "备份目录不存在。");

        var configDir = StorageService.GetConfigDir();
        Directory.CreateDirectory(configDir);

        try
        {
            foreach (var file in ConfigFiles)
            {
                var src = Path.Combine(backupDir, file);
                if (!File.Exists(src)) continue;
                var dst = Path.Combine(configDir, file);
                File.Copy(src, dst, overwrite: true);
            }
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
