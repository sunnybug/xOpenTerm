using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>配置备份与恢复窗口：列出备份时间与目录大小，支持恢复/打开备份目录。</summary>
public partial class BackupRestoreWindow : Window
{
    /// <summary>列表项：显示用，含格式化大小。</summary>
    private sealed class BackupItem
    {
        public string DisplayTime { get; init; } = "";
        public string SizeDisplay { get; init; } = "";
        public string DirectoryPath { get; init; } = "";
    }

    public BackupRestoreWindow(Window? owner)
    {
        InitializeComponent();
        Owner = owner;
        LoadBackups();
    }

    private void LoadBackups()
    {
        var entries = ConfigBackupService.GetBackupEntries();
        var items = entries.Select(e => new BackupItem
        {
            DisplayTime = e.DisplayTime,
            SizeDisplay = FormatSize(e.SizeBytes),
            DirectoryPath = e.DirectoryPath
        }).ToList();
        BackupList.ItemsSource = items;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }

    private void RefreshBtn_Click(object sender, RoutedEventArgs e) => LoadBackups();

    private void CurrentConfigBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var configDir = App.GetStorageService()?.GetConfigDir() ?? StorageService.GetConfigDir();
            Process.Start(new ProcessStartInfo
            {
                FileName = configDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "无法打开当前配置目录：\n" + ex.Message, "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenBtn_Click(object sender, RoutedEventArgs e)
    {
        var item = GetItemFromSender(sender);
        if (item == null) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = item.DirectoryPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "无法打开目录：\n" + ex.Message, "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RestoreBtn_Click(object sender, RoutedEventArgs e)
    {
        var item = GetItemFromSender(sender);
        if (item == null) return;
        if (MessageBox.Show(this,
                "恢复前将先备份当前配置。确定要从此备份恢复吗？恢复后请重新加载或重启应用使配置生效。",
                "xOpenTerm",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var backupCurrent = ConfigBackupService.BackupNow();
        if (string.IsNullOrEmpty(backupCurrent))
        {
            MessageBox.Show(this, "当前配置备份失败，已取消恢复。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var (ok, message) = ConfigBackupService.Restore(item.DirectoryPath);
        if (!ok)
        {
            MessageBox.Show(this, "恢复失败：\n" + (message ?? "未知错误"), "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        MessageBox.Show(this,
            "配置已恢复。当前配置已备份至：\n" + backupCurrent + "\n\n请关闭本窗口后，主窗口将自动重新加载配置。",
            "xOpenTerm",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        DialogResult = true;
        Close();
    }

    private static BackupItem? GetItemFromSender(object sender)
    {
        if (sender is not Button btn || btn.DataContext is not BackupItem item)
            return null;
        return item;
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}
