using System.Collections.Generic;
using System.Linq;
using System.Windows;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>主窗口：维护相关（如磁盘占用检查、端口扫描）。</summary>
public partial class MainWindow
{
    private void OpenDiskUsageCheck(Node node)
    {
        List<Node> targetNodes;
        if (node.Type == NodeType.group || node.Type == NodeType.tencentCloudGroup ||
            node.Type == NodeType.aliCloudGroup || node.Type == NodeType.kingsoftCloudGroup)
            targetNodes = GetLeafNodes(node.Id).Where(n => n.Type == NodeType.ssh || ConfigResolver.IsCloudRdpNode(n, _nodes)).ToList();
        else if (node.Type == NodeType.ssh)
            targetNodes = new List<Node> { node };
        else if (node.Type == NodeType.rdp && ConfigResolver.IsCloudRdpNode(node, _nodes))
            targetNodes = new List<Node> { node };
        else
            return;

        if (targetNodes.Count == 0)
        {
            MessageBox.Show("没有可检查的 SSH 或云 RDP 节点。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Information);
            BringMainWindowToFront();
            return;
        }

        var win = new DiskUsageCheckWindow(targetNodes, _nodes, _credentials, _tunnels, OpenTab);
        win.Owner = this;
        // 放在主窗口右侧，避免遮挡左侧节点树，便于同时查看对话框与操作树展开/折叠
        if (ActualWidth > 0 && ActualHeight > 0)
        {
            win.WindowStartupLocation = WindowStartupLocation.Manual;
            var rightEdge = Left + ActualWidth + 8;
            var screenRight = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
            win.Left = rightEdge + win.Width <= screenRight ? rightEdge : Left + ActualWidth - win.Width;
            win.Top = Top + (ActualHeight - win.Height) / 2;
        }
        win.Show();
    }

    /// <summary>打开端口扫描窗口</summary>
    private void OpenPortScan(Node node)
    {
        List<Node> targetNodes;
        if (node.Type == NodeType.group || node.Type == NodeType.tencentCloudGroup ||
            node.Type == NodeType.aliCloudGroup || node.Type == NodeType.kingsoftCloudGroup)
            // 支持所有主机类型：SSH 和 RDP
            targetNodes = GetLeafNodes(node.Id).Where(n => n.Type == NodeType.ssh || n.Type == NodeType.rdp).ToList();
        else if (node.Type == NodeType.ssh || node.Type == NodeType.rdp)
            targetNodes = new List<Node> { node };
        else
            return;

        if (targetNodes.Count == 0)
        {
            MessageBox.Show("没有可扫描的主机节点（SSH/RDP）。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Information);
            BringMainWindowToFront();
            return;
        }

        // 根据主机地址去重（同一服务器可能有多个节点配置）
        var uniqueNodes = targetNodes
            .GroupBy(n => n.Config?.Host ?? "")
            .Select(g => g.First())
            .ToList();

        // 传入 AppSettings 以便加载/保存配置
        var win = new PortScanWindow(uniqueNodes, _nodes, _credentials, _tunnels, _appSettings);
        win.Owner = this;
        win.Show();
    }

    /// <summary>打开端口扫描窗口（多选模式）</summary>
    private void OpenPortScanMulti(List<Node> selectedNodes)
    {
        // 筛选出所有主机节点（SSH 和 RDP）
        var targetNodes = selectedNodes.Where(n => n.Type == NodeType.ssh || n.Type == NodeType.rdp).ToList();

        if (targetNodes.Count == 0)
        {
            MessageBox.Show("选中的节点中没有可扫描的主机节点（SSH/RDP）。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Information);
            BringMainWindowToFront();
            return;
        }

        // 根据主机地址去重（同一服务器可能有多个节点配置）
        var uniqueNodes = targetNodes
            .GroupBy(n => n.Config?.Host ?? "")
            .Select(g => g.First())
            .ToList();

        // 传入 AppSettings 以便加载/保存配置
        var win = new PortScanWindow(uniqueNodes, _nodes, _credentials, _tunnels, _appSettings);
        win.Owner = this;
        win.Show();
    }

    /// <summary>顶栏菜单：工具 → 端口扫描（默认不添加任何服务器，用户可手动添加目标）</summary>
    private void MenuPortScan_Click(object sender, RoutedEventArgs e)
    {
        var win = new PortScanWindow(new List<Node>(), _nodes, _credentials, _tunnels, _appSettings);
        win.Owner = this;
        win.Show();
    }

    /// <summary>顶栏菜单：工具 → 同步所有云节点。对节点树中所有云组（腾讯/阿里/金山）依次执行同步。</summary>
    private void MenuSyncAllCloudNodes_Click(object sender, RoutedEventArgs e)
    {
        var cloudGroups = _nodes
            .Where(n => n.Type == NodeType.tencentCloudGroup || n.Type == NodeType.aliCloudGroup || n.Type == NodeType.kingsoftCloudGroup)
            .ToList();
        if (cloudGroups.Count == 0)
        {
            MessageBox.Show("节点树中没有云同步分组（腾讯云/阿里云/金山云）。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Information);
            BringMainWindowToFront();
            return;
        }

        foreach (var group in cloudGroups)
        {
            if (group.Type == NodeType.tencentCloudGroup)
                SyncTencentCloudGroup(group);
            else if (group.Type == NodeType.aliCloudGroup)
                SyncAliCloudGroup(group);
            else if (group.Type == NodeType.kingsoftCloudGroup)
                SyncKingsoftCloudGroup(group);
        }
    }
}
