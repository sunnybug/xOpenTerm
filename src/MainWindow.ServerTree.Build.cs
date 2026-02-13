using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using xOpenTerm.Controls;
using xOpenTerm.Models;

namespace xOpenTerm;

/// <summary>主窗口：服务器树构建与筛选。</summary>
public partial class MainWindow
{
    private void BuildTree(bool expandNodes = true, HashSet<string>? initialExpandedIds = null)
    {
        var expandedIds = initialExpandedIds ?? CollectExpandedGroupNodeIds(ServerTree);
        ServerTree.Items.Clear();
        var roots = _nodes.Where(n => string.IsNullOrEmpty(n.ParentId)).ToList();
        foreach (var node in roots)
            if (MatchesSearch(node))
                ServerTree.Items.Add(CreateTreeItem(node, expandedIds, expandNodes));
        RestoreSelectionFromSelectedNodeIds();
    }

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

    private static bool ShouldExpand(Node node, HashSet<string>? expandedIds, bool defaultExpand)
    {
        if (node.Type != NodeType.group && node.Type != NodeType.tencentCloudGroup && node.Type != NodeType.aliCloudGroup && node.Type != NodeType.kingsoftCloudGroup) return true;
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
        UpdateServerSearchPlaceholder();
        BuildTree();
    }

    private void UpdateServerSearchPlaceholder()
    {
        if (ServerSearchBoxPlaceholder == null) return;
        ServerSearchBoxPlaceholder.Visibility = string.IsNullOrEmpty(ServerSearchBox?.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
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
        if (node.Type == NodeType.group || node.Type == NodeType.tencentCloudGroup || node.Type == NodeType.aliCloudGroup || node.Type == NodeType.kingsoftCloudGroup)
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
