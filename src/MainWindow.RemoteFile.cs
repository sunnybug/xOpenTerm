using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>主窗口：远程文件列表（浏览、编辑、改名、权限、删除、上传下载）。</summary>
public partial class MainWindow
{
    private void LoadRemoteFileList()
    {
        if (string.IsNullOrEmpty(_remoteFileNodeId))
        {
            RemoteFileList.ItemsSource = null;
            return;
        }
        var node = _nodes.FirstOrDefault(n => n.Id == _remoteFileNodeId);
        if (node == null) { RemoteFileList.ItemsSource = null; return; }
        var path = string.IsNullOrWhiteSpace(RemotePathBox.Text) ? "." : RemotePathBox.Text.Trim();
        _remoteFilePath = path;
        var list = RemoteFileService.ListDirectory(node, _nodes, _credentials, _tunnels, path, out var error);
        if (!string.IsNullOrEmpty(error))
        {
            RemoteFileList.ItemsSource = new List<RemoteFileItem>();
            if (_tabIdToTerminal.Count > 0)
            {
                var tabId = _tabIdToNodeId.FirstOrDefault(p => p.Value == _remoteFileNodeId).Key;
                if (!string.IsNullOrEmpty(tabId) && _tabIdToTerminal.TryGetValue(tabId, out var term))
                    term.Append("\r\n\x1b[31m[远程文件] " + error + "\x1b[0m\r\n");
            }
            return;
        }
        if (path != "." && path != "/")
        {
            var parentList = new List<RemoteFileItem> { new RemoteFileItem { Name = "..", IsDirectory = true } };
            parentList.AddRange(list);
            list = parentList;
        }
        RemoteFileList.ItemsSource = list;
    }

