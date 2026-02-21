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
            return;
        }

        var win = new DiskUsageCheckWindow(targetNodes, _nodes, _credentials, _tunnels, OpenTab);
        win.Owner = this;
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

    /// <summary>顶栏菜单：工具 → 端口扫描（使用全部主机节点作为初始目标，无则空列表）</summary>
    private void MenuPortScan_Click(object sender, RoutedEventArgs e)
    {
        var targetNodes = _nodes
            .Where(n => n.Type == NodeType.ssh || n.Type == NodeType.rdp)
            .GroupBy(n => n.Config?.Host ?? "")
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .Select(g => g.First())
            .ToList();
        var win = new PortScanWindow(targetNodes, _nodes, _credentials, _tunnels, _appSettings);
        win.Owner = this;
        win.Show();
    }
}
