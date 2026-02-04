using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using xOpenTerm2.Controls;
using xOpenTerm2.Models;
using xOpenTerm2.Services;

namespace xOpenTerm2;

public partial class MainWindow : Window
{
    private readonly StorageService _storage = new();
    private readonly SessionManager _sessionManager = new();
    private List<Node> _nodes = new();
    private List<Credential> _credentials = new();
    private List<Tunnel> _tunnels = new();
    private string _searchTerm = "";
    private readonly Dictionary<string, TerminalControl> _tabIdToTerminal = new();
    private readonly Dictionary<string, string> _tabIdToNodeId = new();
    private ContextMenu? _treeContextMenu;
    private Node? _contextMenuNode;
    private Node? _draggedNode;
    private Point _dragStartPoint;

    public MainWindow()
    {
        InitializeComponent();
        LoadData();
        BuildTree();
        _sessionManager.DataReceived += OnSessionDataReceived;
        _sessionManager.SessionClosed += OnSessionClosed;
        SearchBox.Text = "";
    }

    private void LoadData()
    {
        _nodes = _storage.LoadNodes();
        _credentials = _storage.LoadCredentials();
        _tunnels = _storage.LoadTunnels();
    }

    private void BuildTree()
    {
        ServerTree.Items.Clear();
        var roots = _nodes.Where(n => string.IsNullOrEmpty(n.ParentId)).ToList();
        foreach (var node in roots)
            if (MatchesSearch(node))
                ServerTree.Items.Add(CreateTreeItem(node));
    }

    private bool MatchesSearch(Node node)
    {
        if (string.IsNullOrWhiteSpace(_searchTerm)) return true;
        var term = _searchTerm.Trim().ToLowerInvariant();
        if (node.Name.ToLowerInvariant().Contains(term)) return true;
        if (node.Config?.Host?.ToLowerInvariant().Contains(term) == true) return true;
        return _nodes.Where(n => n.ParentId == node.Id).Any(MatchesSearch);
    }

