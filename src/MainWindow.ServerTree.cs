using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using xOpenTerm.Models;

namespace xOpenTerm;

/// <summary>主窗口：服务器树、节点 CRUD、拖拽排序。</summary>
public partial class MainWindow
{
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
        var textPrimary = (Brush)FindResource("TextPrimary");
        var textSecondary = (Brush)FindResource("TextSecondary");
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        var iconBlock = new TextBlock
        {
            Text = ServerTreeItemBuilder.NodeIcon(node, isGroupExpanded: true),
            Margin = new Thickness(0, 0, 6, 0),
            Foreground = ServerTreeItemBuilder.NodeColor(node)
        };
        if (node.Type == NodeType.ssh || node.Type == NodeType.rdp)
            iconBlock.FontFamily = new FontFamily("Segoe MDL2 Assets");
        header.Children.Add(iconBlock);
        var displayName = node.Type == NodeType.rdp && string.IsNullOrEmpty(node.Name) && !string.IsNullOrEmpty(node.Config?.Host)
            ? node.Config!.Host!
            : node.Name;
        header.Children.Add(new TextBlock
        {
            Text = displayName,
            Foreground = textPrimary,
            VerticalAlignment = VerticalAlignment.Center
        });
        if (node.Type == NodeType.ssh && !string.IsNullOrEmpty(node.Config?.Host))
        {
            header.Children.Add(new TextBlock
            {
                Text = " " + node.Config.Host,
                Foreground = textSecondary,
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
        if (node.Type == NodeType.group)
        {
            void UpdateGroupIcon()
            {
                iconBlock.Text = ServerTreeItemBuilder.NodeIcon(node, item.IsExpanded);
            }
            item.Expanded += (_, _) => UpdateGroupIcon();
            item.Collapsed += (_, _) => UpdateGroupIcon();
        }
        var children = _nodes.Where(n => n.ParentId == node.Id).ToList();
        foreach (var child in children)
            if (MatchesSearch(child))
                item.Items.Add(CreateTreeItem(child));
        return item;
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
        var node = _contextMenuNode;
        if (node == null)
            _treeContextMenu = BuildContextMenu(null);
        else
            _treeContextMenu = BuildContextMenu(node);
        if (_treeContextMenu != null)
            ServerTree.ContextMenu = _treeContextMenu;
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
        var name = string.IsNullOrEmpty(node.Name) ? "未命名分组" : node.Name;
        if (MessageBox.Show($"确定删除分组「{name}」及全部子节点？此操作不可恢复。", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        RemoveNodeRecursive(node.Id);
        _storage.SaveNodes(_nodes);
        BuildTree();
    }

    private void RemoveNodeRecursive(string nodeId)
    {
        foreach (var child in _nodes.Where(n => n.ParentId == nodeId).ToList())
            RemoveNodeRecursive(child.Id);
        _nodes.RemoveAll(n => n.Id == nodeId);
    }

    private void DeleteNode(Node node)
    {
        var name = string.IsNullOrEmpty(node.Name) && node.Config?.Host != null ? node.Config.Host : (node.Name ?? "未命名节点");
        if (MessageBox.Show($"确定删除节点「{name}」？", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
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
