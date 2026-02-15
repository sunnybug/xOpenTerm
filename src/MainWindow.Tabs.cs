using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using xOpenTerm.Controls;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>主窗口：标签页与会话（SSH/RDP）管理。</summary>
public partial class MainWindow
{
    private void OpenTab(Node node)
    {
        if (node.Type == NodeType.group || node.Type == NodeType.tencentCloudGroup || node.Type == NodeType.aliCloudGroup || node.Type == NodeType.kingsoftCloudGroup) return;
        if (node.Type == NodeType.rdp)
        {
            OpenRdpTab(node);
            return;
        }

        var sameCount = _tabIdToNodeId.Values.Count(id => id == node.Id);
        var tabTitle = sameCount == 0 ? node.Name : $"{node.Name} ({sameCount + 1})";
        var tabId = "tab-" + DateTime.UtcNow.Ticks;

        if (node.Type == NodeType.ssh)
        {
            try
            {
                var (host, port, username, password, keyPath, keyPassphrase, jumpChain, useAgent) =
                    ConfigResolver.ResolveSsh(node, _nodes, _credentials, _tunnels);
                OpenSshPuttyTab(tabId, tabTitle, node, host, port, username, password, keyPath, keyPassphrase, useAgent, jumpChain);
            }
            catch (Exception ex)
            {
                var text = new TextBlock
                {
                    Text = ex.Message,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCD, 0x31, 0x31)),
                    TextWrapping = TextWrapping.Wrap,
                    Padding = new Thickness(12, 12, 12, 12),
                    VerticalAlignment = VerticalAlignment.Top
                };
                var scroll = new ScrollViewer { Content = text, Padding = new Thickness(8) };
                var tabItem = new TabItem
                {
                    Header = CreateTabHeader(tabTitle, tabId, node),
                    Content = scroll,
                    Tag = tabId,
                    ContextMenu = CreateTabContextMenu(tabId),
                    Style = (Style)FindResource("AppTabItemStyle")
                };
                TabsControl.Items.Add(tabItem);
                TabsControl.SelectedItem = tabItem;
                _tabIdToNodeId[tabId] = node.Id;
            }
            return;
        }
    }

    private void OpenSshPuttyTab(string tabId, string tabTitle, Node node,
        string host, int port, string username, string? password, string? keyPath, string? keyPassphrase, bool useAgent = false,
        List<JumpHop>? jumpChain = null)
    {
        var puttyControl = new SshPuttyHostControl();
        puttyControl.Closed += (_, _) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_tabIdToPuttyControl.ContainsKey(tabId))
                    CloseTab(tabId);
            });
        };

        var hostWpf = new WindowsFormsHost { Child = puttyControl };
        hostWpf.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        hostWpf.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
        var statusBar = new SshStatusBarControl();
        statusBar.UpdateStats(false, null, null, null, null, null, null);
        statusBar.GetProcessTrafficCommandCallback = async () =>
        {
            if (!_tabIdToSshStatsParams.TryGetValue(tabId, out var p))
                return RemoteOsInfoService.GetProcessTrafficCommand(null);
            var output = await SessionManager.RunSshCommandAsync(
                p.host, (ushort)p.port, p.username, p.password, p.keyPath, p.keyPassphrase, p.jumpChain, p.useAgent,
                RemoteOsInfoService.DetectionCommand, CancellationToken.None);
            var osInfo = RemoteOsInfoService.ParseDetectionOutput(output);
            return RemoteOsInfoService.GetProcessTrafficCommand(osInfo);
        };
        statusBar.GetLargestFilesCommandCallback = async () =>
        {
            if (!_tabIdToSshStatsParams.TryGetValue(tabId, out var p))
                return RemoteOsInfoService.GetLargestFilesCommand(null);
            var output = await SessionManager.RunSshCommandAsync(
                p.host, (ushort)p.port, p.username, p.password, p.keyPath, p.keyPassphrase, p.jumpChain, p.useAgent,
                RemoteOsInfoService.DetectionCommand, CancellationToken.None);
            var osInfo = RemoteOsInfoService.ParseDetectionOutput(output);
            return RemoteOsInfoService.GetLargestFilesCommand(osInfo);
        };
        var dock = new DockPanel();
        DockPanel.SetDock(statusBar, Dock.Bottom);
        dock.Children.Add(statusBar);
        dock.Children.Add(hostWpf);
        var tabItem = new TabItem
        {
            Header = CreateTabHeader(tabTitle, tabId, node),
            Content = dock,
            Tag = tabId,
            ContextMenu = CreateTabContextMenu(tabId),
            Style = (Style)FindResource("AppTabItemStyle")
        };
        TabsControl.Items.Add(tabItem);
        TabsControl.SelectedItem = tabItem;
        _tabIdToPuttyControl[tabId] = puttyControl;
        _tabIdToNodeId[tabId] = node.Id;
        _tabIdToSshStatusBar[tabId] = statusBar;
        _tabIdToSshStatsParams[tabId] = (host, port, username ?? "", password, keyPath, keyPassphrase, jumpChain, useAgent);
        puttyControl.Connected += (_, _) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                _remoteFileNodeId = node.Id;
                _remoteFilePath = ".";
                RemotePathBox.Text = ".";
                RemoteFileTitle.Text = "远程文件 - " + node.Name;
                LoadRemoteFileList();
                if (_tabIdToSshStatsParams.TryGetValue(tabId, out var p))
                    StartSshStatusBarPolling(tabId, p.host, (ushort)p.port, p.username, p.password, p.keyPath, p.keyPassphrase, p.jumpChain, p.useAgent);
            });
        };

        // 延迟到 WPF 布局完成后再连接，确保 WindowsFormsHost/Panel 已获得正确尺寸，
        // 避免 PuTTY 以极小的窗口初始化导致终端列数过窄
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            try
            {
                puttyControl.Connect(host, port, username ?? "", password, keyPath, SshPuttyHostControl.DefaultPuttyPath, keyPassphrase, useAgent, jumpChain);
            }
            catch (Exception ex)
            {
                ExceptionLog.Write(ex, "PuTTY 启动失败", toCrashLog: false);
                MessageBox.Show("PuTTY 启动失败：" + ex.Message, "xOpenTerm");
                CloseTab(tabId);
            }
        }));
    }

    private StackPanel CreateTabHeader(string title, string tabId, Node? node = null)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        if (node != null)
        {
            bool isDisconnected = _tabIdToDisconnected.TryGetValue(tabId, out var disconnected) && disconnected;
            string iconText = ServerTreeItemBuilder.NodeIcon(node, isGroupExpanded: true);

            // 使用Grid来叠加图标和x
            var iconContainer = new Grid
            {
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            // 原始图标
            var iconBlock = new TextBlock
            {
                Text = iconText,
                Foreground = ServerTreeItemBuilder.NodeColor(node),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            iconContainer.Children.Add(iconBlock);

            // 如果断开连接，添加x覆盖
            if (isDisconnected)
            {
                var xBlock = new TextBlock
                {
                    Text = "✕",
                    Foreground = System.Windows.Media.Brushes.Red,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                iconContainer.Children.Add(xBlock);
            }

            panel.Children.Add(iconContainer);
        }
        panel.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var closeBtn = new Button
        {
            Tag = tabId,
            Style = (System.Windows.Style)Application.Current.Resources["TabCloseButtonStyle"]
        };
        closeBtn.Click += (s, _) =>
        {
            if (s is Button b && b.Tag is string id)
                CloseTabWithConfirm(id);
        };
        panel.Children.Add(closeBtn);
        return panel;
    }

    private ContextMenu CreateTabContextMenu(string tabId)
    {
        var menu = new ContextMenu();
        var reconnectItem = new MenuItem { Header = "重连(_R)" };
        reconnectItem.Click += (_, _) => ReconnectTab(tabId);
        var disconnectItem = new MenuItem { Header = "断开(_D)" };
        disconnectItem.Click += (_, _) => DisconnectTab(tabId);
        var closeAllItem = new MenuItem { Header = "关闭所有连接(_A)" };
        closeAllItem.Click += (_, _) => CloseAllTabs();
        menu.Items.Add(reconnectItem);
        menu.Items.Add(disconnectItem);
        menu.Items.Add(closeAllItem);
        return menu;
    }

    private void DisconnectTab(string tabId)
    {
        if (_tabIdToRdpSession.TryGetValue(tabId, out var rdpSession))
        {
            rdpSession.Disconnect();
            return;
        }
        if (_tabIdToPuttyControl.TryGetValue(tabId, out var putty))
        {
            if (TryGetTabItem(tabId) is not TabItem tabItem)
                return;
            _tabIdToPuttyControl.Remove(tabId);
            StopSshStatusBarPolling(tabId);
            if (_tabIdToNodeId.TryGetValue(tabId, out var nid) && nid == _remoteFileNodeId)
            {
                _remoteFileNodeId = null;
                RemoteFileList.ItemsSource = null;
                RemoteFileTitle.Text = "远程文件";
            }
            var placeholder = CreateDisconnectedPlaceholder();
            if (_tabIdToSshStatusBar.TryGetValue(tabId, out var statusBar))
            {
                var dock = new DockPanel();
                DockPanel.SetDock(statusBar, Dock.Bottom);
                dock.Children.Add(statusBar);
                dock.Children.Add(placeholder);
                tabItem.Content = dock;
            }
            else
                tabItem.Content = placeholder;
            _disconnectedPuttyTabIds.Add(tabId);
            putty.Close();
            return;
        }
        _sessionManager.CloseSession(tabId);
    }

    private static Border CreateDisconnectedPlaceholder()
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x29, 0x3b)),
            Child = new TextBlock
            {
                Text = "连接已断开。右键选择「重连」。",
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xa3, 0xb8)),
                FontSize = 14,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            }
        };
        return border;
    }

    private TabItem? TryGetTabItem(string tabId)
    {
        foreach (var item in TabsControl.Items)
            if (item is TabItem ti && ti.Tag is string id && id == tabId)
                return ti;
        return null;
    }

    private void ReconnectPuttyTabInPlace(string tabId, Node node)
    {
        try
        {
            var (host, port, username, password, keyPath, keyPassphrase, jumpChain, useAgent) =
                ConfigResolver.ResolveSsh(node, _nodes, _credentials, _tunnels);

            if (TryGetTabItem(tabId) is not TabItem tabItem)
                return;

            var puttyControl = new SshPuttyHostControl();
            puttyControl.Closed += (_, _) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (_tabIdToPuttyControl.ContainsKey(tabId))
                        CloseTab(tabId);
                });
            };
            var hostWpfReconnect = new WindowsFormsHost { Child = puttyControl };
            hostWpfReconnect.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            hostWpfReconnect.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
            var statusBarReconnect = new SshStatusBarControl();
            statusBarReconnect.UpdateStats(false, null, null, null, null, null, null);
            statusBarReconnect.GetProcessTrafficCommandCallback = async () =>
            {
                if (!_tabIdToSshStatsParams.TryGetValue(tabId, out var p))
                    return RemoteOsInfoService.GetProcessTrafficCommand(null);
                var output = await SessionManager.RunSshCommandAsync(
                    p.host, (ushort)p.port, p.username, p.password, p.keyPath, p.keyPassphrase, p.jumpChain, p.useAgent,
                    RemoteOsInfoService.DetectionCommand, CancellationToken.None);
                var osInfo = RemoteOsInfoService.ParseDetectionOutput(output);
                return RemoteOsInfoService.GetProcessTrafficCommand(osInfo);
            };
            statusBarReconnect.GetLargestFilesCommandCallback = async () =>
            {
                if (!_tabIdToSshStatsParams.TryGetValue(tabId, out var p))
                    return RemoteOsInfoService.GetLargestFilesCommand(null);
                var output = await SessionManager.RunSshCommandAsync(
                    p.host, (ushort)p.port, p.username, p.password, p.keyPath, p.keyPassphrase, p.jumpChain, p.useAgent,
                    RemoteOsInfoService.DetectionCommand, CancellationToken.None);
                var osInfo = RemoteOsInfoService.ParseDetectionOutput(output);
                return RemoteOsInfoService.GetLargestFilesCommand(osInfo);
            };
            var dockReconnect = new DockPanel();
            DockPanel.SetDock(statusBarReconnect, Dock.Bottom);
            dockReconnect.Children.Add(statusBarReconnect);
            dockReconnect.Children.Add(hostWpfReconnect);
            tabItem.Content = dockReconnect;
            _tabIdToPuttyControl[tabId] = puttyControl;
            _tabIdToSshStatusBar[tabId] = statusBarReconnect;
            _tabIdToSshStatsParams[tabId] = (host, port, username ?? "", password, keyPath, keyPassphrase, jumpChain, useAgent);
            puttyControl.Connected += (_, _) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    _remoteFileNodeId = node.Id;
                    _remoteFilePath = ".";
                    RemotePathBox.Text = ".";
                    RemoteFileTitle.Text = "远程文件 - " + node.Name;
                    LoadRemoteFileList();
                    if (_tabIdToSshStatsParams.TryGetValue(tabId, out var p))
                        StartSshStatusBarPolling(tabId, p.host, (ushort)p.port, p.username, p.password, p.keyPath, p.keyPassphrase, p.jumpChain, p.useAgent);
                });
            };
            _disconnectedPuttyTabIds.Remove(tabId);
            TabsControl.SelectedItem = tabItem;

            // 延迟到布局完成后连接，确保 Panel 尺寸正确
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                try
                {
                    puttyControl.Connect(host, port, username ?? "", password, keyPath, SshPuttyHostControl.DefaultPuttyPath, keyPassphrase, useAgent, jumpChain);
                }
                catch (Exception ex)
                {
                    ExceptionLog.Write(ex, "PuTTY 重连失败", toCrashLog: false);
                    MessageBox.Show("重连失败：" + ex.Message, "xOpenTerm");
                }
            }));
        }
        catch (Exception ex)
        {
            ExceptionLog.Write(ex, "重连失败", toCrashLog: false);
            MessageBox.Show("重连失败：" + ex.Message, "xOpenTerm");
        }
    }

    private void ReconnectTab(string tabId)
    {
        if (!_tabIdToNodeId.TryGetValue(tabId, out var nodeId))
            return;
        var node = _nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node == null)
            return;

        // 更新连接状态为未断开
        if (_tabIdToDisconnected.ContainsKey(tabId))
            _tabIdToDisconnected[tabId] = false;
        // 更新标签页图标
        UpdateTabHeaderIcon(tabId);

        if (_tabIdToRdpSession.TryGetValue(tabId, out var rdpSession))
        {
            // RDP 嵌入会话无法原地重连，关闭当前 tab 后重新打开同一节点
            CloseTab(tabId);
            OpenTab(node!);
            return;
        }
        if (_disconnectedPuttyTabIds.Contains(tabId))
        {
            ReconnectPuttyTabInPlace(tabId, node);
            return;
        }
        if (_tabIdToPuttyControl.TryGetValue(tabId, out var _))
        {
            CloseTab(tabId);
            OpenTab(node);
        }
    }

    private void OpenRdpTab(Node node)
    {
        try
        {
            // 关闭 RDP 后 COM 需时间释放，短时内再打开则延迟执行以减轻“COM 已分离”错误
            var elapsed = DateTime.UtcNow - _lastRdpCloseUtc;
            if (elapsed < TimeSpan.FromSeconds(1.2))
            {
                var delayMs = (int)Math.Max(100, (1.2 - elapsed.TotalSeconds) * 1000);
                var nodeToOpen = node;
                Task.Run(async () =>
                {
                    await Task.Delay(delayMs);
                    await Dispatcher.InvokeAsync(() => OpenRdpTab(nodeToOpen));
                });
                return;
            }

            var (host, port, username, domain, password, rdpOptions) = ConfigResolver.ResolveRdp(node, _nodes, _credentials);
            if (string.IsNullOrWhiteSpace(host))
            {
                MessageBox.Show("请填写 RDP 主机地址。", "xOpenTerm");
                return;
            }
            if (string.IsNullOrEmpty(password))
            {
                MessageBox.Show("未填写密码或未选择登录凭证，内嵌 RDP 无法自动登录。请填写密码或选择凭证后再连接，或使用右键菜单「使用 mstsc 打开」在外部连接并手动输入密码。", "xOpenTerm");
                return;
            }

            var sameCount = _tabIdToNodeId.Values.Count(id => id == node.Id);
            var displayName = string.IsNullOrEmpty(node.Name) ? host : node.Name;
            var tabTitle = sameCount == 0 ? displayName : $"{displayName} ({sameCount + 1})";
            var tabId = "rdp-" + DateTime.UtcNow.Ticks;

            var panel = new System.Windows.Forms.Panel { Dock = DockStyle.Fill, MinimumSize = new System.Drawing.Size(400, 300) };
            var session = new RdpEmbeddedSession(host, port, username, domain, password, rdpOptions, panel, SynchronizationContext.Current!);
            session.ErrorOccurred += (_, msg) =>
            {
                Dispatcher.Invoke(() => MessageBox.Show(msg, "xOpenTerm"));
            };
            session.Disconnected += (_, _) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (_tabIdToRdpSession.ContainsKey(tabId))
                        CloseTab(tabId);
                });
            };
            session.Connected += (_, _) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (_tabIdToRdpStatsParams.TryGetValue(tabId, out var p))
                        StartRdpStatusBarPolling(tabId, p.host, (ushort)p.port, p.username, p.password);
                });
            };

            var hostWpf = new WindowsFormsHost { Child = panel };
            hostWpf.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            hostWpf.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
            var statusBar = new SshStatusBarControl();
            statusBar.UpdateStats(false, null, null, null, null, null, null);
            var dock = new DockPanel();
            DockPanel.SetDock(statusBar, Dock.Bottom);
            dock.Children.Add(statusBar);
            dock.Children.Add(hostWpf);
            var tabItem = new TabItem
            {
                Header = CreateTabHeader(tabTitle, tabId, node),
                Content = dock,
                Tag = tabId,
                ContextMenu = CreateTabContextMenu(tabId),
                Style = (Style)FindResource("AppTabItemStyle")
            };
            TabsControl.Items.Add(tabItem);
            TabsControl.SelectedItem = tabItem;
            _tabIdToRdpSession[tabId] = session;
            _tabIdToRdpStatusBar[tabId] = statusBar;
            _tabIdToRdpStatsParams[tabId] = (host, port, username, password);
            _tabIdToNodeId[tabId] = node.Id;

            session.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "xOpenTerm");
        }
    }

    /// <summary>若已无任何连接 tab 对应该 node，则清除该 node 的远程文件缓存并关闭其 SFTP 长连接。</summary>
    private void ClearRemoteFileCacheIfNoTabsForNode(string? nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return;
        if (_tabIdToNodeId.Values.Any(id => id == nodeId)) return;
        _remoteFileCacheByNodeId.Remove(nodeId);
        _remoteFileCurrentPathByNodeId.Remove(nodeId);
        SftpSessionManager.ClearSession(nodeId);
    }

    /// <summary>关闭连接 tab 前提示用户，确认后再执行关闭。</summary>
    private void CloseTabWithConfirm(string tabId)
    {
        if (MessageBox.Show("确定要关闭此连接吗？", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        CloseTab(tabId);
    }

    /// <summary>关闭所有连接 tab，确认后依次关闭。</summary>
    private void CloseAllTabs()
    {
        if (TabsControl.Items.Count == 0) return;
        if (MessageBox.Show("确定要关闭所有连接吗？", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        var tabIds = new List<string>();
        foreach (var item in TabsControl.Items)
        {
            if (item is TabItem ti && ti.Tag is string id)
                tabIds.Add(id);
        }
        foreach (var tabId in tabIds)
            CloseTab(tabId);
    }

    private void CloseTab(string tabId)
    {
        StopSshStatusBarPolling(tabId);
        _tabIdToSshStatusBar.Remove(tabId);
        _tabIdToRdpStatusBar.Remove(tabId);
        _tabIdToSshStatsParams.Remove(tabId);
        _tabIdToRdpStatsParams.Remove(tabId);
        _tabIdToDisconnected.Remove(tabId);

        if (_disconnectedPuttyTabIds.Contains(tabId))
        {
            _disconnectedPuttyTabIds.Remove(tabId);
            var nodeId = _tabIdToNodeId.TryGetValue(tabId, out var nid) ? nid : null;
            _tabIdToNodeId.Remove(tabId);
            ClearRemoteFileCacheIfNoTabsForNode(nodeId);
            RemoveTabItem(tabId);
            return;
        }
        if (_tabIdToRdpSession.TryGetValue(tabId, out var rdpSession))
        {
            rdpSession.Close();
            _lastRdpCloseUtc = DateTime.UtcNow;
            _tabIdToRdpSession.Remove(tabId);
            var nodeId = _tabIdToNodeId.TryGetValue(tabId, out var nid) ? nid : null;
            _tabIdToNodeId.Remove(tabId);
            ClearRemoteFileCacheIfNoTabsForNode(nodeId);
            RemoveTabItem(tabId);
            return;
        }

        if (_tabIdToPuttyControl.TryGetValue(tabId, out var putty))
        {
            putty.Close();
            _tabIdToPuttyControl.Remove(tabId);
            var nodeId = _tabIdToNodeId.TryGetValue(tabId, out var nid) ? nid : null;
            if (nodeId != null && nodeId == _remoteFileNodeId)
            {
                _remoteFileNodeId = null;
                RemoteFileList.ItemsSource = null;
                RemoteFileTitle.Text = "远程文件";
            }
            _tabIdToNodeId.Remove(tabId);
            ClearRemoteFileCacheIfNoTabsForNode(nodeId);
            RemoveTabItem(tabId);
            return;
        }

        var termNodeId = _tabIdToNodeId.TryGetValue(tabId, out var tnid) ? tnid : null;
        _sessionManager.CloseSession(tabId);
        _tabIdToNodeId.Remove(tabId);
        ClearRemoteFileCacheIfNoTabsForNode(termNodeId);
        RemoveTabItem(tabId);
    }

    private void RemoveTabItem(string tabId)
    {
        var removedIndex = -1;
        for (var i = 0; i < TabsControl.Items.Count; i++)
        {
            if (TabsControl.Items[i] is TabItem ti && ti.Tag is string id && id == tabId)
            {
                removedIndex = i;
                TabsControl.Items.RemoveAt(i);
                break;
            }
        }
        if (removedIndex < 0) return;

        if (TabsControl.Items.Count == 0)
        {
            if (LeftTabControl.SelectedIndex != 0)
                LeftTabControl.SelectedIndex = 0;
            return;
        }
        // 按右>左顺序选下一个：优先选被关闭 tab 右侧的 tab，否则选左侧
        var nextIndex = removedIndex < TabsControl.Items.Count ? removedIndex : Math.Max(0, removedIndex - 1);
        TabsControl.SelectedIndex = nextIndex;
        SyncRemoteFileToCurrentConnectionTab();
    }

    /// <summary>若左侧已激活远程文件 tab，则把远程文件面板切换为当前连接 tab 对应的远程文件（当前连接为 SSH 时）。有缓存则恢复，否则从根路径加载。</summary>
    private void SyncRemoteFileToCurrentConnectionTab()
    {
        if (LeftTabControl.SelectedIndex != 1) return;
        if (TabsControl.SelectedItem is not TabItem tabItem || tabItem.Tag is not string tabId) return;
        if (!_tabIdToNodeId.TryGetValue(tabId, out var nodeId)) return;
        var node = _nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node?.Type != NodeType.ssh)
        {
            if (_remoteFileNodeId == nodeId) return;
            _remoteFileNodeId = null;
            RemoteFileList.ItemsSource = null;
            RemoteFileTitle.Text = "远程文件";
            return;
        }
        _remoteFileNodeId = nodeId;
        RemoteFileTitle.Text = "远程文件 - " + node.Name;
        var path = _remoteFileCurrentPathByNodeId.TryGetValue(nodeId, out var p) ? p : ".";
        _remoteFilePath = path;
        RemotePathBox.Text = path;
        if (_remoteFileCacheByNodeId.TryGetValue(nodeId, out var pathDict) && pathDict.TryGetValue(path, out var cachedList))
        {
            RemoteFileList.ItemsSource = cachedList;
        }
        else
        {
            LoadRemoteFileList();
        }
    }

    private void TabsControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncRemoteFileToCurrentConnectionTab();
    }


    private void UpdateTabHeaderIcon(string tabId)
    {
        // 查找对应的标签页
        TabItem? tabItem = null;
        foreach (var item in TabsControl.Items)
        {
            if (item is TabItem ti && ti.Tag is string id && id == tabId)
            {
                tabItem = ti;
                break;
            }
        }
        if (tabItem == null) return;

        // 获取节点信息
        if (!_tabIdToNodeId.TryGetValue(tabId, out var nodeId)) return;
        var node = _nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node == null) return;

        // 获取当前标签页标题
        string title = "";
        if (tabItem.Header is StackPanel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is TextBlock tb && tb != panel.Children[0])
                {
                    title = tb.Text;
                    break;
                }
            }
        }

        // 更新标签页头部
        if (!string.IsNullOrEmpty(title))
        {
            tabItem.Header = CreateTabHeader(title, tabId, node);
        }
    }

    /// <summary>启动状态栏轮询：每 3 秒在远程执行统计命令并更新指定 tab 的状态栏。</summary>
    private void StartSshStatusBarPolling(string tabId,
        string host, ushort port, string username, string? password, string? keyPath, string? keyPassphrase,
        List<JumpHop>? jumpChain, bool useAgent)
    {
        if (_tabIdToStatsCts.TryGetValue(tabId, out var oldCts))
        {
            try { oldCts.Cancel(); } catch { }
            _tabIdToStatsCts.Remove(tabId);
        }
        var cts = new CancellationTokenSource();
        _tabIdToStatsCts[tabId] = cts;
        var token = cts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var output = await SessionManager.RunSshCommandAsync(
                        host, port, username, password, keyPath, keyPassphrase, jumpChain, useAgent,
                        SshStatsHelper.StatsCommand, token);
                    if (token.IsCancellationRequested) break;
                    var (cpu, mem, rxBps, txBps, tcp, udp) = SshStatsHelper.ParseStatsOutput(output);
                    if (!_tabIdToSshStatusBar.TryGetValue(tabId, out var bar)) break;
                    Dispatcher.Invoke(() =>
                    {
                        if (_tabIdToSshStatusBar.TryGetValue(tabId, out var b))
                            b.UpdateStats(true, cpu, mem, rxBps, txBps, tcp, udp);
                    });
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) when (ex.Message.Contains("Too many authentication failures", StringComparison.OrdinalIgnoreCase))
                {
                    Dispatcher.Invoke(() => MessageBox.Show("SSH 状态栏采集失败：认证尝试次数过多，服务器已断开。已停止重试。\n\n" + ex.Message, "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning));
                    break;
                }
                catch
                {
                    if (!_tabIdToSshStatusBar.TryGetValue(tabId, out var bar)) break;
                    Dispatcher.Invoke(() =>
                    {
                        if (_tabIdToSshStatusBar.TryGetValue(tabId, out var b))
                            b.UpdateStats(true, null, null, null, null, null, null);
                    });
                }
                try
                {
                    await Task.Delay(3000, token);
                }
                catch (OperationCanceledException) { break; }
            }
        }, token);

        // 磁盘占用率：每 3 分钟拉取一次（按分区）
        const int diskIntervalSeconds = 180;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2000, token);
            }
            catch (OperationCanceledException) { return; }
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var diskOutput = await SessionManager.RunSshCommandAsync(
                        host, port, username, password, keyPath, keyPassphrase, jumpChain, useAgent,
                        SshStatsHelper.DiskStatsCommand, token);
                    if (token.IsCancellationRequested) break;
                    var diskList = SshStatsHelper.ParseDiskStatsOutput(diskOutput);
                    if (!_tabIdToSshStatusBar.TryGetValue(tabId, out var bar)) break;
                    Dispatcher.Invoke(() =>
                    {
                        if (_tabIdToSshStatusBar.TryGetValue(tabId, out var b))
                            b.UpdateDiskStats(diskList);
                    });
                }
                catch (OperationCanceledException) { break; }
                catch
                {
                    if (_tabIdToSshStatusBar.TryGetValue(tabId, out var bar))
                        Dispatcher.Invoke(() => bar.UpdateDiskStats(null));
                }
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(diskIntervalSeconds), token);
                }
                catch (OperationCanceledException) { break; }
            }
        }, token);
    }

    /// <summary>停止指定 tab 的状态栏轮询并更新为未连接。</summary>
    private void StopSshStatusBarPolling(string tabId)
    {
        if (_tabIdToStatsCts.TryGetValue(tabId, out var cts))
        {
            try { cts.Cancel(); } catch { }
            _tabIdToStatsCts.Remove(tabId);
        }
        if (_tabIdToSshStatusBar.TryGetValue(tabId, out var bar))
        {
            bar.UpdateStats(false, null, null, null, null, null, null);
            bar.UpdateDiskStats(null);
        }
        if (_tabIdToRdpStatusBar.TryGetValue(tabId, out var rdpBar))
            rdpBar.UpdateStats(false, null, null, null, null, null, null);
    }

    /// <summary>为 RDP 标签页启动状态栏轮询（3 秒一次，远程执行统计命令并更新状态栏）。</summary>
    private void StartRdpStatusBarPolling(string tabId, string host, ushort port, string username, string? password)
    {
        if (_tabIdToStatsCts.TryGetValue(tabId, out var oldCts))
        {
            try { oldCts.Cancel(); } catch { }
            _tabIdToStatsCts.Remove(tabId);
        }
        var cts = new CancellationTokenSource();
        _tabIdToStatsCts[tabId] = cts;
        var token = cts.Token;
        _ = Task.Run(async () =>
        {
            bool hasTried = false;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // 使用 SSH 方式连接到 Windows 服务器执行命令
                    var output = await SessionManager.RunSshCommandAsync(
                        host, port, username, password, null, null, null, false,
                        RdpStatsHelper.StatsCommand, token);
                    if (token.IsCancellationRequested) break;
                    var (cpu, mem, tcp, udp) = RdpStatsHelper.ParseStatsOutput(output);
                    if (!_tabIdToRdpStatusBar.TryGetValue(tabId, out var bar)) break;
                    Dispatcher.Invoke(() =>
                    {
                        if (_tabIdToRdpStatusBar.TryGetValue(tabId, out var b))
                            b.UpdateStats(true, cpu, mem, null, null, tcp, udp);
                    });
                    hasTried = true;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) when (ex.Message.Contains("Too many authentication failures", StringComparison.OrdinalIgnoreCase))
                {
                    Dispatcher.Invoke(() => MessageBox.Show("SSH 状态栏采集失败：认证尝试次数过多，服务器已断开。已停止重试。\n\n" + ex.Message, "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning));
                    break;
                }
                catch
                {
                    if (!_tabIdToRdpStatusBar.TryGetValue(tabId, out var bar)) break;
                    Dispatcher.Invoke(() =>
                    {
                        if (_tabIdToRdpStatusBar.TryGetValue(tabId, out var b))
                        {
                            if (!hasTried)
                            {
                                // 第一次尝试失败，显示无SSH服务的提示
                                b.ShowRdpNoSshMessage();
                                hasTried = true;
                            }
                            else
                            {
                                // 后续尝试失败，保持灰色状态
                                b.UpdateStats(true, null, null, null, null, null, null);
                            }
                        }
                    });
                }
                try
                {
                    await Task.Delay(3000, token);
                }
                catch (OperationCanceledException) { break; }
            }
        }, token);
    }
}