    private TreeViewItem CreateTreeItem(Node node)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock
        {
            Text = NodeIcon(node),
            Margin = new Thickness(0, 0, 6, 0),
            Foreground = NodeColor(node)
        });
        header.Children.Add(new TextBlock
        {
            Text = node.Name,
            VerticalAlignment = VerticalAlignment.Center
        });
        if (node.Type == NodeType.ssh && !string.IsNullOrEmpty(node.Config?.Host))
        {
            header.Children.Add(new TextBlock
            {
                Text = " " + node.Config.Host,
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xa3, 0xb8)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        var item = new TreeViewItem
        {
            Header = header,
            Tag = node,
            IsExpanded = true
        };
        var children = _nodes.Where(n => n.ParentId == node.Id).ToList();
        foreach (var child in children)
            if (MatchesSearch(child))
                item.Items.Add(CreateTreeItem(child));
        return item;
    }

    private static string NodeIcon(Node n)
    {
        return n.Type switch
        {
            NodeType.group => "📁",
            NodeType.ssh => "🖥",
            NodeType.rdp => "🖥",
            _ => "⌨"
        };
    }

    private static Brush NodeColor(Node n)
    {
        return n.Type switch
        {
            NodeType.group => Brushes.Gold,
            NodeType.ssh => new SolidColorBrush(Color.FromRgb(0x60, 0xa5, 0xfa)),
            NodeType.rdp => new SolidColorBrush(Color.FromRgb(0xc0, 0x84, 0xfc)),
            _ => Brushes.LightGreen
        };
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchTerm = SearchBox.Text ?? "";
        BuildTree();
    }

    private void ServerTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        // 单击仅选中，不打开
    }

    private void ServerTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var item = FindClickedNode(e.OriginalSource);
        if (item?.Tag is Node node && node.Type != NodeType.group)
            OpenTab(node);
    }

    private void ServerTree_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _contextMenuNode = (FindClickedNode(e.OriginalSource)?.Tag as Node);
    }

    private void ServerTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var node = _contextMenuNode ?? (ServerTree.SelectedItem as TreeViewItem)?.Tag as Node;
        if (node == null)
        {
            _treeContextMenu = BuildContextMenu(null);
        }
        else
        {
            _treeContextMenu = BuildContextMenu(node);
        }
        if (_treeContextMenu != null)
        {
            ServerTree.ContextMenu = _treeContextMenu;
        }
    }

    private TreeViewItem? FindClickedNode(object source)
    {
        for (var p = source as DependencyObject; p != null; p = VisualTreeHelper.GetParent(p))
            if (p is TreeViewItem tvi)
                return tvi;
        return null;
    }

    private ContextMenu BuildContextMenu(Node? node)
    {
        var menu = new ContextMenu();
        if (node == null)
        {
            menu.Items.Add(CreateMenuItem("新建分组", () => AddNode(NodeType.group, null)));
            menu.Items.Add(CreateMenuItem("新建主机", () => AddNode(NodeType.ssh, null)));
            return menu;
        }
        if (node.Type == NodeType.group)
        {
            menu.Items.Add(CreateMenuItem("新建分组", () => AddNode(NodeType.group, node.Id)));
            menu.Items.Add(CreateMenuItem("新建主机", () => AddNode(NodeType.ssh, node.Id)));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("连接全部", () => ConnectAll(node.Id)));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("删除（含子节点）", () => DeleteNodeRecursive(node)));
        }
        else
        {
            menu.Items.Add(CreateMenuItem("连接", () => OpenTab(node)));
            menu.Items.Add(CreateMenuItem("复制节点", () => DuplicateNode(node)));
            menu.Items.Add(CreateMenuItem("设置", () => EditNode(node)));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("删除", () => DeleteNode(node)));
        }
        return menu;
    }

    private static MenuItem CreateMenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void AddNode(NodeType type, string? parentId)
    {
        var node = new Node
        {
            Id = Guid.NewGuid().ToString(),
            ParentId = parentId,
            Type = type,
            Name = type == NodeType.group ? "新分组" : "新主机",
            Config = type != NodeType.group ? new ConnectionConfig() : null
        };
        _nodes.Add(node);
        _storage.SaveNodes(_nodes);
        BuildTree();
        if (type != NodeType.group)
            EditNode(node);
    }

    private void ConnectAll(string groupId)
    {
        var leaves = GetLeafNodes(groupId);
        foreach (var node in leaves)
            OpenTab(node);
    }

    private List<Node> GetLeafNodes(string parentId)
    {
        var list = new List<Node>();
        foreach (var n in _nodes.Where(n => n.ParentId == parentId))
        {
            if (n.Type == NodeType.group)
                list.AddRange(GetLeafNodes(n.Id));
            else
                list.Add(n);
        }
        return list;
    }

    private void DeleteNodeRecursive(Node node)
    {
        foreach (var child in _nodes.Where(n => n.ParentId == node.Id).ToList())
            DeleteNodeRecursive(child);
        _nodes.RemoveAll(n => n.Id == node.Id);
        _storage.SaveNodes(_nodes);
        BuildTree();
    }

    private void DeleteNode(Node node)
    {
        _nodes.RemoveAll(n => n.Id == node.Id);
        _storage.SaveNodes(_nodes);
        BuildTree();
    }

    private void DuplicateNode(Node node)
    {
        var copy = new Node
        {
            Id = Guid.NewGuid().ToString(),
            ParentId = node.ParentId,
            Type = node.Type,
            Name = node.Name + " (副本)",
            Config = node.Config != null ? new ConnectionConfig
            {
                Host = node.Config.Host,
                Port = node.Config.Port,
                Username = node.Config.Username,
                AuthType = node.Config.AuthType,
                Password = node.Config.Password,
                KeyPath = node.Config.KeyPath,
                KeyPassphrase = node.Config.KeyPassphrase,
                Protocol = node.Config.Protocol,
                CredentialId = node.Config.CredentialId,
                AuthSource = node.Config.AuthSource,
                TunnelIds = node.Config.TunnelIds != null ? new List<string>(node.Config.TunnelIds) : null,
                TunnelId = node.Config.TunnelId,
                Domain = node.Config.Domain
            } : null
        };
        _nodes.Add(copy);
        _storage.SaveNodes(_nodes);
        BuildTree();
    }

    private void EditNode(Node node)
    {
        var dlg = new NodeEditWindow(node, _nodes, _credentials, _tunnels, _storage);
        if (dlg.ShowDialog() == true && dlg.SavedNode != null)
        {
            var idx = _nodes.FindIndex(n => n.Id == dlg.SavedNode.Id);
            if (idx >= 0) _nodes[idx] = dlg.SavedNode;
            else _nodes.Add(dlg.SavedNode);
            _storage.SaveNodes(_nodes);
            BuildTree();
        }
    }

    private void OpenTab(Node node)
    {
        if (node.Type == NodeType.group) return;
        if (node.Type == NodeType.rdp)
        {
            try
            {
                RdpLauncher.Launch(node);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "xOpenTerm2");
            }
            return;
        }

        var sameCount = _tabIdToNodeId.Values.Count(id => id == node.Id);
        var tabTitle = sameCount == 0 ? node.Name : $"{node.Name} ({sameCount + 1})";
        var tabId = "tab-" + DateTime.UtcNow.Ticks;

        var terminal = new TerminalControl();
        terminal.DataToSend += (_, data) => _sessionManager.WriteToSession(tabId, data);

        var tabItem = new TabItem
        {
            Header = CreateTabHeader(tabTitle, tabId),
            Content = terminal,
            Tag = tabId
        };
        TabsControl.Items.Add(tabItem);
        TabsControl.SelectedItem = tabItem;
        _tabIdToTerminal[tabId] = terminal;
        _tabIdToNodeId[tabId] = node.Id;

        terminal.Append("\x1b[32m正在连接...\x1b[0m\r\n");

        if (node.Type == NodeType.local)
        {
            var protocol = node.Config?.Protocol ?? Protocol.powershell;
            var protocolStr = protocol == Protocol.cmd ? "cmd" : "powershell";
            _sessionManager.CreateLocalSession(tabId, node.Id, protocolStr, err =>
            {
                Dispatcher.Invoke(() => terminal.Append("\r\n\x1b[31m" + err + "\x1b[0m\r\n"));
            });
        }
        else if (node.Type == NodeType.ssh)
        {
            try
            {
                var (host, port, username, password, keyPath, keyPassphrase, jumpChain) =
                    ConfigResolver.ResolveSsh(node, _nodes, _credentials, _tunnels);
                _sessionManager.CreateSshSession(tabId, node.Id, host, port, username, password, keyPath, keyPassphrase, jumpChain, err =>
                {
                    Dispatcher.Invoke(() => terminal.Append("\r\n\x1b[31m" + err + "\x1b[0m\r\n"));
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => terminal.Append("\r\n\x1b[31m" + ex.Message + "\x1b[0m\r\n"));
            }
        }
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

    private void CloseTab(string tabId)
    {
        _sessionManager.CloseSession(tabId);
        for (var i = 0; i < TabsControl.Items.Count; i++)
        {
            if (TabsControl.Items[i] is TabItem ti && ti.Tag is string id && id == tabId)
            {
                TabsControl.Items.RemoveAt(i);
                _tabIdToTerminal.Remove(tabId);
                _tabIdToNodeId.Remove(tabId);
                break;
            }
        }
    }

    private void OnSessionDataReceived(object? sender, (string SessionId, string Data) e)
    {
        Dispatcher.Invoke(() =>
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
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        foreach (var tabId in _tabIdToTerminal.Keys.ToList())
            _sessionManager.CloseSession(tabId);
        base.OnClosed(e);
    }

    private void MenuCredentials_Click(object sender, RoutedEventArgs e)
    {
        var win = new CredentialsWindow(this);
        win.ShowDialog();
        LoadData();
        BuildTree();
    }

    private void MenuTunnels_Click(object sender, RoutedEventArgs e)
    {
        var win = new TunnelManagerWindow(this);
        win.ShowDialog();
        LoadData();
        BuildTree();
    }

    private void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow(this).ShowDialog();
    }

    private void MenuUpdate_Click(object sender, RoutedEventArgs e)
    {
        new UpdateWindow(this).ShowDialog();
    }

    private void ServerTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindClickedNode(e.OriginalSource);
        _draggedNode = item?.Tag as Node;
        _dragStartPoint = e.GetPosition(null);
    }

    private void ServerTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedNode == null) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStartPoint.X) < 4 && Math.Abs(pos.Y - _dragStartPoint.Y) < 4) return;
        try
        {
            DragDrop.DoDragDrop(ServerTree, _draggedNode.Id, DragDropEffects.Move);
        }
        finally
        {
            _draggedNode = null;
        }
    }

    private void ServerTree_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        if (!e.Data.GetDataPresent(DataFormats.Text)) return;
        var draggedId = e.Data.GetData(DataFormats.Text) as string;
        if (string.IsNullOrEmpty(draggedId)) return;
        var target = GetNodeAtPosition(ServerTree, e.GetPosition(ServerTree));
        if (target == null || target.Type != NodeType.group || target.Id == draggedId || IsDescendant(target.Id, draggedId))
            return;
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void ServerTree_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.Text)) return;
        var draggedId = e.Data.GetData(DataFormats.Text) as string;
        if (string.IsNullOrEmpty(draggedId)) return;
        var target = GetNodeAtPosition(ServerTree, e.GetPosition(ServerTree));
        if (target == null || target.Type != NodeType.group || target.Id == draggedId || IsDescendant(target.Id, draggedId))
            return;
        var idx = _nodes.FindIndex(n => n.Id == draggedId);
        if (idx >= 0)
        {
            _nodes[idx].ParentId = target.Id;
            _storage.SaveNodes(_nodes);
            BuildTree();
        }
        e.Handled = true;
    }

    private Node? GetNodeAtPosition(DependencyObject container, Point pos)
    {
        var hit = VisualTreeHelper.HitTest(container as Visual ?? throw new InvalidOperationException(), pos);
        if (hit?.VisualHit == null) return null;
        for (var p = hit.VisualHit as DependencyObject; p != null; p = VisualTreeHelper.GetParent(p))
            if (p is TreeViewItem tvi && tvi.Tag is Node n)
                return n;
        return null;
    }

    private bool IsDescendant(string potentialAncestorId, string nodeId)
    {
        var current = _nodes.FirstOrDefault(n => n.Id == nodeId);
        while (current?.ParentId != null)
        {
            if (current.ParentId == potentialAncestorId) return true;
            current = _nodes.FirstOrDefault(n => n.Id == current!.ParentId);
        }
        return false;
    }
}
