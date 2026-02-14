using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using xOpenTerm.Controls;
using xOpenTerm.Models;

namespace xOpenTerm;

/// <summary>主窗口：服务器树构建与筛选。</summary>
public partial class MainWindow
{
    private void BuildTree(bool expandNodes = true, HashSet<string>? initialExpandedIds = null)
    {
        var expandedIds = initialExpandedIds ?? CollectExpandedGroupNodeIds(ServerTree);
        _nodeIdToTvi.Clear();
        var childrenByParentId = BuildChildrenByParentId();
        var serverCountUnder = BuildServerCountUnder(childrenByParentId);
        _buildVisibleNodeIds = BuildVisibleNodeIds(childrenByParentId);
        var matchingServerCountUnder = _buildVisibleNodeIds != null
            ? BuildMatchingServerCountUnder(childrenByParentId, _buildVisibleNodeIds)
            : null;
        List<TreeViewItem> newRootItems;
        try
        {
            var roots = childrenByParentId.GetValueOrDefault("", Array.Empty<Node>().ToList());
            newRootItems = new List<TreeViewItem>(roots.Count);
            foreach (var node in roots)
                if (MatchesSearch(node))
                    newRootItems.Add(CreateTreeItem(node, expandedIds, expandNodes, childrenByParentId, serverCountUnder, matchingServerCountUnder));
        }
        finally
        {
            _buildVisibleNodeIds = null;
        }
        ServerTree.BeginInit();
        try
        {
            ServerTree.Items.Clear();
            foreach (var item in newRootItems)
                ServerTree.Items.Add(item);
        }
        finally
        {
            ServerTree.EndInit();
        }
        RestoreSelectionFromSelectedNodeIds();
        _prevSelectedNodeIds = new HashSet<string>(_selectedNodeIds);
    }

    private Dictionary<string, List<Node>> BuildChildrenByParentId()
    {
        var d = new Dictionary<string, List<Node>>();
        foreach (var n in _nodes)
        {
            var key = n.ParentId ?? "";
            if (!d.TryGetValue(key, out var list))
            {
                list = new List<Node>();
                d[key] = list;
            }
            list.Add(n);
        }
        return d;
    }

    private static Dictionary<string, int> BuildServerCountUnder(Dictionary<string, List<Node>> childrenByParentId)
    {
        var count = new Dictionary<string, int>();
        foreach (var root in childrenByParentId.GetValueOrDefault("", Array.Empty<Node>().ToList()))
            BuildServerCountUnderDfs(root, childrenByParentId, count);
        return count;
    }

    private static int BuildServerCountUnderDfs(Node n, Dictionary<string, List<Node>> childrenByParentId, Dictionary<string, int> count)
    {
        var list = childrenByParentId.GetValueOrDefault(n.Id, Array.Empty<Node>().ToList());
        var c = 0;
        foreach (var child in list)
        {
            if (child.Type == NodeType.ssh || child.Type == NodeType.rdp)
                c += 1;
            else
                c += BuildServerCountUnderDfs(child, childrenByParentId, count);
        }
        count[n.Id] = c;
        return c;
    }

    /// <summary>搜索时：每个节点下“匹配的”服务器数量（仅统计可见的 ssh/rdp）。</summary>
    private static Dictionary<string, int> BuildMatchingServerCountUnder(
        Dictionary<string, List<Node>> childrenByParentId,
        HashSet<string> visibleNodeIds)
    {
        var count = new Dictionary<string, int>();
        var roots = childrenByParentId.GetValueOrDefault("", Array.Empty<Node>().ToList());
        foreach (var root in roots)
        {
            if (!visibleNodeIds.Contains(root.Id)) continue;
            BuildMatchingServerCountUnderDfs(root, childrenByParentId, visibleNodeIds, count);
        }
        return count;
    }

    private static int BuildMatchingServerCountUnderDfs(
        Node n,
        Dictionary<string, List<Node>> childrenByParentId,
        HashSet<string> visibleNodeIds,
        Dictionary<string, int> count)
    {
        var list = childrenByParentId.GetValueOrDefault(n.Id, Array.Empty<Node>().ToList());
        var c = 0;
        foreach (var child in list)
        {
            if (!visibleNodeIds.Contains(child.Id)) continue;
            if (child.Type == NodeType.ssh || child.Type == NodeType.rdp)
                c += 1;
            else
                c += BuildMatchingServerCountUnderDfs(child, childrenByParentId, visibleNodeIds, count);
        }
        count[n.Id] = c;
        return c;
    }

