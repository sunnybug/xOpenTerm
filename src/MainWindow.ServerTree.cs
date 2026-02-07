using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>主窗口：服务器树、节点 CRUD、拖拽排序。</summary>
public partial class MainWindow
{
    private void BuildTree(bool expandNodes = true)
    {
        // 重建前收集当前已展开的分组节点 ID，避免新增/编辑/删除后其它父节点被意外展开
        var expandedIds = CollectExpandedGroupNodeIds(ServerTree);
        ServerTree.Items.Clear();
        var roots = _nodes.Where(n => string.IsNullOrEmpty(n.ParentId)).ToList();
        foreach (var node in roots)
            if (MatchesSearch(node))
                ServerTree.Items.Add(CreateTreeItem(node, expandedIds, expandNodes));
        // 重建后根据 _selectedNodeIds 恢复 TreeView 的选中项，避免键盘 Delete 误删父节点
        RestoreSelectionFromSelectedNodeIds();
    }

    /// <summary>根据 _selectedNodeIds 恢复 TreeView 选中项并展开祖先，使键盘操作作用在正确节点上。</summary>
    private void RestoreSelectionFromSelectedNodeIds()
    {
        if (_selectedNodeIds.Count == 0) return;
        var firstId = _selectedNodeIds.FirstOrDefault(id => _nodes.Any(n => n.Id == id));
        if (string.IsNullOrEmpty(firstId)) return;
        var tvi = FindTreeViewItemByNodeId(ServerTree, firstId);
        if (tvi == null) return;
        ExpandAncestors(tvi);
        tvi.IsSelected = true;
        tvi.BringIntoView();
    }

    private static TreeViewItem? FindTreeViewItemByNodeId(ItemsControl parent, string nodeId)
    {
        for (var i = 0; i < parent.Items.Count; i++)
        {
            if (parent.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem child) continue;
            if (child.Tag is Node n && n.Id == nodeId) return child;
            var found = FindTreeViewItemByNodeId(child, nodeId);
            if (found != null) return found;
        }
        return null;
    }

    private static void ExpandAncestors(TreeViewItem item)
    {
        for (var p = System.Windows.Media.VisualTreeHelper.GetParent(item) as DependencyObject; p != null; p = System.Windows.Media.VisualTreeHelper.GetParent(p))
        {
            if (p is TreeViewItem parentTvi)
                parentTvi.IsExpanded = true;
        }
    }

    private static HashSet<string>? CollectExpandedGroupNodeIds(ItemsControl tree)
    {
        // 树为空（如首次加载）时返回 null，沿用 defaultExpand；有节点时返回集合（可为空），空集合表示全部折叠
        if (tree.Items.Count == 0) return null;
        var set = new HashSet<string>();
        foreach (var tvi in EnumerateTreeViewItems(tree))
            if (tvi.IsExpanded && tvi.Tag is Node n && (n.Type == NodeType.group || n.Type == NodeType.tencentCloudGroup))
                set.Add(n.Id);
        return set;
    }

    private static bool ShouldExpand(Node node, HashSet<string>? expandedIds, bool defaultExpand)
    {
        if (node.Type != NodeType.group && node.Type != NodeType.tencentCloudGroup) return true;
        if (expandedIds != null) return expandedIds.Contains(node.Id);
        return defaultExpand;
    }

    private bool MatchesSearch(Node node)
    {
        if (string.IsNullOrWhiteSpace(_searchTerm)) return true;
        var term = _searchTerm.Trim().ToLowerInvariant();
        if (node.Name.ToLowerInvariant().Contains(term)) return true;
        if (node.Config?.Host?.ToLowerInvariant().Contains(term) == true) return true;
        if (node.Config?.Username?.ToLowerInvariant().Contains(term) == true) return true;
        return _nodes.Where(n => n.ParentId == node.Id).Any(MatchesSearch);
    }

