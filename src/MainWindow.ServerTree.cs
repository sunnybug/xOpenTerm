using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    private void BuildTree(bool expandNodes = true, HashSet<string>? initialExpandedIds = null)
    {
        // 重建前收集当前已展开的分组节点 ID（或使用传入的初始展开状态，用于启动时恢复）
        var expandedIds = initialExpandedIds ?? CollectExpandedGroupNodeIds(ServerTree);
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
            if (tvi.IsExpanded && tvi.Tag is Node n && (n.Type == NodeType.group || n.Type == NodeType.tencentCloudGroup || n.Type == NodeType.aliCloudGroup || n.Type == NodeType.kingCloudGroup))
                set.Add(n.Id);
        return set;
    }

    private static bool ShouldExpand(Node node, HashSet<string>? expandedIds, bool defaultExpand)
    {
        if (node.Type != NodeType.group && node.Type != NodeType.tencentCloudGroup && node.Type != NodeType.aliCloudGroup && node.Type != NodeType.kingCloudGroup) return true;
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
        var displayName = GetNodeDisplayName(node);
        header.Children.Add(new TextBlock
        {
            Text = displayName ?? "",
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
        if (node.Type == NodeType.group || node.Type == NodeType.tencentCloudGroup || node.Type == NodeType.aliCloudGroup || node.Type == NodeType.kingCloudGroup)
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
                if (node.Type == NodeType.group || node.Type == NodeType.tencentCloudGroup || node.Type == NodeType.aliCloudGroup || node.Type == NodeType.kingCloudGroup)
                    DeleteNodeRecursive(node);
                else
                    DeleteNode(node);
            }
        }
    }

    /// <summary>节点显示名：RDP 无名称时用 Host；腾讯云项目节点去掉末尾 " (项目ID)" 仅显示项目名。</summary>
    private static string GetNodeDisplayName(Node node)
    {
        var raw = node.Type == NodeType.rdp && string.IsNullOrEmpty(node.Name) && !string.IsNullOrEmpty(node.Config?.Host)
            ? node.Config!.Host!
            : (node.Name ?? "");
        return StripTrailingProjectIdFromDisplay(raw);
    }

    /// <summary>显示时去掉末尾 " (数字)"，不显示项目 ID。</summary>
    private static string StripTrailingProjectIdFromDisplay(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var m = System.Text.RegularExpressions.Regex.Match(name, @"^(.+?)\s*\(\d+\)\s*$");
        return m.Success ? m.Groups[1].Value.TrimEnd() : name;
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
                    // 腾讯云项目节点：保留原节点名中的项目 ID，便于同步时匹配
                    var pid = GetTencentProjectIdFromName(node.Name);
                    if (pid != "0" && !newName.EndsWith(" (" + pid + ")", StringComparison.Ordinal))
                        newName = newName + " (" + pid + ")";
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
        if (item?.Tag is not Node node) return;
        // 分组与云组（腾讯云）双击仅展开/折叠；仅可连接节点（ssh/local/rdp）双击打开标签
        if (node.Type == NodeType.group || node.Type == NodeType.tencentCloudGroup || node.Type == NodeType.aliCloudGroup || node.Type == NodeType.kingCloudGroup)
            return;
        OpenTab(node);
    }

    private void ServerTree_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var tvi = FindClickedNode(e.OriginalSource);
        _contextMenuNode = tvi?.Tag as Node;
        // 仅记录右键目标节点，不修改任何选择状态
        e.Handled = true;
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
        menu.Items.Add(CreateMenuItem("[L] 连接", () => ConnectSelected(selectedNodes)));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("[D] 删除", () => DeleteSelected(selectedNodes)));
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

    /// <summary>当前节点自身或任意祖先是否为腾讯云组。仅沿父链向上查找，O(n) 建表 + O(深度)。</summary>
    private bool HasAncestorOrSelfCloudGroup(Node? node)
    {
        if (node == null) return false;
        var byId = _nodes.ToDictionary(n => n.Id);
        var id = node.Id;
        while (!string.IsNullOrEmpty(id) && byId.TryGetValue(id, out var n))
        {
            if (n.Type == NodeType.tencentCloudGroup || n.Type == NodeType.aliCloudGroup || n.Type == NodeType.kingCloudGroup)
                return true;
            id = n.ParentId;
        }
        return false;
    }

    private ContextMenu BuildContextMenu(Node? node)
    {
        var menu = new ContextMenu();
        if (node == null)
        {
            // 新建子菜单
            var newSub = new MenuItem { Header = "[N] 新建" };
            newSub.Items.Add(CreateMenuItem("[G] 分组", () => AddNode(NodeType.group, null)));
            newSub.Items.Add(CreateMenuItem("[T] 分组 - 腾讯云", () => AddTencentCloudGroup(null)));
            newSub.Items.Add(CreateMenuItem("[A] 分组 - 阿里云", () => AddAliCloudGroup(null)));
            newSub.Items.Add(CreateMenuItem("[K] 分组 - 金山云", () => AddKingCloudGroup(null)));
            newSub.Items.Add(CreateMenuItem("[H] 主机", () => AddNode(NodeType.ssh, null)));
            menu.Items.Add(newSub);
            menu.Items.Add(new Separator());
            // 导入子菜单
            var importSub = new MenuItem { Header = "[I] 导入" };
            importSub.Items.Add(CreateMenuItem("[Y] 导入 YAML", () => ImportYaml(null)));
            menu.Items.Add(importSub);
            // 导出子菜单
            var exportSub = new MenuItem { Header = "[E] 导出" };
            exportSub.Items.Add(CreateMenuItem("[Y] 导出 YAML", () => ExportYaml()));
            menu.Items.Add(exportSub);
            return menu;
        }
        if (node.Type == NodeType.group)
        {
            // 新建子菜单（云组及其任意层级子节点下不允许再嵌套云组）
            var newSub = new MenuItem { Header = "[N] 新建" };
            newSub.Items.Add(CreateMenuItem("[G] 分组", () => AddNode(NodeType.group, node.Id)));
            if (!HasAncestorOrSelfCloudGroup(node))
            {
                newSub.Items.Add(CreateMenuItem("[T] 分组 - 腾讯云", () => AddTencentCloudGroup(node.Id)));
                newSub.Items.Add(CreateMenuItem("[A] 分组 - 阿里云", () => AddAliCloudGroup(node.Id)));
                newSub.Items.Add(CreateMenuItem("[K] 分组 - 金山云", () => AddKingCloudGroup(node.Id)));
            }
            newSub.Items.Add(CreateMenuItem("[H] 主机", () => AddNode(NodeType.ssh, node.Id)));
            menu.Items.Add(newSub);
            menu.Items.Add(new Separator());
            // 导入子菜单
            var importSub = new MenuItem { Header = "[I] 导入" };
            importSub.Items.Add(CreateMenuItem("[M] 导入 MobaXterm", () => ImportMobaXterm(node)));
            importSub.Items.Add(CreateMenuItem("[Y] 导入 YAML", () => ImportYaml(node)));
            menu.Items.Add(importSub);
            // 导出子菜单
            var exportSub = new MenuItem { Header = "[E] 导出" };
            exportSub.Items.Add(CreateMenuItem("[Y] 导出 YAML", () => ExportYaml()));
            menu.Items.Add(exportSub);
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("[S] 设置", () => EditGroupSettings(node)));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("[A] 连接全部", () => ConnectAll(node.Id)));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("[X] 删除（含子节点）", () => DeleteNodeRecursive(node)));
        }
        else if (node.Type == NodeType.tencentCloudGroup)
        {
            // 新建子菜单（云组下不允许再嵌套任何云组）
            var newSub = new MenuItem { Header = "[N] 新建" };
            newSub.Items.Add(CreateMenuItem("[G] 分组", () => AddNode(NodeType.group, node.Id)));
            newSub.Items.Add(CreateMenuItem("[H] 主机", () => AddNode(NodeType.ssh, node.Id)));
            menu.Items.Add(newSub);
            menu.Items.Add(new Separator());
            // 导入子菜单
            var importSub = new MenuItem { Header = "[I] 导入" };
            importSub.Items.Add(CreateMenuItem("[M] 导入 MobaXterm", () => ImportMobaXterm(node)));
            importSub.Items.Add(CreateMenuItem("[Y] 导入 YAML", () => ImportYaml(node)));
            menu.Items.Add(importSub);
            // 导出子菜单
            var exportSub = new MenuItem { Header = "[E] 导出" };
            exportSub.Items.Add(CreateMenuItem("[Y] 导出 YAML", () => ExportYaml()));
            menu.Items.Add(exportSub);
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("[S] 设置", () => EditGroupSettings(node)));
            menu.Items.Add(CreateMenuItem("[Y] 同步", () => SyncTencentCloudGroup(node)));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("[A] 连接全部", () => ConnectAll(node.Id)));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("[X] 删除（含子节点）", () => DeleteNodeRecursive(node)));
        }
        else if (node.Type == NodeType.aliCloudGroup)
        {
            var newSub = new MenuItem { Header = "[N] 新建" };
            newSub.Items.Add(CreateMenuItem("[G] 分组", () => AddNode(NodeType.group, node.Id)));
            newSub.Items.Add(CreateMenuItem("[H] 主机", () => AddNode(NodeType.ssh, node.Id)));
            menu.Items.Add(newSub);
            menu.Items.Add(new Separator());
            var importSub = new MenuItem { Header = "[I] 导入" };
            importSub.Items.Add(CreateMenuItem("[M] 导入 MobaXterm", () => ImportMobaXterm(node)));
            importSub.Items.Add(CreateMenuItem("[Y] 导入 YAML", () => ImportYaml(node)));
            menu.Items.Add(importSub);
            var exportSub = new MenuItem { Header = "[E] 导出" };
            exportSub.Items.Add(CreateMenuItem("[Y] 导出 YAML", () => ExportYaml()));
            menu.Items.Add(exportSub);
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("[S] 设置", () => EditGroupSettings(node)));
            menu.Items.Add(CreateMenuItem("[Y] 同步", () => SyncAliCloudGroup(node)));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("[A] 连接全部", () => ConnectAll(node.Id)));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("[X] 删除（含子节点）", () => DeleteNodeRecursive(node)));
        }
        else if (node.Type == NodeType.kingCloudGroup)
        {
            var newSub = new MenuItem { Header = "[N] 新建" };
            newSub.Items.Add(CreateMenuItem("[G] 分组", () => AddNode(NodeType.group, node.Id)));
            newSub.Items.Add(CreateMenuItem("[H] 主机", () => AddNode(NodeType.ssh, node.Id)));
            menu.Items.Add(newSub);
            menu.Items.Add(new Separator());
            var importSub = new MenuItem { Header = "[I] 导入" };
            importSub.Items.Add(CreateMenuItem("[M] 导入 MobaXterm", () => ImportMobaXterm(node)));
            importSub.Items.Add(CreateMenuItem("[Y] 导入 YAML", () => ImportYaml(node)));
            menu.Items.Add(importSub);
            var exportSub = new MenuItem { Header = "[E] 导出" };
            exportSub.Items.Add(CreateMenuItem("[Y] 导出 YAML", () => ExportYaml()));
            menu.Items.Add(exportSub);
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("[S] 设置", () => EditGroupSettings(node)));
            menu.Items.Add(CreateMenuItem("[Y] 同步", () => SyncKingCloudGroup(node)));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("[A] 连接全部", () => ConnectAll(node.Id)));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("[X] 删除（含子节点）", () => DeleteNodeRecursive(node)));
        }
        else
        {
            menu.Items.Add(CreateMenuItem("[L] 连接", () => OpenTab(node)));
            menu.Items.Add(CreateMenuItem("[C] 克隆", () => DuplicateNode(node)));
            menu.Items.Add(CreateMenuItem("[S] 设置", () => EditNode(node)));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("[D] 删除", () => DeleteNode(node)));
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
            EditNode(node, isExistingNode: false);
    }

    private void ConnectAll(string groupId)
    {
        var leaves = GetLeafNodes(groupId);
        if (leaves.Count == 0)
        {
            MessageBox.Show("该分组下没有可连接的主机。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var groupNode = _nodes.FirstOrDefault(n => n.Id == groupId);
        var groupName = groupNode != null ? (GetNodeDisplayName(groupNode) ?? groupNode.Name ?? "未命名分组") : "分组";
        if (MessageBox.Show($"确定要连接分组「{groupName}」下的全部 {leaves.Count} 台主机吗？", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        foreach (var node in leaves)
            OpenTab(node);
    }

    private List<Node> GetLeafNodes(string parentId)
    {
        var list = new List<Node>();
        foreach (var n in _nodes.Where(n => n.ParentId == parentId))
        {
            if (n.Type == NodeType.group || n.Type == NodeType.tencentCloudGroup || n.Type == NodeType.aliCloudGroup || n.Type == NodeType.kingCloudGroup)
                list.AddRange(GetLeafNodes(n.Id));
            else
                list.Add(n);
        }
        return list;
    }

    private void DeleteNodeRecursive(Node node)
    {
        var name = string.IsNullOrEmpty(node.Name) ? "未命名分组" : (GetNodeDisplayName(node) ?? node.Name);
        if (string.IsNullOrWhiteSpace(name)) name = "未命名分组";
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
        var name = string.IsNullOrEmpty(node.Name) && node.Config?.Host != null ? node.Config.Host : (GetNodeDisplayName(node) ?? node.Name ?? "未命名节点");
        if (string.IsNullOrWhiteSpace(name)) name = "未命名节点";
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

    private void EditNode(Node node, bool isExistingNode = true)
    {
        NodeEditWindowBase? dlg = node.Type switch
        {
            NodeType.group => new GroupNodeEditWindow(node, _nodes, _credentials, _tunnels, _storage, isExistingNode),
            NodeType.tencentCloudGroup => new TencentCloudNodeEditWindow(node, _nodes, _credentials, _tunnels, _storage, isExistingNode),
            NodeType.aliCloudGroup => new AliCloudNodeEditWindow(node, _nodes, _credentials, _tunnels, _storage, isExistingNode),
            NodeType.kingCloudGroup => new KingCloudNodeEditWindow(node, _nodes, _credentials, _tunnels, _storage, isExistingNode),
            NodeType.ssh => new SshNodeEditWindow(node, _nodes, _credentials, _tunnels, _storage, isExistingNode),
            NodeType.local => new LocalNodeEditWindow(node, _nodes, _credentials, _tunnels, _storage, isExistingNode),
            NodeType.rdp => new RdpNodeEditWindow(node, _nodes, _credentials, _tunnels, _storage, isExistingNode),
            _ => null
        };
        if (dlg == null) return;
        dlg.Owner = this;
        if (dlg.ShowDialog() == true && dlg.SavedNode != null)
        {
            var idx = _nodes.FindIndex(n => n.Id == dlg.SavedNode!.Id);
            if (idx >= 0) _nodes[idx] = dlg.SavedNode;
            else _nodes.Add(dlg.SavedNode);
            _storage.SaveNodes(_nodes);
            BuildTree();
        }
    }

    private void EditGroupSettings(Node groupNode)
    {
        var dlg = new GroupSettingsWindow(groupNode, _nodes, _credentials, _tunnels, _storage) { Owner = this };
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

    /// <summary>收集节点树中所有被引用的凭证 ID（Config 与 Tunnel 跳转）。</summary>
    private static HashSet<string> CollectReferencedCredentialIds(IEnumerable<Node> nodes)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in nodes)
        {
            var c = n.Config;
            if (c == null) continue;
            if (!string.IsNullOrEmpty(c.CredentialId)) ids.Add(c.CredentialId);
            if (!string.IsNullOrEmpty(c.SshCredentialId)) ids.Add(c.SshCredentialId);
            if (!string.IsNullOrEmpty(c.RdpCredentialId)) ids.Add(c.RdpCredentialId);
            if (c.Tunnel != null)
            {
                foreach (var h in c.Tunnel)
                {
                    if (!string.IsNullOrEmpty(h.CredentialId)) ids.Add(h.CredentialId);
                }
            }
        }
        return ids;
    }

    private void ExportYaml()
    {
        var refIds = CollectReferencedCredentialIds(_nodes);
        var credentials = _credentials.Where(c => refIds.Contains(c.Id)).ToList();
        var data = new ExportYamlRoot
        {
            Version = 1,
            Nodes = _nodes,
            Credentials = credentials
        };
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出 YAML",
            Filter = "YAML 文件 (*.yaml)|*.yaml|所有文件 (*.*)|*.*",
            DefaultExt = "yaml",
            FileName = $"nodes-export-{DateTime.Now:MMyy-HHmm}.yaml"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var yaml = _storage.SerializeExport(data);
            File.WriteAllText(dlg.FileName, yaml);
            MessageBox.Show($"已导出 {_nodes.Count} 个节点、{credentials.Count} 个凭证。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportYaml(Node? parentNode)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入 YAML",
            Filter = "YAML 文件 (*.yaml)|*.yaml|所有文件 (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true || string.IsNullOrEmpty(dlg.FileName)) return;
        try
        {
            var yaml = File.ReadAllText(dlg.FileName);
            var data = _storage.DeserializeExport(yaml);
            if (data?.Nodes == null || data.Nodes.Count == 0)
            {
                MessageBox.Show("文件中没有节点数据。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var importedCreds = data.Credentials ?? new List<Credential>();
            var credIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var imp in importedCreds)
            {
                if (string.IsNullOrEmpty(imp.Id)) continue;
                var existing = _credentials.FirstOrDefault(c => CredentialContentEquals(c, imp));
                if (existing != null)
                {
                    credIdMap[imp.Id] = existing.Id;
                    continue;
                }
                var oldCredId = imp.Id;
                imp.Id = Guid.NewGuid().ToString();
                _credentials.Add(imp);
                credIdMap[oldCredId] = imp.Id;
            }
            string? parentIdForRoots = parentNode?.Id;
            // 云组祖先下不允许导入云组节点：过滤掉腾讯云组/阿里云组，并将其子节点挂到有效祖先下
            var underCloudGroup = parentNode != null && HasAncestorOrSelfCloudGroup(parentNode);
            var nodesToAdd = underCloudGroup
                ? data.Nodes.Where(n => n.Type != NodeType.tencentCloudGroup && n.Type != NodeType.aliCloudGroup && n.Type != NodeType.kingCloudGroup).ToList()
                : data.Nodes;
            var removedCloudGroupIds = underCloudGroup
                ? data.Nodes.Where(n => n.Type == NodeType.tencentCloudGroup || n.Type == NodeType.aliCloudGroup || n.Type == NodeType.kingCloudGroup).Select(n => n.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var importedById = data.Nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);

            var nodeIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in nodesToAdd)
                nodeIdMap[n.Id] = Guid.NewGuid().ToString();

            foreach (var n in nodesToAdd)
            {
                var oldParentId = n.ParentId;
                var effectiveOldParentId = oldParentId;
                while (!string.IsNullOrEmpty(effectiveOldParentId) && removedCloudGroupIds.Contains(effectiveOldParentId)
                    && importedById.TryGetValue(effectiveOldParentId, out var p))
                    effectiveOldParentId = p.ParentId;
                n.Id = nodeIdMap[n.Id];
                n.ParentId = string.IsNullOrEmpty(effectiveOldParentId) ? parentIdForRoots : nodeIdMap[effectiveOldParentId];
                RemapCredentialIdsInNode(n, credIdMap);
                _nodes.Add(n);
            }
            _storage.SaveNodes(_nodes);
            _storage.SaveCredentials(_credentials);
            BuildTree(expandNodes: false);
            var skipMsg = removedCloudGroupIds.Count > 0 ? $"，已跳过 {removedCloudGroupIds.Count} 个云组节点（云组下不允许嵌套云组）" : "";
            MessageBox.Show($"已导入 {nodesToAdd.Count} 个节点{skipMsg}；凭证：相同已忽略，不同已新增。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导入失败：{ex.Message}", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool CredentialContentEquals(Credential a, Credential b)
    {
        if (a.Name != b.Name || a.Username != b.Username || a.AuthType != b.AuthType) return false;
        if ((a.Password ?? "") != (b.Password ?? "")) return false;
        if ((a.KeyPath ?? "") != (b.KeyPath ?? "")) return false;
        if ((a.KeyPassphrase ?? "") != (b.KeyPassphrase ?? "")) return false;
        if ((a.AgentForwarding ?? false) != (b.AgentForwarding ?? false)) return false;
        if ((a.Tunnel == null) != (b.Tunnel == null)) return false;
        if (a.Tunnel != null && b.Tunnel != null)
        {
            if (a.Tunnel.Count != b.Tunnel.Count) return false;
            for (var i = 0; i < a.Tunnel.Count; i++)
            {
                var ha = a.Tunnel[i];
                var hb = b.Tunnel[i];
                if (ha.Host != hb.Host || ha.Username != hb.Username || ha.AuthType != hb.AuthType) return false;
                if ((ha.Password ?? "") != (hb.Password ?? "")) return false;
                if ((ha.KeyPath ?? "") != (hb.KeyPath ?? "")) return false;
                if ((ha.KeyPassphrase ?? "") != (hb.KeyPassphrase ?? "")) return false;
            }
        }
        return true;
    }

    private static void RemapCredentialIdsInNode(Node node, Dictionary<string, string> credIdMap)
    {
        var c = node.Config;
        if (c == null) return;
        if (!string.IsNullOrEmpty(c.CredentialId) && credIdMap.TryGetValue(c.CredentialId, out var id1)) c.CredentialId = id1;
        if (!string.IsNullOrEmpty(c.SshCredentialId) && credIdMap.TryGetValue(c.SshCredentialId, out var id2)) c.SshCredentialId = id2;
        if (!string.IsNullOrEmpty(c.RdpCredentialId) && credIdMap.TryGetValue(c.RdpCredentialId, out var id3)) c.RdpCredentialId = id3;
        if (c.Tunnel != null)
        {
            foreach (var h in c.Tunnel)
            {
                if (!string.IsNullOrEmpty(h.CredentialId) && credIdMap.TryGetValue(h.CredentialId, out var id4)) h.CredentialId = id4;
            }
        }
    }

    private void AddTencentCloudGroup(string? parentId)
    {
        if (!string.IsNullOrEmpty(parentId))
        {
            var parent = _nodes.FirstOrDefault(n => n.Id == parentId);
            if (parent != null && HasAncestorOrSelfCloudGroup(parent))
            {
                MessageBox.Show("云组下不允许再嵌套云组，请在其他分组下新建。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
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

    private void AddAliCloudGroup(string? parentId)
    {
        if (!string.IsNullOrEmpty(parentId))
        {
            var parent = _nodes.FirstOrDefault(n => n.Id == parentId);
            if (parent != null && HasAncestorOrSelfCloudGroup(parent))
            {
                MessageBox.Show("云组下不允许再嵌套云组，请在其他分组下新建。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        var dlg = new AliCloudGroupAddWindow { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var groupNode = new Node
        {
            Id = Guid.NewGuid().ToString(),
            ParentId = parentId,
            Type = NodeType.aliCloudGroup,
            Name = string.IsNullOrWhiteSpace(dlg.GroupName) ? "阿里云" : dlg.GroupName.Trim(),
            Config = new ConnectionConfig
            {
                AliAccessKeyId = dlg.AccessKeyId,
                AliAccessKeySecret = dlg.AccessKeySecret
            }
        };
        _nodes.Add(groupNode);
        _storage.SaveNodes(_nodes);
        BuildTree();
        SyncAliCloudGroup(groupNode);
    }

    /// <summary>根据阿里云 ECS/轻量 实例列表构建 地域→服务器 节点树。</summary>
    private static List<Node> BuildAliCloudSubtree(string rootId, List<AliEcsInstance> instances)
    {
        var result = new List<Node>();
        var regionIdToNode = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);

        foreach (var ins in instances.OrderBy(i => i.RegionId).ThenBy(i => i.InstanceName))
        {
            if (string.IsNullOrEmpty(ins.InstanceId)) continue;

            if (!regionIdToNode.TryGetValue(ins.RegionId ?? "", out var regionNode))
            {
                regionNode = new Node
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = rootId,
                    Type = NodeType.group,
                    Name = ins.RegionName ?? ins.RegionId ?? "",
                    Config = null
                };
                result.Add(regionNode);
                regionIdToNode[ins.RegionId ?? ""] = regionNode;
            }

            var host = ins.PublicIp ?? ins.PrivateIp ?? "";
            if (string.IsNullOrEmpty(host)) continue;

            var serverNode = new Node
            {
                Id = Guid.NewGuid().ToString(),
                ParentId = regionNode.Id,
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

    private void SyncAliCloudGroup(Node groupNode)
    {
        if (groupNode.Config?.AliAccessKeyId == null || groupNode.Config?.AliAccessKeySecret == null)
        {
            MessageBox.Show("该阿里云组未配置密钥，请先在「设置」中保存 AccessKeyId/AccessKeySecret。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var accessKeyId = groupNode.Config.AliAccessKeyId;
        var accessKeySecret = groupNode.Config.AliAccessKeySecret ?? "";
        var cts = new CancellationTokenSource();
        var syncWin = new AliCloudSyncWindow(cts.Cancel) { Owner = this };
        syncWin.Show();
        syncWin.ReportProgress("正在拉取实例列表…", 0, 1);

        var progress = new Progress<(string message, int current, int total)>(p =>
        {
            syncWin.Dispatcher.Invoke(() => syncWin.ReportProgress(p.message, p.current, p.total));
        });

        List<AliEcsInstance>? instances = null;
        Exception? runEx = null;
        var t = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                return AliCloudService.ListAllInstances(accessKeyId, accessKeySecret, progress, cts.Token);
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
        var cloudInstanceIds = instances.Where(i => !string.IsNullOrEmpty(i.InstanceId)).Select(i => i.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);

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
                else if (n.Type == NodeType.group || n.Type == NodeType.tencentCloudGroup || n.Type == NodeType.aliCloudGroup || n.Type == NodeType.kingCloudGroup)
                    CollectServerNodes(n.Id);
            }
        }
        CollectServerNodes(groupNode.Id);

        var toRemove = serverNodesUnderGroup.Where(n => !cloudInstanceIds.Contains(n.Config!.ResourceId!)).ToList();
        var removedDetail = new List<string>();
        if (toRemove.Count > 0)
        {
            if (MessageBox.Show($"云上已不存在以下 {toRemove.Count} 个实例，是否从本地树中删除？\n\n此操作不可恢复。", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                foreach (var n in toRemove)
                {
                    removedDetail.Add(n.Name ?? n.Config?.ResourceId ?? n.Id);
                    _nodes.RemoveAll(x => x.Id == n.Id);
                }
            }
            else
                toRemove = new List<Node>();
        }

        var instanceMap = instances.Where(i => !string.IsNullOrEmpty(i.InstanceId)).ToDictionary(i => i.InstanceId!, StringComparer.OrdinalIgnoreCase);
        var updatedDetail = new List<string>();
        foreach (var n in serverNodesUnderGroup.Where(n => _nodes.Any(x => x.Id == n.Id)))
        {
            var rid = n.Config?.ResourceId;
            if (string.IsNullOrEmpty(rid)) continue;
            if (instanceMap.TryGetValue(rid, out var aliIns))
            {
                var newHost = aliIns.PublicIp ?? aliIns.PrivateIp ?? "";
                if (!string.IsNullOrEmpty(newHost) && n.Config!.Host != newHost)
                {
                    var oldHost = n.Config.Host ?? "";
                    n.Config.Host = newHost;
                    updatedDetail.Add($"{n.Name ?? rid}: {oldHost} → {newHost}");
                }
            }
        }

        var existingRegionNodes = _nodes.Where(x => x.ParentId == groupNode.Id && x.Type == NodeType.group).ToList();
        var regionByKey = existingRegionNodes.ToDictionary(n => n.Name ?? "", StringComparer.OrdinalIgnoreCase);
        var existingIds = CollectResourceIdsUnderGroup(groupNode.Id);
        var addedCount = 0;
        var addedDetail = new List<string>();

        foreach (var ins in instances)
        {
            if (string.IsNullOrEmpty(ins.InstanceId) || existingIds.Contains(ins.InstanceId)) continue;
            existingIds.Add(ins.InstanceId);
            addedCount++;
            addedDetail.Add(string.IsNullOrEmpty(ins.InstanceName) ? ins.InstanceId! : $"{ins.InstanceName} ({ins.InstanceId})");

            var regionName = ins.RegionName ?? ins.RegionId ?? "";
            if (!regionByKey.TryGetValue(regionName, out var regionNode))
            {
                regionNode = new Node
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = groupNode.Id,
                    Type = NodeType.group,
                    Name = regionName,
                    Config = null
                };
                _nodes.Add(regionNode);
                regionByKey[regionName] = regionNode;
            }

            var host = ins.PublicIp ?? ins.PrivateIp ?? "";
            if (string.IsNullOrEmpty(host)) continue;

            var serverNode = new Node
            {
                Id = Guid.NewGuid().ToString(),
                ParentId = regionNode.Id,
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
        var detailLines = new List<string>();
        foreach (var s in removedDetail)
            detailLines.Add("[删除] " + s);
        foreach (var s in updatedDetail)
            detailLines.Add("[更新IP] " + s);
        foreach (var s in addedDetail)
            detailLines.Add("[新增] " + s);
        var summary = $"已删除 {removedDetail.Count} 个本地节点，更新 {updatedDetail.Count} 台 IP，新增 {addedCount} 台实例（共 {instances.Count} 台）。";
        syncWin.ReportResult(summary, true, detailLines.Count > 0 ? detailLines : null);
        _selectedNodeIds.Clear();
        _selectedNodeIds.Add(groupNode.Id);
        BuildTree(expandNodes: false);
    }

    private void AddKingCloudGroup(string? parentId)
    {
        if (!string.IsNullOrEmpty(parentId))
        {
            var parent = _nodes.FirstOrDefault(n => n.Id == parentId);
            if (parent != null && HasAncestorOrSelfCloudGroup(parent))
            {
                MessageBox.Show("云组下不允许再嵌套云组，请在其他分组下新建。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        var dlg = new KingCloudGroupAddWindow { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var groupNode = new Node
        {
            Id = Guid.NewGuid().ToString(),
            ParentId = parentId,
            Type = NodeType.kingCloudGroup,
            Name = string.IsNullOrWhiteSpace(dlg.GroupName) ? "金山云" : dlg.GroupName.Trim(),
            Config = new ConnectionConfig
            {
                KingAccessKeyId = dlg.AccessKeyId,
                KingAccessKeySecret = dlg.AccessKeySecret
            }
        };
        _nodes.Add(groupNode);
        _storage.SaveNodes(_nodes);
        BuildTree();
        SyncKingCloudGroup(groupNode);
    }

    /// <summary>根据金山云 KEC 实例列表构建 地域→服务器 节点树。</summary>
    private static List<Node> BuildKingCloudSubtree(string rootId, List<KingEcsInstance> instances)
    {
        var result = new List<Node>();
        var regionIdToNode = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);

        foreach (var ins in instances.OrderBy(i => i.RegionId).ThenBy(i => i.InstanceName))
        {
            if (string.IsNullOrEmpty(ins.InstanceId)) continue;

            if (!regionIdToNode.TryGetValue(ins.RegionId ?? "", out var regionNode))
            {
                regionNode = new Node
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = rootId,
                    Type = NodeType.group,
                    Name = ins.RegionName ?? ins.RegionId ?? "",
                    Config = null
                };
                result.Add(regionNode);
                regionIdToNode[ins.RegionId ?? ""] = regionNode;
            }

            var host = ins.PublicIp ?? ins.PrivateIp ?? "";
            if (string.IsNullOrEmpty(host)) continue;

            var serverNode = new Node
            {
                Id = Guid.NewGuid().ToString(),
                ParentId = regionNode.Id,
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

    private void SyncKingCloudGroup(Node groupNode)
    {
        if (groupNode.Config?.KingAccessKeyId == null || groupNode.Config?.KingAccessKeySecret == null)
        {
            MessageBox.Show("该金山云组未配置密钥，请先在「设置」中保存 AccessKeyId/AccessKeySecret。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var accessKeyId = groupNode.Config.KingAccessKeyId;
        var accessKeySecret = groupNode.Config.KingAccessKeySecret ?? "";
        var cts = new CancellationTokenSource();
        var syncWin = new KingCloudSyncWindow(cts.Cancel) { Owner = this };
        syncWin.Show();
        syncWin.ReportProgress("正在拉取实例列表…", 0, 1);

        var progress = new Progress<(string message, int current, int total)>(p =>
        {
            syncWin.Dispatcher.Invoke(() => syncWin.ReportProgress(p.message, p.current, p.total));
        });

        List<KingEcsInstance>? instances = null;
        Exception? runEx = null;
        var t = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                return KingCloudService.ListAllInstances(accessKeyId, accessKeySecret, progress, cts.Token);
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
        var cloudInstanceIds = instances.Where(i => !string.IsNullOrEmpty(i.InstanceId)).Select(i => i.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);

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
                else if (n.Type == NodeType.group || n.Type == NodeType.tencentCloudGroup || n.Type == NodeType.aliCloudGroup || n.Type == NodeType.kingCloudGroup)
                    CollectServerNodes(n.Id);
            }
        }
        CollectServerNodes(groupNode.Id);

        var toRemove = serverNodesUnderGroup.Where(n => !cloudInstanceIds.Contains(n.Config!.ResourceId!)).ToList();
        var removedDetail = new List<string>();
        if (toRemove.Count > 0)
        {
            if (MessageBox.Show($"云上已不存在以下 {toRemove.Count} 个实例，是否从本地树中删除？\n\n此操作不可恢复。", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                foreach (var n in toRemove)
                {
                    removedDetail.Add(n.Name ?? n.Config?.ResourceId ?? n.Id);
                    _nodes.RemoveAll(x => x.Id == n.Id);
                }
            }
            else
                toRemove = new List<Node>();
        }

        var instanceMap = instances.Where(i => !string.IsNullOrEmpty(i.InstanceId)).ToDictionary(i => i.InstanceId!, StringComparer.OrdinalIgnoreCase);
        var updatedDetail = new List<string>();
        foreach (var n in serverNodesUnderGroup.Where(n => _nodes.Any(x => x.Id == n.Id)))
        {
            var rid = n.Config?.ResourceId;
            if (string.IsNullOrEmpty(rid)) continue;
            if (instanceMap.TryGetValue(rid, out var kingIns))
            {
                var newHost = kingIns.PublicIp ?? kingIns.PrivateIp ?? "";
                if (!string.IsNullOrEmpty(newHost) && n.Config!.Host != newHost)
                {
                    var oldHost = n.Config.Host ?? "";
                    n.Config.Host = newHost;
                    updatedDetail.Add($"{n.Name ?? rid}: {oldHost} → {newHost}");
                }
            }
        }

        var existingRegionNodes = _nodes.Where(x => x.ParentId == groupNode.Id && x.Type == NodeType.group).ToList();
        var regionByKey = existingRegionNodes.ToDictionary(n => n.Name ?? "", StringComparer.OrdinalIgnoreCase);
        var existingIds = CollectResourceIdsUnderGroup(groupNode.Id);
        var addedCount = 0;
        var addedDetail = new List<string>();

        foreach (var ins in instances)
        {
            if (string.IsNullOrEmpty(ins.InstanceId) || existingIds.Contains(ins.InstanceId)) continue;
            existingIds.Add(ins.InstanceId);
            addedCount++;
            addedDetail.Add(string.IsNullOrEmpty(ins.InstanceName) ? ins.InstanceId! : $"{ins.InstanceName} ({ins.InstanceId})");

            var regionName = ins.RegionName ?? ins.RegionId ?? "";
            if (!regionByKey.TryGetValue(regionName, out var regionNode))
            {
                regionNode = new Node
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = groupNode.Id,
                    Type = NodeType.group,
                    Name = regionName,
                    Config = null
                };
                _nodes.Add(regionNode);
                regionByKey[regionName] = regionNode;
            }

            var host = ins.PublicIp ?? ins.PrivateIp ?? "";
            if (string.IsNullOrEmpty(host)) continue;

            var serverNode = new Node
            {
                Id = Guid.NewGuid().ToString(),
                ParentId = regionNode.Id,
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
        var detailLines = new List<string>();
        foreach (var s in removedDetail)
            detailLines.Add("[删除] " + s);
        foreach (var s in updatedDetail)
            detailLines.Add("[更新IP] " + s);
        foreach (var s in addedDetail)
            detailLines.Add("[新增] " + s);
        var summary = $"已删除 {removedDetail.Count} 个本地节点，更新 {updatedDetail.Count} 台 IP，新增 {addedCount} 台实例（共 {instances.Count} 台）。";
        syncWin.ReportResult(summary, true, detailLines.Count > 0 ? detailLines : null);
        _selectedNodeIds.Clear();
        _selectedNodeIds.Add(groupNode.Id);
        BuildTree(expandNodes: false);
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
                var projectDisplayName = !string.IsNullOrWhiteSpace(ins.ProjectName)
                    ? $"{ins.ProjectName} ({ins.ProjectId})"
                    : "项目 " + ins.ProjectId;
                projectNode = new Node
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = regionNode.Id,
                    Type = NodeType.group,
                    Name = projectDisplayName,
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
        syncWin.ReportProgress("正在拉取 CVM/轻量（并行）…", 0, 1);

        // 进度回调统一同步封送到同步窗口的 UI 线程；CVM 与轻量并行拉取时合并两路进度
        var totalCvm = 0;
        var totalLighthouse = 0;
        var completedCvm = 0;
        var completedLighthouse = 0;
        var progressLock = new object();
        var progressCvm = new Progress<(string message, int current, int total)>(p =>
        {
            lock (progressLock)
            {
                totalCvm = p.total;
                completedCvm = p.current;
                var total = totalCvm + totalLighthouse;
                var current = completedCvm + completedLighthouse;
                syncWin.Dispatcher.Invoke(() => syncWin.ReportProgress("正在拉取 CVM/轻量（并行）…", current, total > 0 ? total : 1));
            }
        });
        var progressLighthouse = new Progress<(string message, int current, int total)>(p =>
        {
            lock (progressLock)
            {
                totalLighthouse = p.total;
                completedLighthouse = p.current;
                var total = totalCvm + totalLighthouse;
                var current = completedCvm + completedLighthouse;
                syncWin.Dispatcher.Invoke(() => syncWin.ReportProgress("正在拉取 CVM/轻量（并行）…", current, total > 0 ? total : 1));
            }
        });

        List<TencentCvmInstance>? cvmInstances = null;
        List<TencentLighthouseInstance>? lighthouseInstances = null;
        Exception? runEx = null;
        var tCvm = Task.Run(() => TencentCloudService.ListInstances(secretId, secretKey, progressCvm, cts.Token));
        var tLighthouse = Task.Run(() => TencentCloudService.ListLighthouseInstances(secretId, secretKey, progressLighthouse, cts.Token));
        var t = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(tCvm, tLighthouse);
                return (Cvm: tCvm.Result, Lighthouse: tLighthouse.Result);
            }
            catch (Exception ex)
            {
                runEx = ex;
                return (Cvm: (List<TencentCvmInstance>?)null, Lighthouse: (List<TencentLighthouseInstance>?)null);
            }
        });

        while (!t.IsCompleted && syncWin.IsLoaded && syncWin.IsVisible)
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                System.Windows.Threading.DispatcherPriority.Background,
                () => { });
            Thread.Sleep(50);
        }

        if (runEx != null || t.Result.Cvm == null || t.Result.Lighthouse == null)
        {
            var errMsg = runEx != null ? GetFullExceptionMessage(runEx) : "拉取失败或已取消";
            syncWin.ReportResult(errMsg, false);
            return;
        }
        cvmInstances = t.Result.Cvm;
        lighthouseInstances = t.Result.Lighthouse;
        var cloudInstanceIds = cvmInstances.Select(i => i.InstanceId)
            .Concat(lighthouseInstances.Select(i => i.InstanceId))
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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
                else if (n.Type == NodeType.group || n.Type == NodeType.tencentCloudGroup || n.Type == NodeType.aliCloudGroup || n.Type == NodeType.kingCloudGroup)
                    CollectServerNodes(n.Id);
            }
        }
        CollectServerNodes(groupNode.Id);

        // 删除本地存在但云上已不存在的节点（先询问用户），并记录实际删除的节点名用于变动列表
        var toRemove = serverNodesUnderGroup.Where(n => !cloudInstanceIds.Contains(n.Config!.ResourceId!)).ToList();
        var removedDetail = new List<string>();
        if (toRemove.Count > 0)
        {
            if (MessageBox.Show($"云上已不存在以下 {toRemove.Count} 个实例，是否从本地树中删除？\n\n此操作不可恢复。", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                foreach (var n in toRemove)
                {
                    removedDetail.Add(n.Name ?? n.Config?.ResourceId ?? n.Id);
                    _nodes.RemoveAll(x => x.Id == n.Id);
                }
            }
            else
                toRemove = new List<Node>();
        }

        // 现有实例 ID -> 实例信息（用于更新 IP 或新增）
        var cvmInstanceMap = cvmInstances.Where(i => !string.IsNullOrEmpty(i.InstanceId)).ToDictionary(i => i.InstanceId!, StringComparer.OrdinalIgnoreCase);
        var lighthouseInstanceMap = lighthouseInstances.Where(i => !string.IsNullOrEmpty(i.InstanceId)).ToDictionary(i => i.InstanceId!, StringComparer.OrdinalIgnoreCase);

        // 更新已有节点的 Host，并记录 IP 变动用于详细列表
        var updatedDetail = new List<string>();
        foreach (var n in serverNodesUnderGroup.Where(n => _nodes.Any(x => x.Id == n.Id)))
        {
            var rid = n.Config?.ResourceId;
            if (string.IsNullOrEmpty(rid)) continue;

            // 先尝试从 CVM 实例中查找，再尝试从轻量服务器实例中查找
            if (cvmInstanceMap.TryGetValue(rid, out var cvmIns))
            {
                var newHost = cvmIns.PublicIp ?? cvmIns.PrivateIp ?? "";
                if (!string.IsNullOrEmpty(newHost) && n.Config!.Host != newHost)
                {
                    var oldHost = n.Config.Host ?? "";
                    n.Config.Host = newHost;
                    updatedDetail.Add($"{n.Name ?? rid}: {oldHost} → {newHost}");
                }
            }
            else if (lighthouseInstanceMap.TryGetValue(rid, out var lighthouseIns))
            {
                var newHost = lighthouseIns.PublicIp ?? lighthouseIns.PrivateIp ?? "";
                if (!string.IsNullOrEmpty(newHost) && n.Config!.Host != newHost)
                {
                    var oldHost = n.Config.Host ?? "";
                    n.Config.Host = newHost;
                    updatedDetail.Add($"{n.Name ?? rid}: {oldHost} → {newHost}");
                }
            }
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
        var addedDetail = new List<string>();

        // 处理 CVM 实例（地域 → 项目 → 服务器）
        foreach (var ins in cvmInstances)
        {
            if (string.IsNullOrEmpty(ins.InstanceId) || existingIds.Contains(ins.InstanceId)) continue;
            existingIds.Add(ins.InstanceId);
            addedCount++;
            addedDetail.Add(string.IsNullOrEmpty(ins.InstanceName) ? ins.InstanceId! : $"{ins.InstanceName} ({ins.InstanceId})");

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
                var projectDisplayName = !string.IsNullOrWhiteSpace(ins.ProjectName)
                    ? $"{ins.ProjectName} ({ins.ProjectId})"
                    : "项目 " + ins.ProjectId;
                projectNode = new Node
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = regionNode.Id,
                    Type = NodeType.group,
                    Name = projectDisplayName,
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

        // 处理轻量服务器实例（地域 → 服务器，无项目层）
        var lighthouseRegionKeyPrefix = "轻量服务器-";
        foreach (var ins in lighthouseInstances)
        {
            if (string.IsNullOrEmpty(ins.InstanceId) || existingIds.Contains(ins.InstanceId)) continue;
            existingIds.Add(ins.InstanceId);
            addedCount++;
            addedDetail.Add(string.IsNullOrEmpty(ins.InstanceName) ? ins.InstanceId! : $"{ins.InstanceName} ({ins.InstanceId})");

            var regionName = ins.RegionName ?? ins.Region ?? "";
            // 轻量服务器地域添加前缀以区分 CVM
            var regionKey = lighthouseRegionKeyPrefix + regionName;
            if (!regionByKey.TryGetValue(regionKey, out var regionNode))
            {
                regionNode = new Node
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = groupNode.Id,
                    Type = NodeType.group,
                    Name = regionName + " (轻量)",
                    Config = null
                };
                _nodes.Add(regionNode);
                regionByKey[regionKey] = regionNode;
            }

            var host = ins.PublicIp ?? ins.PrivateIp ?? "";
            if (string.IsNullOrEmpty(host)) continue;

            var serverNode = new Node
            {
                Id = Guid.NewGuid().ToString(),
                ParentId = regionNode.Id,
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
        // 汇总变动详情：删除、更新 IP、新增
        var detailLines = new List<string>();
        foreach (var s in removedDetail)
            detailLines.Add("[删除] " + s);
        foreach (var s in updatedDetail)
            detailLines.Add("[更新IP] " + s);
        foreach (var s in addedDetail)
            detailLines.Add("[新增] " + s);
        var summary = $"已删除 {removedDetail.Count} 个本地节点，更新 {updatedDetail.Count} 台 IP，新增 {addedCount} 台实例（CVM: {cvmInstances.Count}，轻量: {lighthouseInstances.Count}）。";
        syncWin.ReportResult(summary, true, detailLines.Count > 0 ? detailLines : null);
        // 同步后只恢复当前同步组的选中与展开，避免沿用旧选中项导致其它云组被展开
        _selectedNodeIds.Clear();
        _selectedNodeIds.Add(groupNode.Id);
        BuildTree(expandNodes: false);
    }

    private HashSet<string> CollectResourceIdsUnderGroup(string groupId)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in _nodes.Where(x => x.ParentId == groupId))
        {
            if ((n.Type == NodeType.ssh || n.Type == NodeType.rdp) && !string.IsNullOrEmpty(n.Config?.ResourceId))
                set.Add(n.Config!.ResourceId!);
            if (n.Type == NodeType.group || n.Type == NodeType.tencentCloudGroup || n.Type == NodeType.aliCloudGroup || n.Type == NodeType.kingCloudGroup)
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

    /// <summary>从项目节点显示名中解析出项目 ID（支持 "项目 123" 或 "项目名 (123)" 格式）。</summary>
    private static string GetTencentProjectIdFromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "0";
        // 匹配末尾 "(数字)" 如 "默认项目 (1000101)"
        var match = System.Text.RegularExpressions.Regex.Match(name, @"\((\d+)\)\s*$");
        if (match.Success) return match.Groups[1].Value;
        // 兼容旧格式 "项目 123"
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
                // 直接设置 Background 以覆盖控件模板的默认视觉
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
        if (target.Type != NodeType.group && target.Type != NodeType.tencentCloudGroup && target.Type != NodeType.aliCloudGroup && target.Type != NodeType.kingCloudGroup) return;
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
            if (target.Type != NodeType.group && target.Type != NodeType.tencentCloudGroup && target.Type != NodeType.aliCloudGroup && target.Type != NodeType.kingCloudGroup) return;
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
