using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Forms.Integration;
using xOpenTerm.Controls;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>主窗口：标签页与会话（SSH/RDP/本地终端）管理。</summary>
public partial class MainWindow
{
    private void OpenTab(Node node)
    {
        if (node.Type == NodeType.group || node.Type == NodeType.tencentCloudGroup || node.Type == NodeType.aliCloudGroup) return;
        if (node.Type == NodeType.rdp)
        {
            OpenRdpTab(node);
            return;
        }

        var sameCount = _tabIdToNodeId.Values.Count(id => id == node.Id);
        var tabTitle = sameCount == 0 ? node.Name : $"{node.Name} ({sameCount + 1})";
        var tabId = "tab-" + DateTime.UtcNow.Ticks;

        if (node.Type == NodeType.local)
        {
            OpenLocalOrSshTab(tabId, tabTitle, node, null);
            return;
        }

        if (node.Type == NodeType.ssh)
        {
            try
            {
                var (host, port, username, password, keyPath, keyPassphrase, jumpChain, useAgent) =
                    ConfigResolver.ResolveSsh(node, _nodes, _credentials, _tunnels);

                if (jumpChain == null || jumpChain.Count == 0)
                {
                    OpenSshPuttyTab(tabId, tabTitle, node, host, port, username, password, keyPath, useAgent);
                    return;
                }

                OpenLocalOrSshTab(tabId, tabTitle, node, (host, port, username, password, keyPath, keyPassphrase, jumpChain, useAgent));
            }
            catch (Exception ex)
            {
                var terminal = new TerminalControl();
                var tabItem = new TabItem
                {
                    Header = CreateTabHeader(tabTitle, tabId, node),
                    Content = terminal,
                    Tag = tabId,
                    ContextMenu = CreateTabContextMenu(tabId),
                    Style = (Style)FindResource("AppTabItemStyle")
                };
                TabsControl.Items.Add(tabItem);
                TabsControl.SelectedItem = tabItem;
                _tabIdToTerminal[tabId] = terminal;
                _tabIdToNodeId[tabId] = node.Id;
                terminal.Append("\r\n\x1b[31m" + ex.Message + "\x1b[0m\r\n");
            }
            return;
        }

        OpenLocalOrSshTab(tabId, tabTitle, node, null);
    }

