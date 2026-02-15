using System.Collections.Generic;
using System.Linq;
using System.Windows;
using xOpenTerm.Models;

namespace xOpenTerm;

/// <summary>主窗口：维护相关（如磁盘占用检查）。</summary>
public partial class MainWindow
{
    private void OpenDiskUsageCheck(Node node)
    {
        List<Node> targetNodes;
        if (node.Type == NodeType.group || node.Type == NodeType.tencentCloudGroup ||
            node.Type == NodeType.aliCloudGroup || node.Type == NodeType.kingsoftCloudGroup)
            targetNodes = GetLeafNodes(node.Id).Where(n => n.Type == NodeType.ssh).ToList();
        else if (node.Type == NodeType.ssh)
            targetNodes = new List<Node> { node };
        else
            return;

        if (targetNodes.Count == 0)
        {
            MessageBox.Show("没有可检查的 SSH 节点。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var win = new DiskUsageCheckWindow(targetNodes, _nodes, _credentials, _tunnels, OpenTab);
        win.Owner = this;
        win.Show();
    }
}