    private HashSet<string>? BuildVisibleNodeIds(Dictionary<string, List<Node>> childrenByParentId)
    {
        if (string.IsNullOrWhiteSpace(_searchTerm)) return null;
        var term = _searchTerm.Trim().ToLowerInvariant();
        var selfMatchIds = _nodes.Where(n => NodeSelfMatches(n, term)).Select(n => n.Id).ToHashSet();
        var parentIdByNodeId = _nodes.Where(n => !string.IsNullOrEmpty(n.ParentId)).ToDictionary(n => n.Id, n => n.ParentId!);
        var visible = new HashSet<string>(selfMatchIds);
        var queue = new Queue<string>(selfMatchIds);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!parentIdByNodeId.TryGetValue(id, out var pid)) continue;
            if (visible.Add(pid))
                queue.Enqueue(pid);
        }
        return visible;
    }

    private static bool NodeSelfMatches(Node n, string term)
    {
        if (n.Name.ToLowerInvariant().Contains(term)) return true;
        if (n.Config?.Host?.ToLowerInvariant().Contains(term) == true) return true;
        if (n.Config?.Username?.ToLowerInvariant().Contains(term) == true) return true;
        return false;
    }

    private void RestoreSelectionFromSelectedNodeIds()
    {
        if (_selectedNodeIds.Count == 0) return;
        var firstId = _selectedNodeIds.FirstOrDefault(id => _nodes.Any(n => n.Id == id));
        if (string.IsNullOrEmpty(firstId)) return;
        if (!_nodeIdToTvi.TryGetValue(firstId, out var tvi)) return;
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
        for (var p = VisualTreeHelper.GetParent(item) as DependencyObject; p != null; p = VisualTreeHelper.GetParent(p))
        {
            if (p is TreeViewItem parentTvi)
                parentTvi.IsExpanded = true;
        }
    }

    private static HashSet<string>? CollectExpandedGroupNodeIds(ItemsControl tree)
    {
        if (tree.Items.Count == 0) return null;
        var set = new HashSet<string>();
        foreach (var tvi in EnumerateTreeViewItems(tree))
            if (tvi.IsExpanded && tvi.Tag is Node n && (n.Type == NodeType.group || n.Type == NodeType.tencentCloudGroup || n.Type == NodeType.aliCloudGroup || n.Type == NodeType.kingsoftCloudGroup))
                set.Add(n.Id);
        return set;
    }

    private bool ShouldExpand(Node node, HashSet<string>? expandedIds, bool defaultExpand)
    {
        if (node.Type != NodeType.group && node.Type != NodeType.tencentCloudGroup && node.Type != NodeType.aliCloudGroup && node.Type != NodeType.kingsoftCloudGroup) return true;
        // 搜索时：若该节点在“可见集合”中（自身匹配或为匹配节点的祖先），则展开以便看到匹配的子节点
        if (_buildVisibleNodeIds != null && _buildVisibleNodeIds.Contains(node.Id)) return true;
        if (expandedIds != null) return expandedIds.Contains(node.Id);
        return defaultExpand;
    }

    private bool MatchesSearch(Node node)
    {
        if (string.IsNullOrWhiteSpace(_searchTerm)) return true;
        if (_buildVisibleNodeIds != null) return _buildVisibleNodeIds.Contains(node.Id);
        var term = _searchTerm.Trim().ToLowerInvariant();
        if (node.Name.ToLowerInvariant().Contains(term)) return true;
        if (node.Config?.Host?.ToLowerInvariant().Contains(term) == true) return true;
        if (node.Config?.Username?.ToLowerInvariant().Contains(term) == true) return true;
        return _nodes.Where(n => n.ParentId == node.Id).Any(MatchesSearch);
    }

    private void ServerSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _searchTerm = ServerSearchBox?.Text ?? "";
        UpdateServerSearchPlaceholder();
        if (_searchDebounceTimer == null)
        {
            _searchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            _searchDebounceTimer.Tick += (_, _) =>
            {
                _searchDebounceTimer!.Stop();
                BuildTree();
            };
        }
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void UpdateServerSearchPlaceholder()
    {
        if (ServerSearchBoxPlaceholder == null) return;
        ServerSearchBoxPlaceholder.Visibility = string.IsNullOrEmpty(ServerSearchBox?.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private TreeViewItem CreateTreeItem(Node node, HashSet<string>? expandedIds, bool defaultExpand,
        Dictionary<string, List<Node>> childrenByParentId, Dictionary<string, int> serverCountUnder,
        Dictionary<string, int>? matchingServerCountUnder = null)
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
        var isGroup = node.Type == NodeType.group || node.Type == NodeType.tencentCloudGroup || node.Type == NodeType.aliCloudGroup || node.Type == NodeType.kingsoftCloudGroup;
        if (isGroup)
        {
            var countDict = matchingServerCountUnder ?? serverCountUnder;
            var serverCount = countDict.TryGetValue(node.Id, out var c) ? c : 0;
            header.Children.Add(new TextBlock
            {
                Text = " (" + serverCount + ")",
                Foreground = textSecondary,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        if ((node.Type == NodeType.ssh || node.Type == NodeType.rdp) && !string.IsNullOrEmpty(node.Config?.Host))
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
        _nodeIdToTvi[node.Id] = item;
        if (node.Type == NodeType.group || node.Type == NodeType.tencentCloudGroup || node.Type == NodeType.aliCloudGroup || node.Type == NodeType.kingsoftCloudGroup)
        {
            void UpdateGroupIcon()
            {
                iconBlock.Text = ServerTreeItemBuilder.NodeIcon(node, item.IsExpanded);
            }
            item.Expanded += (_, _) => UpdateGroupIcon();
            item.Collapsed += (_, _) => UpdateGroupIcon();
        }
        var children = childrenByParentId.GetValueOrDefault(node.Id, Array.Empty<Node>().ToList());
        foreach (var child in children)
            if (MatchesSearch(child))
                item.Items.Add(CreateTreeItem(child, expandedIds, defaultExpand, childrenByParentId, serverCountUnder, matchingServerCountUnder));
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
        e.Handled = true;
    }

    private static string GetNodeDisplayName(Node node)
    {
        var raw = node.Type == NodeType.rdp && string.IsNullOrEmpty(node.Name) && !string.IsNullOrEmpty(node.Config?.Host)
            ? node.Config!.Host!
            : (node.Name ?? "");
        return StripTrailingProjectIdFromDisplay(raw);
    }

    private static string StripTrailingProjectIdFromDisplay(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var m = System.Text.RegularExpressions.Regex.Match(name, @"^(.+?)\s*\(\d+\)\s*$");
        return m.Success ? m.Groups[1].Value.TrimEnd() : name;
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
}
