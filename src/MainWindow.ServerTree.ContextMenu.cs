using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>主窗口：服务器树右键菜单与命令。</summary>
public partial class MainWindow
{
    private void ServerTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_suppressTreeViewSelection) return;
        if (e.NewValue is TreeViewItem tvi && tvi.Tag is Node node)
        {
            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (!ctrl && !shift)
                UpdateTreeSelectionVisuals();
        }
    }

    private void ServerTree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        List<Node>? nodesToAct = null;
        if (_selectedNodeIds.Count > 0)
        {
            nodesToAct = _nodes.Where(n => _selectedNodeIds.Contains(n.Id)).ToList();
            if (nodesToAct.Count == 0) nodesToAct = null;
        }
        if (nodesToAct == null && ServerTree.SelectedItem is TreeViewItem tvi && tvi.Tag is Node single)
            nodesToAct = new List<Node> { single };

        if (nodesToAct == null || nodesToAct.Count == 0) return;

        if (e.Key == Key.Delete)
        {
            e.Handled = true;
            if (nodesToAct.Count > 1)
                DeleteSelected(nodesToAct);
            else
            {
                var node = nodesToAct[0];
                if (node.Type == NodeType.group || node.Type == NodeType.tencentCloudGroup || node.Type == NodeType.aliCloudGroup || node.Type == NodeType.kingsoftCloudGroup)
                    DeleteNodeRecursive(node);
                else
                    DeleteNode(node);
            }
        }
    }

    private void ServerTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var item = FindClickedNode(e.OriginalSource);
        if (item?.Tag is not Node node) return;
        if (node.Type == NodeType.group || node.Type == NodeType.tencentCloudGroup || node.Type == NodeType.aliCloudGroup || node.Type == NodeType.kingsoftCloudGroup)
            return;
        OpenTab(node);
    }

    private void ServerTree_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var tvi = FindClickedNode(e.OriginalSource);
        _contextMenuNode = tvi?.Tag as Node;
        // 右键菜单触发时，将选择改为鼠标处的节点
        if (_contextMenuNode != null && tvi != null)
        {
            _selectedNodeIds.Clear();
            _selectedNodeIds.Add(_contextMenuNode.Id);
            _lastSelectedNodeId = _contextMenuNode.Id;
            _suppressTreeViewSelection = true;
            tvi.IsSelected = true;
            _suppressTreeViewSelection = false;
            UpdateTreeSelectionVisuals();
        }
        e.Handled = true;
    }

    private void ServerTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var node = _contextMenuNode;
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
        menu.Items.Add(CreateMenuItem("连接(_L)", () => ConnectSelected(selectedNodes)));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("删除(_D)", () => DeleteSelected(selectedNodes)));
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

    private bool HasAncestorOrSelfCloudGroup(Node? node)
    {
        if (node == null) return false;
        var byId = _nodes.ToDictionary(n => n.Id);
        var id = node.Id;
        while (!string.IsNullOrEmpty(id) && byId.TryGetValue(id, out var n))
        {
            if (n.Type == NodeType.tencentCloudGroup || n.Type == NodeType.aliCloudGroup || n.Type == NodeType.kingsoftCloudGroup)
                return true;
            id = n.ParentId;
        }
        return false;
    }

    private NodeType? GetAncestorCloudGroupType(Node? node)
    {
        if (node == null) return null;
        var byId = _nodes.ToDictionary(n => n.Id);
        var id = node.Id;
        while (!string.IsNullOrEmpty(id) && byId.TryGetValue(id, out var n))
        {
            if (n.Type == NodeType.tencentCloudGroup || n.Type == NodeType.aliCloudGroup || n.Type == NodeType.kingsoftCloudGroup)
                return n.Type;
            id = n.ParentId;
        }
        return null;
    }

    /// <summary>腾讯云 CVM 实例 ID 格式：ins- 后跟小写字母/数字。</summary>
    private static readonly Regex TencentCvmInstanceIdRegex = new(@"^ins-[a-z0-9]+$", RegexOptions.Compiled);
    /// <summary>腾讯云轻量实例 ID 格式：lhins- 后跟小写字母/数字。</summary>
    private static readonly Regex TencentLighthouseInstanceIdRegex = new(@"^lhins-[a-z0-9]+$", RegexOptions.Compiled);

    private bool TryGetCloudDetailUrl(Node node, out string? url)
    {
        url = null;
        if (node.Config?.ResourceId == null || node.Config.CloudRegionId == null) return false;
        var instanceId = node.Config.ResourceId.Trim();
        if (string.IsNullOrEmpty(instanceId)) return false;

        var cloudType = GetAncestorCloudGroupType(node);
        var region = node.Config.CloudRegionId;
        var isLightweight = node.Config.CloudIsLightweight == true;

        switch (cloudType)
        {
            case NodeType.tencentCloudGroup:
                if (isLightweight ? !TencentLighthouseInstanceIdRegex.IsMatch(instanceId) : !TencentCvmInstanceIdRegex.IsMatch(instanceId))
                    return false;
                url = isLightweight
                    ? $"https://console.cloud.tencent.com/lighthouse/instance/detail?region={Uri.EscapeDataString(region)}&id={Uri.EscapeDataString(instanceId)}"
                    : $"https://console.cloud.tencent.com/cvm/instance/detail?region={Uri.EscapeDataString(region)}&id={Uri.EscapeDataString(instanceId)}";
                break;
            case NodeType.aliCloudGroup:
                url = isLightweight
                    ? $"https://swas.console.aliyun.com/#/server/detail?regionId={Uri.EscapeDataString(region)}&instanceId={Uri.EscapeDataString(instanceId)}"
                    : $"https://ecs.console.aliyun.com/server/{Uri.EscapeDataString(instanceId)}/detail?regionId={Uri.EscapeDataString(region)}#/";
                break;
            case NodeType.kingsoftCloudGroup:
                url = $"https://kec.console.ksyun.com/v2/#/kec/detail?Region={Uri.EscapeDataString(region)}&kec={Uri.EscapeDataString(instanceId)}";
                break;
            default:
                return false;
        }
        return true;
    }

    private ContextMenu BuildContextMenu(Node? node)
    {
        var menu = new ContextMenu();
        if (node == null)
        {
            var newSub = new MenuItem { Header = "新建(_N)" };
            newSub.Items.Add(CreateMenuItem("分组(_G)", () => AddNode(NodeType.group, null)));
            newSub.Items.Add(CreateMenuItem("分组 - 腾讯云(_T)", () => AddTencentCloudGroup(null)));
            newSub.Items.Add(CreateMenuItem("分组 - 阿里云(_A)", () => AddAliCloudGroup(null)));
            newSub.Items.Add(CreateMenuItem("分组 - 金山云(_K)", () => AddKingsoftCloudGroup(null)));
            newSub.Items.Add(CreateMenuItem("主机(_H)", () => AddNode(NodeType.ssh, null)));
            menu.Items.Add(newSub);
            menu.Items.Add(new Separator());
            var importSub = new MenuItem { Header = "导入(_I)" };
            importSub.Items.Add(CreateMenuItem("导入 MobaXterm(_M)", () => ImportMobaXterm(null)));
            importSub.Items.Add(CreateMenuItem("导入 YAML(_Y)", () => ImportYaml(null)));
            menu.Items.Add(importSub);
            var exportSub = new MenuItem { Header = "导出(_O)" };
            exportSub.Items.Add(CreateMenuItem("导出 YAML(_Y)", () => ExportYaml()));
            menu.Items.Add(exportSub);
            return menu;
        }
        if (node.Type == NodeType.group)
        {
            var newSub = new MenuItem { Header = "新建(_N)" };
            newSub.Items.Add(CreateMenuItem("分组(_G)", () => AddNode(NodeType.group, node.Id)));
            if (!HasAncestorOrSelfCloudGroup(node))
            {
                newSub.Items.Add(CreateMenuItem("分组 - 腾讯云(_T)", () => AddTencentCloudGroup(node.Id)));
                newSub.Items.Add(CreateMenuItem("分组 - 阿里云(_A)", () => AddAliCloudGroup(node.Id)));
                newSub.Items.Add(CreateMenuItem("分组 - 金山云(_K)", () => AddKingsoftCloudGroup(node.Id)));
            }
            newSub.Items.Add(CreateMenuItem("主机(_H)", () => AddNode(NodeType.ssh, node.Id)));
            menu.Items.Add(newSub);
            menu.Items.Add(new Separator());
            var importSub = new MenuItem { Header = "导入(_I)" };
            importSub.Items.Add(CreateMenuItem("导入 MobaXterm(_M)", () => ImportMobaXterm(node)));
            importSub.Items.Add(CreateMenuItem("导入 YAML(_Y)", () => ImportYaml(node)));
            menu.Items.Add(importSub);
            var exportSub = new MenuItem { Header = "导出(_O)" };
            exportSub.Items.Add(CreateMenuItem("导出 YAML(_Y)", () => ExportYaml()));
            menu.Items.Add(exportSub);
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("编辑(_E)", () => OpenGroupEdit(node)));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("连接全部(_A)", () => ConnectAll(node.Id)));
            if (GetLeafNodes(node.Id).Any(n => n.Type == NodeType.ssh))
            {
                menu.Items.Add(new Separator());
                var maintainSub = new MenuItem { Header = "维护(_M)" };
                maintainSub.Items.Add(CreateMenuItem("磁盘占用(_D)", () => OpenDiskUsageCheck(node)));
                menu.Items.Add(maintainSub);
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("删除（含子节点）(_X)", () => DeleteNodeRecursive(node)));
        }
        else if (node.Type == NodeType.tencentCloudGroup)
        {
            var newSub = new MenuItem { Header = "新建(_N)" };
            newSub.Items.Add(CreateMenuItem("分组(_G)", () => AddNode(NodeType.group, node.Id)));
            newSub.Items.Add(CreateMenuItem("主机(_H)", () => AddNode(NodeType.ssh, node.Id)));
            menu.Items.Add(newSub);
            menu.Items.Add(new Separator());
            var syncSubTencent = new MenuItem { Header = "同步(_Y)" };
            syncSubTencent.Items.Add(CreateMenuItem("同步", () => SyncTencentCloudGroup(node)));
            syncSubTencent.Items.Add(CreateMenuItem("重建", () => RebuildTencentCloudGroup(node)));
            menu.Items.Add(syncSubTencent);
            var importSub = new MenuItem { Header = "导入(_I)" };
            importSub.Items.Add(CreateMenuItem("导入 MobaXterm(_M)", () => ImportMobaXterm(node)));
            importSub.Items.Add(CreateMenuItem("导入 YAML(_Y)", () => ImportYaml(node)));
            menu.Items.Add(importSub);
            var exportSub = new MenuItem { Header = "导出(_O)" };
            exportSub.Items.Add(CreateMenuItem("导出 YAML(_Y)", () => ExportYaml()));
            menu.Items.Add(exportSub);
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("编辑(_E)", () => OpenGroupEdit(node)));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("连接全部(_A)", () => ConnectAll(node.Id)));
            if (GetLeafNodes(node.Id).Any(n => n.Type == NodeType.ssh))
            {
                menu.Items.Add(new Separator());
                var maintainSub = new MenuItem { Header = "维护(_M)" };
                maintainSub.Items.Add(CreateMenuItem("磁盘占用(_D)", () => OpenDiskUsageCheck(node)));
                menu.Items.Add(maintainSub);
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("删除（含子节点）(_X)", () => DeleteNodeRecursive(node)));
        }
        else if (node.Type == NodeType.aliCloudGroup)
        {
            var newSub = new MenuItem { Header = "新建(_N)" };
            newSub.Items.Add(CreateMenuItem("分组(_G)", () => AddNode(NodeType.group, node.Id)));
            newSub.Items.Add(CreateMenuItem("主机(_H)", () => AddNode(NodeType.ssh, node.Id)));
            menu.Items.Add(newSub);
            menu.Items.Add(new Separator());
            var syncSubAli = new MenuItem { Header = "同步(_Y)" };
            syncSubAli.Items.Add(CreateMenuItem("同步", () => SyncAliCloudGroup(node)));
            syncSubAli.Items.Add(CreateMenuItem("重建", () => RebuildAliCloudGroup(node)));
            menu.Items.Add(syncSubAli);
            var importSub = new MenuItem { Header = "导入(_I)" };
            importSub.Items.Add(CreateMenuItem("导入 MobaXterm(_M)", () => ImportMobaXterm(node)));
            importSub.Items.Add(CreateMenuItem("导入 YAML(_Y)", () => ImportYaml(node)));
            menu.Items.Add(importSub);
            var exportSub = new MenuItem { Header = "导出(_O)" };
            exportSub.Items.Add(CreateMenuItem("导出 YAML(_Y)", () => ExportYaml()));
            menu.Items.Add(exportSub);
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("编辑(_E)", () => OpenGroupEdit(node)));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("连接全部(_A)", () => ConnectAll(node.Id)));
            if (GetLeafNodes(node.Id).Any(n => n.Type == NodeType.ssh))
            {
                menu.Items.Add(new Separator());
                var maintainSub = new MenuItem { Header = "维护(_M)" };
                maintainSub.Items.Add(CreateMenuItem("磁盘占用(_D)", () => OpenDiskUsageCheck(node)));
                menu.Items.Add(maintainSub);
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("删除（含子节点）(_X)", () => DeleteNodeRecursive(node)));
        }
        else if (node.Type == NodeType.kingsoftCloudGroup)
        {
            var newSub = new MenuItem { Header = "新建(_N)" };
            newSub.Items.Add(CreateMenuItem("分组(_G)", () => AddNode(NodeType.group, node.Id)));
            newSub.Items.Add(CreateMenuItem("主机(_H)", () => AddNode(NodeType.ssh, node.Id)));
            menu.Items.Add(newSub);
            menu.Items.Add(new Separator());
            var syncSubKingsoft = new MenuItem { Header = "同步(_Y)" };
            syncSubKingsoft.Items.Add(CreateMenuItem("同步", () => SyncKingsoftCloudGroup(node)));
            syncSubKingsoft.Items.Add(CreateMenuItem("重建", () => RebuildKingsoftCloudGroup(node)));
            menu.Items.Add(syncSubKingsoft);
            var importSub = new MenuItem { Header = "导入(_I)" };
            importSub.Items.Add(CreateMenuItem("导入 MobaXterm(_M)", () => ImportMobaXterm(node)));
            importSub.Items.Add(CreateMenuItem("导入 YAML(_Y)", () => ImportYaml(node)));
            menu.Items.Add(importSub);
            var exportSub = new MenuItem { Header = "导出(_O)" };
            exportSub.Items.Add(CreateMenuItem("导出 YAML(_Y)", () => ExportYaml()));
            menu.Items.Add(exportSub);
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("编辑(_E)", () => OpenGroupEdit(node)));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("连接全部(_A)", () => ConnectAll(node.Id)));
            if (GetLeafNodes(node.Id).Any(n => n.Type == NodeType.ssh))
            {
                menu.Items.Add(new Separator());
                var maintainSub = new MenuItem { Header = "维护(_M)" };
                maintainSub.Items.Add(CreateMenuItem("磁盘占用(_D)", () => OpenDiskUsageCheck(node)));
                menu.Items.Add(maintainSub);
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("删除（含子节点）(_X)", () => DeleteNodeRecursive(node)));
        }
        else
        {
            menu.Items.Add(CreateMenuItem("连接(_L)", () => OpenTab(node)));
            if (TryGetCloudDetailUrl(node, out var cloudDetailUrl))
                menu.Items.Add(CreateMenuItem("云详情(_V)", () => OpenCloudDetail(cloudDetailUrl!)));
            menu.Items.Add(CreateMenuItem("编辑(_E)", () => EditNode(node)));
            if (node.Type == NodeType.ssh)
            {
                menu.Items.Add(new Separator());
                var maintainSub = new MenuItem { Header = "维护(_M)" };
                maintainSub.Items.Add(CreateMenuItem("磁盘占用(_D)", () => OpenDiskUsageCheck(node)));
                menu.Items.Add(maintainSub);
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("删除(_D)", () => DeleteNode(node)));
            menu.Items.Add(CreateMenuItem("克隆(_C)", () => DuplicateNode(node)));
        }
        return menu;
    }

    private static void OpenCloudDetail(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ExceptionLog.Write(ex, "打开云详情链接失败");
            MessageBox.Show($"无法打开链接：{ex.Message}", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static MenuItem CreateMenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }
}