    private void OpenSshPuttyTab(string tabId, string tabTitle, Node node,
        string host, int port, string username, string? password, string? keyPath, bool useAgent = false)
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
        _tabIdToSshStatsParams[tabId] = (host, port, username ?? "", password, keyPath, null, null, useAgent);
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
                puttyControl.Connect(host, port, username ?? "", password, keyPath, SshPuttyHostControl.DefaultPuttyPath, useAgent);
            }
            catch (Exception ex)
            {
                MessageBox.Show("PuTTY 启动失败：" + ex.Message, "xOpenTerm");
                CloseTab(tabId);
            }
        }));
    }

    private void OpenLocalOrSshTab(string tabId, string tabTitle, Node node,
        (string host, int port, string username, string? password, string? keyPath, string? keyPassphrase, List<JumpHop>? jumpChain, bool useAgent)? sshParams)
    {
        var terminal = new TerminalControl();
        terminal.DataToSend += (_, data) => _sessionManager.WriteToSession(tabId, data);

        System.Windows.FrameworkElement tabContent;
        if (sshParams != null)
        {
            var statusBar = new SshStatusBarControl();
            statusBar.UpdateStats(false, null, null, null, null, null, null);
            var dock = new DockPanel();
            DockPanel.SetDock(statusBar, Dock.Bottom);
            dock.Children.Add(statusBar);
            dock.Children.Add(terminal);
            tabContent = dock;
            _tabIdToSshStatusBar[tabId] = statusBar;
        }
        else
        {
            tabContent = terminal;
        }

        var tabItem = new TabItem
        {
            Header = CreateTabHeader(tabTitle, tabId, node),
            Content = tabContent,
            Tag = tabId,
            ContextMenu = CreateTabContextMenu(tabId),
            Style = (Style)FindResource("AppTabItemStyle")
        };
        TabsControl.Items.Add(tabItem);
        TabsControl.SelectedItem = tabItem;
        _tabIdToTerminal[tabId] = terminal;
        _tabIdToNodeId[tabId] = node.Id;

        if (sshParams == null)
        {
            terminal.Append("\x1b[32m正在连接...\x1b[0m\r\n");
            var protocol = node.Config?.Protocol ?? Protocol.powershell;
            var protocolStr = protocol == Protocol.cmd ? "cmd" : "powershell";
            _sessionManager.CreateLocalSession(tabId, node.Id, protocolStr, err =>
            {
                Dispatcher.Invoke(() => terminal.Append("\r\n\x1b[31m" + err + "\x1b[0m\r\n"));
            });
            return;
        }

        terminal.Append("\x1b[32m正在连接...\x1b[0m\r\n");
        var (host, port, username, password, keyPath, keyPassphrase, jumpChain, useAgent) = sshParams.Value;
        Task.Run(() =>
        {
            try
            {
                _sessionManager.CreateSshSession(tabId, node.Id, host, (ushort)port, username, password, keyPath, keyPassphrase, jumpChain, useAgent, err =>
                {
                    Dispatcher.BeginInvoke(() => terminal.Append("\r\n\x1b[31m" + err + "\x1b[0m\r\n"));
                });
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(() => terminal.Append("\r\n\x1b[31m" + ex.Message + "\x1b[0m\r\n"));
            }
        });
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
        var reconnectItem = new MenuItem { Header = "[R] 重连" };
        reconnectItem.Click += (_, _) => ReconnectTab(tabId);
        var disconnectItem = new MenuItem { Header = "[D] 断开" };
        disconnectItem.Click += (_, _) => DisconnectTab(tabId);
        var closeItem = new MenuItem { Header = "[W] 关闭" };
        closeItem.Click += (_, _) => CloseTabWithConfirm(tabId);
        menu.Items.Add(reconnectItem);
        menu.Items.Add(disconnectItem);
        menu.Items.Add(closeItem);
        return menu;
    }

    private void DisconnectTab(string tabId)
    {
        if (_tabIdToRdpControl.TryGetValue(tabId, out var rdp))
        {
            rdp.Disconnect();
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
                Text = "连接已断开。右键选择「重连」或「关闭」。",
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
            if (jumpChain != null && jumpChain.Count > 0)
            {
                CloseTab(tabId);
                OpenTab(node);
                return;
            }

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
                    puttyControl.Connect(host, port, username ?? "", password, keyPath, SshPuttyHostControl.DefaultPuttyPath, useAgent);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("重连失败：" + ex.Message, "xOpenTerm");
                }
            }));
        }
        catch (Exception ex)
        {
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

        if (_tabIdToRdpControl.TryGetValue(tabId, out var rdp))
        {
            rdp.Connect();
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
            return;
        }
        if (!_tabIdToTerminal.TryGetValue(tabId, out var terminal))
            return;

        _sessionManager.CloseSession(tabId);
        if (node.Type == NodeType.local)
        {
            terminal.Append("\x1b[32m正在连接...\x1b[0m\r\n");
            var protocol = node.Config?.Protocol ?? Protocol.powershell;
            var protocolStr = protocol == Protocol.cmd ? "cmd" : "powershell";
            _sessionManager.CreateLocalSession(tabId, node.Id, protocolStr, err =>
            {
                Dispatcher.Invoke(() => terminal.Append("\r\n\x1b[31m" + err + "\x1b[0m\r\n"));
            });
            return;
        }
        if (node.Type == NodeType.ssh)
        {
            try
            {
                var (host, port, username, password, keyPath, keyPassphrase, jumpChain, useAgent) =
                    ConfigResolver.ResolveSsh(node, _nodes, _credentials, _tunnels);
                if (jumpChain == null || jumpChain.Count == 0)
                {
                    CloseTab(tabId);
                    OpenTab(node);
                    return;
                }

                terminal.Append("\x1b[32m正在连接...\x1b[0m\r\n");
                var (h, p, u, pw, kp, kpp, jc, ua) = (host, port, username, password, keyPath, keyPassphrase, jumpChain, useAgent);
                Task.Run(() =>
                {
                    try
                    {
                        _sessionManager.CreateSshSession(tabId, node.Id, h, (ushort)p, u, pw, kp, kpp, jc, ua, err =>
                        {
                            Dispatcher.BeginInvoke(() => terminal.Append("\r\n\x1b[31m" + err + "\x1b[0m\r\n"));
                        });
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.BeginInvoke(() => terminal.Append("\r\n\x1b[31m" + ex.Message + "\x1b[0m\r\n"));
                    }
                });
            }
            catch (Exception ex)
            {
                terminal.Append("\r\n\x1b[31m" + ex.Message + "\x1b[0m\r\n");
            }
            return;
        }
        terminal.Append("\x1b[32m正在连接...\x1b[0m\r\n");
        var protocol2 = node.Config?.Protocol ?? Protocol.powershell;
        var protocolStr2 = protocol2 == Protocol.cmd ? "cmd" : "powershell";
        _sessionManager.CreateLocalSession(tabId, node.Id, protocolStr2, err =>
        {
            Dispatcher.Invoke(() => terminal.Append("\r\n\x1b[31m" + err + "\x1b[0m\r\n"));
        });
    }

    private void OpenRdpTab(Node node)
    {
        try
        {
            var (host, port, username, domain, password) = ConfigResolver.ResolveRdp(node, _nodes, _credentials);
            if (string.IsNullOrWhiteSpace(host))
            {
                MessageBox.Show("请填写 RDP 主机地址。", "xOpenTerm");
                return;
            }

            var sameCount = _tabIdToNodeId.Values.Count(id => id == node.Id);
            var displayName = string.IsNullOrEmpty(node.Name) ? host : node.Name;
            var tabTitle = sameCount == 0 ? displayName : $"{displayName} ({sameCount + 1})";
            var tabId = "rdp-" + DateTime.UtcNow.Ticks;

            var rdpControl = new RdpHostControl(host, port, username, domain, password);
            rdpControl.ErrorOccurred += (_, msg) =>
            {
                Dispatcher.Invoke(() => MessageBox.Show(msg, "xOpenTerm"));
            };
            rdpControl.Disconnected += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (_tabIdToRdpControl.ContainsKey(tabId))
                    CloseTab(tabId);
            });
        };
        rdpControl.Connected += (_, _) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_tabIdToRdpStatsParams.TryGetValue(tabId, out var p))
                    StartRdpStatusBarPolling(tabId, p.host, (ushort)p.port, p.username, p.password);
            });
        };

            var hostWpf = new WindowsFormsHost { Child = rdpControl };
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
        _tabIdToRdpControl[tabId] = rdpControl;
        _tabIdToRdpStatusBar[tabId] = statusBar;
        _tabIdToRdpStatsParams[tabId] = (host, port, username, password);
        _tabIdToNodeId[tabId] = node.Id;

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                if (_tabIdToRdpControl.TryGetValue(tabId, out var rdp))
                    rdp.Connect();
            }));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "xOpenTerm");
        }
    }

    /// <summary>若已无任何连接 tab 对应该 node，则清除该 node 的远程文件缓存。</summary>
    private void ClearRemoteFileCacheIfNoTabsForNode(string? nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return;
        if (_tabIdToNodeId.Values.Any(id => id == nodeId)) return;
        _remoteFileCacheByNodeId.Remove(nodeId);
    }

    /// <summary>关闭连接 tab 前提示用户，确认后再执行关闭。</summary>
    private void CloseTabWithConfirm(string tabId)
    {
        if (MessageBox.Show("确定要关闭此连接吗？", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
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
        if (_tabIdToRdpControl.TryGetValue(tabId, out var rdp))
        {
            rdp.Disconnect();
            rdp.Dispose();
            _tabIdToRdpControl.Remove(tabId);
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
        _tabIdToTerminal.Remove(tabId);
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
        if (_remoteFileCacheByNodeId.TryGetValue(nodeId, out var cached))
        {
            _remoteFilePath = cached.Path;
            RemotePathBox.Text = cached.Path;
            RemoteFileList.ItemsSource = cached.List;
        }
        else
        {
            _remoteFilePath = ".";
            RemotePathBox.Text = ".";
            LoadRemoteFileList();
        }
    }

    private void TabsControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncRemoteFileToCurrentConnectionTab();
    }

    private void OnSessionDataReceived(object? sender, (string SessionId, string Data) e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_tabIdToTerminal.TryGetValue(e.SessionId, out var term))
                term.Append(e.Data);
        });
    }

    private void OnSessionClosed(object? sender, string sessionId)
    {
        Dispatcher.Invoke(() =>
        {
            if (_tabIdToTerminal.TryGetValue(sessionId, out var term))
                term.Append("\r\n\x1b[31m连接已关闭\x1b[0m\r\n");
            StopSshStatusBarPolling(sessionId);
            if (_remoteFileNodeId != null && _tabIdToNodeId.TryGetValue(sessionId, out var nodeId) && nodeId == _remoteFileNodeId)
            {
                _remoteFileNodeId = null;
                RemoteFileList.ItemsSource = null;
                RemoteFileTitle.Text = "远程文件";
            }
            // 更新连接状态为断开
            _tabIdToDisconnected[sessionId] = true;
            // 更新标签页图标
            UpdateTabHeaderIcon(sessionId);
        });
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

    private void OnSessionConnected(object? sender, string sessionId)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!_tabIdToNodeId.TryGetValue(sessionId, out var nodeId)) return;
            var node = _nodes.FirstOrDefault(n => n.Id == nodeId);
            if (node == null || node.Type != NodeType.ssh) return;
            _remoteFileNodeId = nodeId;
            _remoteFilePath = ".";
            RemotePathBox.Text = ".";
            RemoteFileTitle.Text = "远程文件 - " + node.Name;
            LoadRemoteFileList();
            // 内置 SSH 终端连接成功后启动状态栏轮询
            StartSshStatusBarPollingForTab(sessionId, node);
            // 更新连接状态为已连接
            if (_tabIdToDisconnected.ContainsKey(sessionId))
                _tabIdToDisconnected[sessionId] = false;
            // 更新标签页图标
            UpdateTabHeaderIcon(sessionId);
        });
    }

    /// <summary>为 SSH 标签页启动状态栏轮询（3 秒一次，远程执行统计命令并更新状态栏）。</summary>
    private void StartSshStatusBarPollingForTab(string tabId, Node node)
    {
        if (!_tabIdToSshStatusBar.TryGetValue(tabId, out var statusBar)) return;
        try
        {
            var (host, port, username, password, keyPath, keyPassphrase, jumpChain, useAgent) =
                ConfigResolver.ResolveSsh(node, _nodes, _credentials, _tunnels);
            StartSshStatusBarPolling(tabId, host, (ushort)port, username, password, keyPath, keyPassphrase, jumpChain, useAgent);
        }
        catch
        {
            statusBar.UpdateStats(true, null, null, null, null, null, null);
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
            bar.UpdateStats(false, null, null, null, null, null, null);
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