    private void ServerSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _searchTerm = ServerSearchBox?.Text ?? "";
        BuildTree();
    }

    private TreeViewItem CreateTreeItem(Node node, HashSet<string>? expandedIds, bool defaultExpand)
    {
        var expand = ShouldExpand(node, expandedIds, defaultExpand);
        var textPrimary = (Brush)FindResource("TextPrimary");
        var textSecondary = (Brush)FindResource("TextSecondary");
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        var iconBlock = new TextBlock
        {
            Text = ServerTreeItemBuilder.NodeIcon(node, isGroupExpanded: expand),
            Margin = new Thickness(0, 0, 6, 0),
            Foreground = ServerTreeItemBuilder.NodeColor(node)
        };
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
            IsExpanded = expand
        };
        if (node.Type == NodeType.group || node.Type == NodeType.tencentCloudGroup)
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
                item.Items.Add(CreateTreeItem(child, expandedIds, defaultExpand));
        SetIsMultiSelected(item, _selectedNodeIds.Contains(node.Id));
        return item;
    }

    #region 多选附加属性

    public static readonly DependencyProperty IsMultiSelectedProperty = DependencyProperty.RegisterAttached(
        "IsMultiSelected", typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    public static void SetIsMultiSelected(DependencyObject d, bool value) => d.SetValue(IsMultiSelectedProperty, value);
    public static bool GetIsMultiSelected(DependencyObject d) => (bool)d.GetValue(IsMultiSelectedProperty);

    #endregion

    private void ServerTree_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        // 禁止选中节点时自动横向滚动到最右边，保持当前滚动位置
        e.Handled = true;
    }

    private void ServerTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        // 当 TreeView 内部因点击触发选中变化时，确保多选视觉同步
        if (_suppressTreeViewSelection) return;
        // 若不是 Ctrl/Shift 操作且有新选中项，同步多选高亮
        if (e.NewValue is TreeViewItem tvi && tvi.Tag is Node node)
        {
            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (!ctrl && !shift)
            {
                // 普通选中时，刷新视觉保持一致
                UpdateTreeSelectionVisuals();
            }
        }
    }

    private void ServerTree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 优先以 _selectedNodeIds 解析操作目标，避免 BuildTree 后 SelectedItem 错位导致误删父节点
        List<Node>? nodesToAct = null;
        if (_selectedNodeIds.Count > 0)
        {
            nodesToAct = _nodes.Where(n => _selectedNodeIds.Contains(n.Id)).ToList();
            if (nodesToAct.Count == 0) nodesToAct = null;
        }
        if (nodesToAct == null && ServerTree.SelectedItem is TreeViewItem tvi && tvi.Tag is Node single)
            nodesToAct = new List<Node> { single };

        if (nodesToAct == null || nodesToAct.Count == 0) return;

        if (e.Key == Key.F2)
        {
            if (nodesToAct.Count != 1) return;
            e.Handled = true;
            var node = nodesToAct[0];
            var targetTvi = FindTreeViewItemByNodeId(ServerTree, node.Id);
            if (targetTvi != null)
                StartInlineRename(targetTvi, node);
        }
        else if (e.Key == Key.Delete)
        {
            e.Handled = true;
            if (nodesToAct.Count > 1)
                DeleteSelected(nodesToAct);
            else
            {
                var node = nodesToAct[0];
                if (node.Type == NodeType.group || node.Type == NodeType.tencentCloudGroup)
                    DeleteNodeRecursive(node);
                else
                    DeleteNode(node);
            }
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
        // Ctrl/Shift 多选时，若右键点在已选中的某一项上，则显示多选菜单：删除/连接
        if (node != null && _selectedNodeIds.Count > 1 && _selectedNodeIds.Contains(node.Id))
        {
            var selectedNodes = _nodes.Where(n => _selectedNodeIds.Contains(n.Id)).ToList();
            _treeContextMenu = BuildContextMenuMultiSelect(selectedNodes);
        }
        else if (node == null)
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

    private ContextMenu BuildContextMenuMultiSelect(List<Node> selectedNodes)
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("连接", () => ConnectSelected(selectedNodes)));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("删除", () => DeleteSelected(selectedNodes)));
        return menu;
    }

    private void ConnectSelected(List<Node> selectedNodes)
    {
        foreach (var n in selectedNodes)
        {
            if (n.Type == NodeType.ssh || n.Type == NodeType.rdp)
                OpenTab(n);
        }
    }

    private void DeleteSelected(List<Node> selectedNodes)
    {
        if (selectedNodes.Count == 0) return;
        if (MessageBox.Show($"确定删除选中的 {selectedNodes.Count} 个节点？此操作不可恢复。", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        // 按深度从大到小删除，先子后父，避免重复移除
        var byDepth = selectedNodes.OrderByDescending(n => NodeDepth(n.Id)).ToList();
        foreach (var n in byDepth)
        {
            if (!_nodes.Any(x => x.Id == n.Id)) continue;
            if (n.Type == NodeType.group)
                RemoveNodeRecursive(n.Id);
            else
                _nodes.RemoveAll(x => x.Id == n.Id);
        }
        _storage.SaveNodes(_nodes);
        _selectedNodeIds.Clear();
        BuildTree();
        UpdateTreeSelectionVisuals();
    }

    private int NodeDepth(string nodeId)
    {
        var d = 0;
        var id = nodeId;
        while (true)
        {
            var n = _nodes.FirstOrDefault(x => x.Id == id);
            if (n == null || string.IsNullOrEmpty(n.ParentId)) return d;
            id = n.ParentId;
            d++;
        }
    }

    private ContextMenu BuildContextMenu(Node? node)
    {
        var menu = new ContextMenu();
        if (node == null)
        {
            menu.Items.Add(CreateMenuItem("新建分组", () => AddNode(NodeType.group, null)));
            menu.Items.Add(CreateMenuItem("新建分组 - 腾讯云", () => AddTencentCloudGroup(null)));
            menu.Items.Add(CreateMenuItem("新建主机", () => AddNode(NodeType.ssh, null)));
            return menu;
        }
        if (node.Type == NodeType.group)
        {
            menu.Items.Add(CreateMenuItem("新建分组", () => AddNode(NodeType.group, node.Id)));
            menu.Items.Add(CreateMenuItem("新建分组 - 腾讯云", () => AddTencentCloudGroup(node.Id)));
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
        else if (node.Type == NodeType.tencentCloudGroup)
        {
            menu.Items.Add(CreateMenuItem("新建分组", () => AddNode(NodeType.group, node.Id)));
            menu.Items.Add(CreateMenuItem("新建分组 - 腾讯云", () => AddTencentCloudGroup(node.Id)));
            menu.Items.Add(CreateMenuItem("新建主机", () => AddNode(NodeType.ssh, node.Id)));
            menu.Items.Add(new Separator());
            var importSub = new MenuItem { Header = "导入" };
            importSub.Items.Add(CreateMenuItem("导入 MobaXterm", () => ImportMobaXterm(node)));
            menu.Items.Add(importSub);
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("设置", () => EditGroupSettings(node)));
            menu.Items.Add(CreateMenuItem("同步", () => SyncTencentCloudGroup(node)));
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
            if (n.Type == NodeType.group || n.Type == NodeType.tencentCloudGroup)
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
        BuildTree(expandNodes: false); // 导入后不自动展开节点
    }

    private void AddTencentCloudGroup(string? parentId)
    {
        var dlg = new TencentCloudGroupAddWindow { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var groupNode = new Node
        {
            Id = Guid.NewGuid().ToString(),
            ParentId = parentId,
            Type = NodeType.tencentCloudGroup,
            Name = string.IsNullOrWhiteSpace(dlg.GroupName) ? "腾讯云" : dlg.GroupName.Trim(),
            Config = new ConnectionConfig
            {
                TencentSecretId = dlg.SecretId,
                TencentSecretKey = dlg.SecretKey
            }
        };
        _nodes.Add(groupNode);
        _storage.SaveNodes(_nodes);
        BuildTree();

        // 复用与右键「同步」完全相同的流程，避免新建组流程中的线程/时序差异导致跨线程访问 UI
        SyncTencentCloudGroup(groupNode);
    }

    /// <summary>根据腾讯云实例列表构建 机房→项目→服务器 节点树，所有服务器节点默认同父节点凭证。</summary>
    private static List<Node> BuildTencentCloudSubtree(string rootId, List<TencentCvmInstance> instances)
    {
        var result = new List<Node>();
        var regionIdToNode = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        var projectKeyToNode = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);

        foreach (var ins in instances.OrderBy(i => i.Region).ThenBy(i => i.ProjectId).ThenBy(i => i.InstanceName))
        {
            if (string.IsNullOrEmpty(ins.InstanceId)) continue;

            if (!regionIdToNode.TryGetValue(ins.Region ?? "", out var regionNode))
            {
                regionNode = new Node
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = rootId,
                    Type = NodeType.group,
                    Name = ins.RegionName ?? ins.Region ?? "",
                    Config = null
                };
                result.Add(regionNode);
                regionIdToNode[ins.Region ?? ""] = regionNode;
            }

            var projectKey = (ins.Region ?? "") + ":" + ins.ProjectId;
            if (!projectKeyToNode.TryGetValue(projectKey, out var projectNode))
            {
                projectNode = new Node
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = regionNode.Id,
                    Type = NodeType.group,
                    Name = "项目 " + ins.ProjectId,
                    Config = null
                };
                result.Add(projectNode);
                projectKeyToNode[projectKey] = projectNode;
            }

            var host = ins.PublicIp ?? ins.PrivateIp ?? "";
            if (string.IsNullOrEmpty(host)) continue;

            var serverNode = new Node
            {
                Id = Guid.NewGuid().ToString(),
                ParentId = projectNode.Id,
                Type = ins.IsWindows ? NodeType.rdp : NodeType.ssh,
                Name = string.IsNullOrEmpty(ins.InstanceName) ? ins.InstanceId : ins.InstanceName,
                Config = new ConnectionConfig
                {
                    Host = host,
                    Port = (ushort)(ins.IsWindows ? 3389 : 22),
                    ResourceId = ins.InstanceId,
                    AuthSource = AuthSource.parent
                }
            };
            result.Add(serverNode);
        }
        return result;
    }

    private void SyncTencentCloudGroup(Node groupNode)
    {
        if (groupNode.Config?.TencentSecretId == null || groupNode.Config?.TencentSecretKey == null)
        {
            MessageBox.Show("该腾讯云组未配置密钥，请先在「设置」中保存 SecretId/SecretKey。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var secretId = groupNode.Config.TencentSecretId;
        var secretKey = groupNode.Config.TencentSecretKey ?? "";
        var cts = new CancellationTokenSource();
        var syncWin = new TencentCloudSyncWindow(cts.Cancel) { Owner = this };
        syncWin.Show();
        syncWin.ReportProgress("正在拉取实例列表…", 0, 1);

        // 进度回调统一同步封送到同步窗口的 UI 线程，避免跨线程访问
        var progress = new Progress<(string message, int current, int total)>(p =>
        {
            syncWin.Dispatcher.Invoke(() => syncWin.ReportProgress(p.message, p.current, p.total));
        });

        List<TencentCvmInstance>? instances = null;
        Exception? runEx = null;
        var t = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                return TencentCloudService.ListInstances(secretId, secretKey, progress, cts.Token);
            }
            catch (Exception ex)
            {
                runEx = ex;
                return null;
            }
        });

        while (!t.IsCompleted && syncWin.IsLoaded && syncWin.IsVisible)
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                System.Windows.Threading.DispatcherPriority.Background,
                () => { });
            Thread.Sleep(50);
        }

        if (runEx != null || t.Result == null)
        {
            var errMsg = runEx != null ? GetFullExceptionMessage(runEx) : "拉取失败或已取消";
            syncWin.ReportResult(errMsg, false);
            return;
        }
        instances = t.Result;
        var cloudInstanceIds = instances.Select(i => i.InstanceId).Where(id => !string.IsNullOrEmpty(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 收集本组下所有带 ResourceId 的服务器节点
        var serverNodesUnderGroup = new List<Node>();
        void CollectServerNodes(string parentId)
        {
            foreach (var n in _nodes.Where(x => x.ParentId == parentId))
            {
                if (n.Type == NodeType.ssh || n.Type == NodeType.rdp)
                {
                    if (!string.IsNullOrEmpty(n.Config?.ResourceId))
                        serverNodesUnderGroup.Add(n);
                }
                else if (n.Type == NodeType.group || n.Type == NodeType.tencentCloudGroup)
                    CollectServerNodes(n.Id);
            }
        }
        CollectServerNodes(groupNode.Id);

        // 删除本地存在但云上已不存在的节点（先询问用户）
        var toRemove = serverNodesUnderGroup.Where(n => !cloudInstanceIds.Contains(n.Config!.ResourceId!)).ToList();
        if (toRemove.Count > 0)
        {
            if (MessageBox.Show($"云上已不存在以下 {toRemove.Count} 个实例，是否从本地树中删除？\n\n此操作不可恢复。", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                toRemove = new List<Node>();
            foreach (var n in toRemove)
                _nodes.RemoveAll(x => x.Id == n.Id);
        }

        // 现有实例 ID -> 实例信息（用于更新 IP 或新增）
        var instanceMap = instances.Where(i => !string.IsNullOrEmpty(i.InstanceId)).ToDictionary(i => i.InstanceId!, StringComparer.OrdinalIgnoreCase);
        var existingResourceIds = serverNodesUnderGroup.Where(n => cloudInstanceIds.Contains(n.Config?.ResourceId ?? "")).Select(n => n.Config!.ResourceId!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 更新已有节点的 Host
        foreach (var n in serverNodesUnderGroup.Where(n => _nodes.Any(x => x.Id == n.Id)))
        {
            var rid = n.Config?.ResourceId;
            if (string.IsNullOrEmpty(rid) || !instanceMap.TryGetValue(rid, out var ins)) continue;
            var newHost = ins.PublicIp ?? ins.PrivateIp ?? "";
            if (!string.IsNullOrEmpty(newHost) && n.Config!.Host != newHost)
                n.Config.Host = newHost;
        }

        // 新增云上有但本地没有的实例（按现有树结构插入到对应机房/项目下）
        var existingRegionNodes = _nodes.Where(x => x.ParentId == groupNode.Id && x.Type == NodeType.group).ToList();
        var existingProjectNodes = _nodes.Where(x => x.ParentId != null && existingRegionNodes.Any(r => r.Id == x.ParentId) && x.Type == NodeType.group).ToList();
        var regionByKey = existingRegionNodes.ToDictionary(n => n.Name ?? "", StringComparer.OrdinalIgnoreCase);
        var projectByKey = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in existingProjectNodes)
        {
            var regionNode = existingRegionNodes.FirstOrDefault(r => r.Id == p.ParentId);
            if (regionNode != null)
            {
                var pid = GetTencentProjectIdFromName(p.Name);
                projectByKey[(regionNode.Name ?? "") + ":" + pid] = p;
            }
        }

        var existingIds = CollectResourceIdsUnderGroup(groupNode.Id);
        var addedCount = 0;

        foreach (var ins in instances)
        {
            if (string.IsNullOrEmpty(ins.InstanceId) || existingIds.Contains(ins.InstanceId)) continue;
            existingIds.Add(ins.InstanceId);
            addedCount++;

            var regionName = ins.RegionName ?? ins.Region ?? "";
            if (!regionByKey.TryGetValue(regionName, out var regionNode))
            {
                regionNode = new Node
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = groupNode.Id,
                    Type = NodeType.group,
                    Name = ins.RegionName ?? ins.Region ?? "",
                    Config = null
                };
                _nodes.Add(regionNode);
                regionByKey[regionName] = regionNode;
            }

            var projectKey = regionName + ":" + ins.ProjectId;
            if (!projectByKey.TryGetValue(projectKey, out var projectNode))
            {
                projectNode = new Node
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = regionNode.Id,
                    Type = NodeType.group,
                    Name = "项目 " + ins.ProjectId,
                    Config = null
                };
                _nodes.Add(projectNode);
                projectByKey[projectKey] = projectNode;
            }

            var host = ins.PublicIp ?? ins.PrivateIp ?? "";
            if (string.IsNullOrEmpty(host)) continue;

            var serverNode = new Node
            {
                Id = Guid.NewGuid().ToString(),
                ParentId = projectNode.Id,
                Type = ins.IsWindows ? NodeType.rdp : NodeType.ssh,
                Name = string.IsNullOrEmpty(ins.InstanceName) ? ins.InstanceId : ins.InstanceName,
                Config = new ConnectionConfig
                {
                    Host = host,
                    Port = (ushort)(ins.IsWindows ? 3389 : 22),
                    ResourceId = ins.InstanceId,
                    AuthSource = AuthSource.parent
                }
            };
            _nodes.Add(serverNode);
        }

        _storage.SaveNodes(_nodes);
        syncWin.ReportResult($"已删除 {toRemove.Count} 个本地节点，新增 {addedCount} 台实例。", true);
        BuildTree(expandNodes: false);
    }

    private HashSet<string> CollectResourceIdsUnderGroup(string groupId)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in _nodes.Where(x => x.ParentId == groupId))
        {
            if ((n.Type == NodeType.ssh || n.Type == NodeType.rdp) && !string.IsNullOrEmpty(n.Config?.ResourceId))
                set.Add(n.Config!.ResourceId!);
            if (n.Type == NodeType.group || n.Type == NodeType.tencentCloudGroup)
            {
                foreach (var id in CollectResourceIdsUnderGroup(n.Id))
                    set.Add(id);
            }
        }
        return set;
    }

    /// <summary>拼接异常及内部异常信息，便于在同步窗口显示具体错误原因（如密钥错误、API 错误码等）。</summary>
    private static string GetFullExceptionMessage(Exception ex)
    {
        var msg = ex?.Message ?? "";
        if (ex?.InnerException != null)
        {
            var inner = ex.InnerException.Message ?? "";
            if (!string.IsNullOrEmpty(inner) && !msg.Contains(inner))
                msg = msg + " " + inner;
        }
        return string.IsNullOrWhiteSpace(msg) ? "拉取失败" : msg.Trim();
    }

    private static string GetTencentProjectIdFromName(string name)
    {
        var s = name.Replace("项目 ", "", StringComparison.Ordinal).Trim();
        return string.IsNullOrEmpty(s) ? "0" : s;
    }

    /// <summary>标记是否正在处理多选逻辑，用于抑制 SelectedItemChanged 中的干扰</summary>
    private bool _suppressTreeViewSelection;

    private void ServerTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindClickedNode(e.OriginalSource);
        var node = item?.Tag as Node;
        _draggedNode = node;
        _dragStartPoint = e.GetPosition(null);

        if (node == null) return;

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
            // 阻止 TreeView 默认的单选行为，保持多选状态
            e.Handled = true;
        }
        else if (shift)
        {
            // Shift 多选只对同一级（兄弟）节点有效，其他层级一律取消多选
            var siblings = _nodes.Where(n => n.ParentId == node.ParentId).OrderBy(n => n.Name).ToList();
            var anchorNode = string.IsNullOrEmpty(_lastSelectedNodeId) ? null : _nodes.FirstOrDefault(n => n.Id == _lastSelectedNodeId);
            var anchorInSameLevel = anchorNode != null && anchorNode.ParentId == node.ParentId;

            _selectedNodeIds.Clear();
            if (anchorInSameLevel)
            {
                var idxClick = siblings.FindIndex(n => n.Id == node.Id);
                var idxAnchor = siblings.FindIndex(n => n.Id == _lastSelectedNodeId);
                if (idxClick >= 0 && idxAnchor >= 0)
                {
                    var (i0, i1) = idxClick <= idxAnchor ? (idxClick, idxAnchor) : (idxAnchor, idxClick);
                    for (var i = i0; i <= i1; i++)
                        AddNodeWithDescendants(siblings[i].Id);
                }
                else
                    _selectedNodeIds.Add(node.Id);
            }
            else
                _selectedNodeIds.Add(node.Id);

            _lastSelectedNodeId = node.Id;
            // 阻止 TreeView 默认的单选行为，保持多选状态
            e.Handled = true;
        }
        else
        {
            // 普通点击（无修饰键）：单选
            _selectedNodeIds.Clear();
            _selectedNodeIds.Add(node.Id);
            _lastSelectedNodeId = node.Id;
        }

        if (item != null)
        {
            if (e.Handled)
            {
                // Ctrl/Shift 多选：手动聚焦但不触发 TreeView 的 SelectedItemChanged
                item.Focus();
                // 手动切换分组节点的展开/折叠（因为 e.Handled 阻止了默认行为）
                if (node.Type == NodeType.group && IsClickOnItemHeader(item, e))
                    item.IsExpanded = !item.IsExpanded;
            }
            else
            {
                _suppressTreeViewSelection = true;
                item.IsSelected = true;
                _suppressTreeViewSelection = false;
            }
        }
        UpdateTreeSelectionVisuals();
    }

    /// <summary>检查点击是否在 TreeViewItem 的 header 区域（而非展开箭头区域外）</summary>
    private static bool IsClickOnItemHeader(TreeViewItem item, MouseButtonEventArgs e)
    {
        // 只要点击位置在 TreeViewItem 内即可，展开箭头的处理由默认行为控制
        // 由于我们已 e.Handled，需要手动判断
        var pos = e.GetPosition(item);
        return pos.X >= 0 && pos.Y >= 0 && pos.X <= item.ActualWidth && pos.Y <= item.ActualHeight;
    }

    /// <summary>将节点及其所有子节点加入多选集合。</summary>
    private void AddNodeWithDescendants(string nodeId)
    {
        _selectedNodeIds.Add(nodeId);
        foreach (var child in _nodes.Where(n => n.ParentId == nodeId))
            AddNodeWithDescendants(child.Id);
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
        var selBg = (Brush)FindResource("SelectionBg");
        foreach (var tvi in EnumerateTreeViewItems(ServerTree))
        {
            if (tvi.Tag is Node n)
            {
                var selected = _selectedNodeIds.Contains(n.Id);
                SetIsMultiSelected(tvi, selected);
                // 直接设置 Background 以覆盖 HandyControl 模板的默认视觉
                tvi.Background = selected ? selBg : Brushes.Transparent;
            }
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
        // 拖到空白处视为拖到根节点
        if (target == null)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }
        if (target.Type != NodeType.group && target.Type != NodeType.tencentCloudGroup) return;
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
        string? parentId;
        if (target == null)
        {
            // 拖到空白处视为拖到根节点
            parentId = null;
        }
        else
        {
            if (target.Type != NodeType.group && target.Type != NodeType.tencentCloudGroup) return;
            if (draggedIds.Any(id => id == target.Id || IsDescendant(target.Id, id)))
                return;
            parentId = target.Id;
        }
        var modified = false;
        foreach (var id in draggedIds)
        {
            var idx = _nodes.FindIndex(n => n.Id == id);
            if (idx >= 0)
            {
                _nodes[idx].ParentId = parentId;
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
