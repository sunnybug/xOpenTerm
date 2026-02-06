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
        SetIsMultiSelected(item, _selectedNodeIds.Contains(node.Id));
        return item;
    }

    #region 多选附加属性

    public static readonly DependencyProperty IsMultiSelectedProperty = DependencyProperty.RegisterAttached(
        "IsMultiSelected", typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    public static void SetIsMultiSelected(DependencyObject d, bool value) => d.SetValue(IsMultiSelectedProperty, value);
    public static bool GetIsMultiSelected(DependencyObject d) => (bool)d.GetValue(IsMultiSelectedProperty);

    #endregion

    private void ServerTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        // 单击仅选中，不打开
    }

    private void ServerTree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ServerTree.SelectedItem is not TreeViewItem tvi || tvi.Tag is not Node node) return;
        if (e.Key == Key.F2)
        {
            e.Handled = true;
            StartInlineRename(tvi, node);
        }
        else if (e.Key == Key.Delete)
        {
            e.Handled = true;
            if (node.Type == NodeType.group)
                DeleteNodeRecursive(node);
            else
                DeleteNode(node);
        }
    }

    private static string GetNodeDisplayName(Node node)
    {
        return node.Type == NodeType.rdp && string.IsNullOrEmpty(node.Name) && !string.IsNullOrEmpty(node.Config?.Host)
            ? node.Config!.Host!
            : node.Name;
    }

    private void StartInlineRename(TreeViewItem tvi, Node node)
    {
        var displayName = GetNodeDisplayName(node);
        var originalHeader = tvi.Header;
        var textPrimary = (Brush)FindResource("TextPrimary");
        var bgInput = FindResource("BgInput");
        var borderBrush = FindResource("BorderBrush");
        var textBox = new System.Windows.Controls.TextBox
        {
            Text = displayName,
            Foreground = textPrimary,
            Background = bgInput as Brush ?? Brushes.Transparent,
            BorderBrush = borderBrush as Brush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 2, 4, 2),
            MinWidth = 120,
            VerticalAlignment = VerticalAlignment.Center
        };
        tvi.Header = textBox;
        textBox.Focus();
        textBox.SelectAll();

        void EndEdit(bool commit)
        {
            textBox.KeyDown -= OnKeyDown;
            textBox.LostFocus -= OnLostFocus;
            if (commit)
            {
                var newName = textBox.Text?.Trim() ?? "";
                if (!string.IsNullOrEmpty(newName))
                {
                    node.Name = newName;
                    _storage.SaveNodes(_nodes);
                    BuildTree();
                }
                else
                    tvi.Header = originalHeader;
            }
            else
                tvi.Header = originalHeader;
        }

        void OnKeyDown(object _, KeyEventArgs args)
        {
            if (args.Key == Key.Enter) { args.Handled = true; EndEdit(true); }
            else if (args.Key == Key.Escape) { args.Handled = true; EndEdit(false); }
        }

        void OnLostFocus(object _, RoutedEventArgs args)
        {
            EndEdit(true);
        }

        textBox.KeyDown += OnKeyDown;
        textBox.LostFocus += OnLostFocus;
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
        var tvi = FindClickedNode(e.OriginalSource);
        _contextMenuNode = tvi?.Tag as Node;
        if (tvi != null)
            tvi.IsSelected = true;
        else
        {
            var current = ServerTree.SelectedItem;
            if (current != null && ServerTree.ItemContainerGenerator.ContainerFromItem(current) is TreeViewItem container)
                container.IsSelected = false;
        }
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
            var importSub = new MenuItem { Header = "导入" };
            importSub.Items.Add(CreateMenuItem("导入 MobaXterm", () => ImportMobaXterm(node)));
            menu.Items.Add(importSub);
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("设置", () => EditGroupSettings(node)));
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

    private void EditGroupSettings(Node groupNode)
    {
        var dlg = new GroupSettingsWindow(groupNode, _nodes, _credentials, _tunnels, _storage);
        if (dlg.ShowDialog() == true)
        {
            var idx = _nodes.FindIndex(n => n.Id == groupNode.Id);
            if (idx >= 0) _nodes[idx] = groupNode;
            _storage.SaveNodes(_nodes);
            BuildTree();
        }
    }

    private void ImportMobaXterm(Node parentNode)
    {
        var dlg = new ImportMobaXtermWindow(this);
        if (dlg.ShowDialog() != true || dlg.SelectedSessions.Count == 0) return;
        // 按目录结构创建父节点：path -> 已创建的分组 Node
        var pathToGroupId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [""] = parentNode.Id };
        foreach (var item in dlg.SelectedSessions.OrderBy(s => s.FolderPath ?? ""))
        {
            var path = item.FolderPath ?? "";
            var parts = string.IsNullOrEmpty(path) ? Array.Empty<string>() : path.Split(new[] { " / " }, StringSplitOptions.None);
            var currentParentId = parentNode.Id;
            var currentPath = "";
            foreach (var segment in parts)
            {
                var name = segment.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                currentPath = string.IsNullOrEmpty(currentPath) ? name : currentPath + " / " + name;
                if (!pathToGroupId.TryGetValue(currentPath, out var groupId))
                {
                    var group = new Node
                    {
                        Id = Guid.NewGuid().ToString(),
                        ParentId = currentParentId,
                        Type = NodeType.group,
                        Name = name,
                        Config = null
                    };
                    _nodes.Add(group);
                    pathToGroupId[currentPath] = group.Id;
                    groupId = group.Id;
                }
                currentParentId = groupId;
            }
            var sessionNode = item.ToNode(currentParentId);
            _nodes.Add(sessionNode);
        }
        _storage.SaveNodes(_nodes);
        BuildTree();
    }

    private void ServerTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindClickedNode(e.OriginalSource);
        var node = item?.Tag as Node;
        _draggedNode = node;
        _dragStartPoint = e.GetPosition(null);

        if (node != null)
        {
            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (ctrl)
            {
                if (_selectedNodeIds.Contains(node.Id))
                    _selectedNodeIds.Remove(node.Id);
                else
                    _selectedNodeIds.Add(node.Id);
                if (_selectedNodeIds.Count == 0)
                    _lastSelectedNodeId = null;
                else
                    _lastSelectedNodeId = node.Id;
            }
            else if (shift)
            {
                if (string.IsNullOrEmpty(_lastSelectedNodeId))
                {
                    _selectedNodeIds.Clear();
                    _selectedNodeIds.Add(node.Id);
                }
                else
                {
                    var ordered = GetNodesInDisplayOrder();
                    var idxClick = ordered.FindIndex(n => n.Id == node.Id);
                    var idxAnchor = ordered.FindIndex(n => n.Id == _lastSelectedNodeId);
                    if (idxClick >= 0 && idxAnchor >= 0)
                    {
                        _selectedNodeIds.Clear();
                        var (i0, i1) = idxClick <= idxAnchor ? (idxClick, idxAnchor) : (idxAnchor, idxClick);
                        for (var i = i0; i <= i1; i++)
                            _selectedNodeIds.Add(ordered[i].Id);
                    }
                    else
                    {
                        _selectedNodeIds.Clear();
                        _selectedNodeIds.Add(node.Id);
                    }
                }
                _lastSelectedNodeId = node.Id;
            }
            else
            {
                _selectedNodeIds.Clear();
                _selectedNodeIds.Add(node.Id);
                _lastSelectedNodeId = node.Id;
            }
            if (item != null)
                item.IsSelected = true;
            UpdateTreeSelectionVisuals();
        }
    }

    /// <summary>树节点按界面显示顺序（深度优先）</summary>
    private List<Node> GetNodesInDisplayOrder()
    {
        var list = new List<Node>();
        void Add(Node n)
        {
            if (!MatchesSearch(n)) return;
            list.Add(n);
            foreach (var c in _nodes.Where(x => x.ParentId == n.Id).OrderBy(x => x.Name))
                Add(c);
        }
        foreach (var root in _nodes.Where(n => string.IsNullOrEmpty(n.ParentId)).OrderBy(n => n.Name))
            Add(root);
        return list;
    }

    private void UpdateTreeSelectionVisuals()
    {
        foreach (var tvi in EnumerateTreeViewItems(ServerTree))
        {
            if (tvi.Tag is Node n)
                SetIsMultiSelected(tvi, _selectedNodeIds.Contains(n.Id));
        }
    }

    private static IEnumerable<TreeViewItem> EnumerateTreeViewItems(ItemsControl parent)
    {
        for (var i = 0; i < parent.Items.Count; i++)
        {
            if (parent.ItemContainerGenerator.ContainerFromIndex(i) is TreeViewItem tvi)
            {
                yield return tvi;
                foreach (var child in EnumerateTreeViewItems(tvi))
                    yield return child;
            }
        }
    }

    private void ServerTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedNode == null) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStartPoint.X) < 4 && Math.Abs(pos.Y - _dragStartPoint.Y) < 4) return;
        try
        {
            var idsToDrag = _selectedNodeIds.Contains(_draggedNode.Id) && _selectedNodeIds.Count > 0
                ? _selectedNodeIds.ToList()
                : new List<string> { _draggedNode.Id };
            var payload = string.Join(",", idsToDrag);
            DragDrop.DoDragDrop(ServerTree, payload, DragDropEffects.Move);
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
        var payload = e.Data.GetData(DataFormats.Text) as string;
        if (string.IsNullOrEmpty(payload)) return;
        var draggedIds = payload.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        if (draggedIds.Count == 0) return;
        var target = GetNodeAtPosition(ServerTree, e.GetPosition(ServerTree));
        if (target == null || target.Type != NodeType.group) return;
        if (draggedIds.Any(id => id == target.Id || IsDescendant(target.Id, id)))
            return;
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void ServerTree_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.Text)) return;
        var payload = e.Data.GetData(DataFormats.Text) as string;
        if (string.IsNullOrEmpty(payload)) return;
        var draggedIds = payload.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        if (draggedIds.Count == 0) return;
        var target = GetNodeAtPosition(ServerTree, e.GetPosition(ServerTree));
        if (target == null || target.Type != NodeType.group) return;
        if (draggedIds.Any(id => id == target.Id || IsDescendant(target.Id, id)))
            return;
        var modified = false;
        foreach (var id in draggedIds)
        {
            var idx = _nodes.FindIndex(n => n.Id == id);
            if (idx >= 0)
            {
                _nodes[idx].ParentId = target.Id;
                modified = true;
            }
        }
        if (modified)
        {
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