    private void RemotePathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        LoadRemoteFileList();
    }

    private void RemoteFileList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (RemoteFileList.SelectedItem is not RemoteFileItem item || item.Name == "..") return;
        if (e.Key == Key.F2)
        {
            e.Handled = true;
            RemoteFileList_Rename_Click(sender, new RoutedEventArgs());
        }
        else if (e.Key == Key.Delete)
        {
            e.Handled = true;
            RemoteFileList_Delete_Click(sender, new RoutedEventArgs());
        }
    }

    private void RemoteFileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RemoteFileList.SelectedItem is not RemoteFileItem item) return;
        var path = string.IsNullOrWhiteSpace(RemotePathBox.Text) ? "." : RemotePathBox.Text.Trim();
        if (path == ".") path = "";
        if (item.Name == "..")
        {
            var idx = path.TrimEnd('/').LastIndexOf('/');
            var newPath = idx <= 0 ? "." : path.Substring(0, idx);
            RemotePathBox.Text = newPath;
            _remoteFilePath = newPath;
            LoadRemoteFileList();
            return;
        }
        var newPath2 = string.IsNullOrEmpty(path) ? item.Name : path.TrimEnd('/') + "/" + item.Name;
        if (item.IsDirectory)
        {
            RemotePathBox.Text = newPath2;
            _remoteFilePath = newPath2;
            LoadRemoteFileList();
        }
    }

    private void RemoteFileList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        RemoteFileList.SelectedItem = null;
        var dep = e.OriginalSource as DependencyObject;
        while (dep != null)
        {
            if (dep is System.Windows.Controls.ListViewItem lvi && lvi.DataContext is RemoteFileItem rf)
            {
                RemoteFileList.SelectedItem = rf;
                break;
            }
            dep = VisualTreeHelper.GetParent(dep);
        }
        var item = RemoteFileList.SelectedItem as RemoteFileItem;
        var canActOnItem = item != null && item.Name != "..";
        RemoteFileMenuEdit.IsEnabled = canActOnItem && item != null && !item.IsDirectory;
        RemoteFileMenuRename.IsEnabled = canActOnItem;
        RemoteFileMenuPermissions.IsEnabled = canActOnItem;
        RemoteFileMenuDelete.IsEnabled = canActOnItem;
        RemoteFileMenuDownload.IsEnabled = canActOnItem;
        RemoteFileMenuUpload.IsEnabled = true;
    }

    private void RemoteFileList_Edit_Click(object sender, RoutedEventArgs e)
    {
        if (RemoteFileList.SelectedItem is not RemoteFileItem item || item.IsDirectory || item.Name == "..") return;
        var node = _nodes.FirstOrDefault(n => n.Id == _remoteFileNodeId);
        if (node == null) return;
        var path = string.IsNullOrWhiteSpace(RemotePathBox.Text) ? "." : RemotePathBox.Text.Trim();
        var remotePath = (path == "." || string.IsNullOrEmpty(path)) ? item.Name : path.TrimEnd('/') + "/" + item.Name;
        var tempPath = Path.Combine(Path.GetTempPath(), "xOpenTerm_edit_" + Path.GetRandomFileName() + "_" + item.Name);
        if (!RemoteFileService.DownloadFile(node, _nodes, _credentials, _tunnels, remotePath, tempPath, out var err))
        {
            MessageBox.Show("下载失败：" + err, "xOpenTerm");
            return;
        }
        var originalBytes = File.ReadAllBytes(tempPath);
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = tempPath,
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            MessageBox.Show("无法用默认程序打开：" + ex.Message, "xOpenTerm");
            try { File.Delete(tempPath); } catch { }
            return;
        }
        var nodeCopy = node;
        var nodesCopy = _nodes;
        var credentialsCopy = _credentials;
        var tunnelsCopy = _tunnels;
        Task.Run(() =>
        {
            const int stableSeconds = 3;
            const int pollIntervalMs = 2000;
            const int timeoutHours = 24;
            var lastWrite = File.GetLastWriteTimeUtc(tempPath);
            var deadline = DateTime.UtcNow.AddHours(timeoutHours);
            while (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(pollIntervalMs);
                if (!File.Exists(tempPath)) return;
                var now = File.GetLastWriteTimeUtc(tempPath);
                if (now != lastWrite)
                {
                    lastWrite = now;
                    Thread.Sleep(TimeSpan.FromSeconds(stableSeconds));
                    if (!File.Exists(tempPath)) return;
                    if (File.GetLastWriteTimeUtc(tempPath) != lastWrite) continue;
                    byte[] currentBytes;
                    try { currentBytes = File.ReadAllBytes(tempPath); } catch { return; }
                    var changed = currentBytes.Length != originalBytes.Length || !originalBytes.SequenceEqual(currentBytes);
                    Dispatcher.Invoke(() =>
                    {
                        if (changed && MessageBox.Show("文件内容已修改，是否上传到服务器？", "xOpenTerm", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                        {
                            if (RemoteFileService.UploadFile(nodeCopy, nodesCopy, credentialsCopy, tunnelsCopy, tempPath, remotePath, out var uploadErr))
                                LoadRemoteFileList();
                            else
                                MessageBox.Show("上传失败：" + uploadErr, "xOpenTerm");
                        }
                        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                    });
                    return;
                }
            }
        });
    }

    private void RemoteFileList_Rename_Click(object sender, RoutedEventArgs e)
    {
        if (RemoteFileList.SelectedItem is not RemoteFileItem item || item.Name == "..") return;
        var node = _nodes.FirstOrDefault(n => n.Id == _remoteFileNodeId);
        if (node == null) return;
        var path = string.IsNullOrWhiteSpace(RemotePathBox.Text) ? "." : RemotePathBox.Text.Trim();
        var remotePath = (path == "." || string.IsNullOrEmpty(path)) ? item.Name : path.TrimEnd('/') + "/" + item.Name;
        var newName = ShowRenameInput(item.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;
        if (newName.IndexOfAny(new[] { '/', '\\', '\0' }) >= 0)
        {
            MessageBox.Show("新名称不能包含 / 或 \\。", "xOpenTerm");
            return;
        }
        var newRemotePath = (path == "." || string.IsNullOrEmpty(path)) ? newName : path.TrimEnd('/') + "/" + newName;
        if (!RemoteFileService.RenamePath(node, _nodes, _credentials, _tunnels, remotePath, newRemotePath, out var err))
        {
            MessageBox.Show("改名失败：" + err, "xOpenTerm");
            return;
        }
        LoadRemoteFileList();
    }

    private string? ShowRenameInput(string currentName)
    {
        var box = new System.Windows.Controls.TextBox
        {
            Text = currentName,
            Margin = new Thickness(12, 8, 12, 8),
            MinWidth = 220
        };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "新名称：", Margin = new Thickness(12, 12, 12, 0) });
        panel.Children.Add(box);
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(12, 0, 12, 12) };
        string? result = null;
        var ok = new Button { Content = "确定", Width = 72, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "取消", Width = 72 };
        var win = new Window
        {
            Title = "改名",
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = this
        };
        ok.Click += (_, _) => { result = box.Text?.Trim(); win.DialogResult = true; win.Close(); };
        cancel.Click += (_, _) => { win.DialogResult = false; win.Close(); };
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { result = box.Text?.Trim(); win.DialogResult = true; win.Close(); } };
        btnPanel.Children.Add(ok);
        btnPanel.Children.Add(cancel);
        panel.Children.Add(btnPanel);
        win.ShowDialog();
        return result;
    }

    private void RemoteFileList_Permissions_Click(object sender, RoutedEventArgs e)
    {
        if (RemoteFileList.SelectedItem is not RemoteFileItem item || item.Name == "..") return;
        var node = _nodes.FirstOrDefault(n => n.Id == _remoteFileNodeId);
        if (node == null) return;
        var path = string.IsNullOrWhiteSpace(RemotePathBox.Text) ? "." : RemotePathBox.Text.Trim();
        var remotePath = (path == "." || string.IsNullOrEmpty(path)) ? item.Name : path.TrimEnd('/') + "/" + item.Name;
        if (!RemoteFileService.GetFilePermissions(node, _nodes, _credentials, _tunnels, remotePath, out var mode, out var err))
        {
            MessageBox.Show("获取权限失败：" + err, "xOpenTerm");
            return;
        }
        var dlg = new PermissionsWindow(item.Name, mode)
        {
            Owner = this
        };
        if (dlg.ShowDialog() != true || !dlg.ResultMode.HasValue) return;
        if (!RemoteFileService.SetFilePermissions(node, _nodes, _credentials, _tunnels, remotePath, dlg.ResultMode.Value, out err))
        {
            MessageBox.Show("设置权限失败：" + err, "xOpenTerm");
            return;
        }
        LoadRemoteFileList();
    }

    private void RemoteFileList_Delete_Click(object sender, RoutedEventArgs e)
    {
        if (RemoteFileList.SelectedItem is not RemoteFileItem item || item.Name == "..") return;
        var node = _nodes.FirstOrDefault(n => n.Id == _remoteFileNodeId);
        if (node == null) return;
        var path = string.IsNullOrWhiteSpace(RemotePathBox.Text) ? "." : RemotePathBox.Text.Trim();
        var remotePath = (path == "." || string.IsNullOrEmpty(path)) ? item.Name : path.TrimEnd('/') + "/" + item.Name;
        var tip = item.IsDirectory ? "确定要删除该目录及其内容吗？" : "确定要删除该文件吗？";
        if (MessageBox.Show(tip, "xOpenTerm", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (!RemoteFileService.DeletePath(node, _nodes, _credentials, _tunnels, remotePath, item.IsDirectory, out var err))
        {
            MessageBox.Show("删除失败：" + err, "xOpenTerm");
            return;
        }
        LoadRemoteFileList();
    }

    private void RemoteFileList_Download_Click(object sender, RoutedEventArgs e)
    {
        if (RemoteFileList.SelectedItem is not RemoteFileItem item || item.Name == "..") return;
        if (item.IsDirectory)
        {
            MessageBox.Show("暂不支持下载整个目录，请选择文件后下载。", "xOpenTerm");
            return;
        }
        var node = _nodes.FirstOrDefault(n => n.Id == _remoteFileNodeId);
        if (node == null) return;
        var path = string.IsNullOrWhiteSpace(RemotePathBox.Text) ? "." : RemotePathBox.Text.Trim();
        var remotePath = (path == "." || string.IsNullOrEmpty(path)) ? item.Name : path.TrimEnd('/') + "/" + item.Name;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = item.Name,
            Title = "保存到本地"
        };
        if (dlg.ShowDialog() != true) return;
        if (!RemoteFileService.DownloadFile(node, _nodes, _credentials, _tunnels, remotePath, dlg.FileName, out var err))
        {
            MessageBox.Show("下载失败：" + err, "xOpenTerm");
            return;
        }
        MessageBox.Show("下载完成。", "xOpenTerm");
    }

    private void RemoteFileList_Upload_Click(object sender, RoutedEventArgs e)
    {
        var node = _nodes.FirstOrDefault(n => n.Id == _remoteFileNodeId);
        if (node == null)
        {
            MessageBox.Show("请先连接一台 SSH 服务器以使用远程文件。", "xOpenTerm");
            return;
        }
        var path = string.IsNullOrWhiteSpace(RemotePathBox.Text) ? "." : RemotePathBox.Text.Trim();
        var remoteDir = (path == "." || string.IsNullOrEmpty(path)) ? "" : path.TrimEnd('/');
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "选择要上传的文件" };
        if (dlg.ShowDialog() != true) return;
        var remotePath = string.IsNullOrEmpty(remoteDir) ? Path.GetFileName(dlg.FileName) : remoteDir + "/" + Path.GetFileName(dlg.FileName);
        if (!RemoteFileService.UploadFile(node, _nodes, _credentials, _tunnels, dlg.FileName, remotePath, out var err))
        {
            MessageBox.Show("上传失败：" + err, "xOpenTerm");
            return;
        }
        LoadRemoteFileList();
    }
}
