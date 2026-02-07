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
        if (node.Type == NodeType.group || node.Type == NodeType.tencentCloudGroup) return;
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
                    Header = CreateTabHeader(tabTitle, tabId),
                    Content = terminal,
                    Tag = tabId,
                    ContextMenu = CreateTabContextMenu(tabId)
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
        puttyControl.Connected += (_, _) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                _remoteFileNodeId = node.Id;
                _remoteFilePath = ".";
                RemotePathBox.Text = ".";
                RemoteFileTitle.Text = "远程文件 - " + node.Name;
                LeftTabControl.SelectedIndex = 1;
                LoadRemoteFileList();
            });
        };

        var hostWpf = new WindowsFormsHost { Child = puttyControl };
        var tabItem = new TabItem
        {
            Header = CreateTabHeader(tabTitle, tabId),
            Content = hostWpf,
            Tag = tabId,
            ContextMenu = CreateTabContextMenu(tabId)
        };
        TabsControl.Items.Add(tabItem);
        TabsControl.SelectedItem = tabItem;
        _tabIdToPuttyControl[tabId] = puttyControl;
        _tabIdToNodeId[tabId] = node.Id;

        try
        {
            puttyControl.Connect(host, port, username ?? "", password, keyPath, SshPuttyHostControl.DefaultPuttyPath, useAgent);
        }
        catch (Exception ex)
        {
            MessageBox.Show("PuTTY 启动失败：" + ex.Message, "xOpenTerm");
            CloseTab(tabId);
        }
    }

    private void OpenLocalOrSshTab(string tabId, string tabTitle, Node node,
        (string host, int port, string username, string? password, string? keyPath, string? keyPassphrase, List<JumpHop>? jumpChain, bool useAgent)? sshParams)
    {
        var terminal = new TerminalControl();
        terminal.DataToSend += (_, data) => _sessionManager.WriteToSession(tabId, data);

        var tabItem = new TabItem
        {
            Header = CreateTabHeader(tabTitle, tabId),
            Content = terminal,
            Tag = tabId,
            ContextMenu = CreateTabContextMenu(tabId)
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

    private StackPanel CreateTabHeader(string title, string tabId)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var closeBtn = new Button
        {
            Content = "×",
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            FontSize = 14,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xa3, 0xb8)),
            Cursor = Cursors.Hand,
            Tag = tabId
        };
        closeBtn.Click += (s, _) =>
        {
            if (s is Button b && b.Tag is string id)
                CloseTab(id);
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
        closeItem.Click += (_, _) => CloseTab(tabId);
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
            if (_tabIdToNodeId.TryGetValue(tabId, out var nid) && nid == _remoteFileNodeId)
            {
                _remoteFileNodeId = null;
                RemoteFileList.ItemsSource = null;
                RemoteFileTitle.Text = "远程文件";
            }
            tabItem.Content = CreateDisconnectedPlaceholder();
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
            puttyControl.Connected += (_, _) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    _remoteFileNodeId = node.Id;
                    _remoteFilePath = ".";
                    RemotePathBox.Text = ".";
                    RemoteFileTitle.Text = "远程文件 - " + node.Name;
                    LeftTabControl.SelectedIndex = 1;
                    LoadRemoteFileList();
                });
            };

            tabItem.Content = new WindowsFormsHost { Child = puttyControl };
            _tabIdToPuttyControl[tabId] = puttyControl;
            _disconnectedPuttyTabIds.Remove(tabId);
            TabsControl.SelectedItem = tabItem;

            puttyControl.Connect(host, port, username ?? "", password, keyPath, SshPuttyHostControl.DefaultPuttyPath, useAgent);
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

            var hostWpf = new WindowsFormsHost { Child = rdpControl };
            var tabItem = new TabItem
            {
                Header = CreateTabHeader(tabTitle, tabId),
                Content = hostWpf,
                Tag = tabId,
                ContextMenu = CreateTabContextMenu(tabId)
            };
            TabsControl.Items.Add(tabItem);
            TabsControl.SelectedItem = tabItem;
            _tabIdToRdpControl[tabId] = rdpControl;
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

    private void CloseTab(string tabId)
    {
        if (_disconnectedPuttyTabIds.Contains(tabId))
        {
            _disconnectedPuttyTabIds.Remove(tabId);
            _tabIdToNodeId.Remove(tabId);
            RemoveTabItem(tabId);
            return;
        }
        if (_tabIdToRdpControl.TryGetValue(tabId, out var rdp))
        {
            rdp.Disconnect();
            rdp.Dispose();
            _tabIdToRdpControl.Remove(tabId);
            _tabIdToNodeId.Remove(tabId);
            RemoveTabItem(tabId);
            return;
        }

        if (_tabIdToPuttyControl.TryGetValue(tabId, out var putty))
        {
            putty.Close();
            _tabIdToPuttyControl.Remove(tabId);
            if (_tabIdToNodeId.TryGetValue(tabId, out var nodeId) && nodeId == _remoteFileNodeId)
            {
                _remoteFileNodeId = null;
                RemoteFileList.ItemsSource = null;
                RemoteFileTitle.Text = "远程文件";
            }
            _tabIdToNodeId.Remove(tabId);
            RemoveTabItem(tabId);
            return;
        }

        _sessionManager.CloseSession(tabId);
        _tabIdToTerminal.Remove(tabId);
        _tabIdToNodeId.Remove(tabId);
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
        var nextIndex = Math.Max(0, removedIndex - 1);
        TabsControl.SelectedIndex = nextIndex;
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
            if (_remoteFileNodeId != null && _tabIdToNodeId.TryGetValue(sessionId, out var nodeId) && nodeId == _remoteFileNodeId)
            {
                _remoteFileNodeId = null;
                RemoteFileList.ItemsSource = null;
                RemoteFileTitle.Text = "远程文件";
            }
        });
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
            LeftTabControl.SelectedIndex = 1;
            LoadRemoteFileList();
        });
    }
}
