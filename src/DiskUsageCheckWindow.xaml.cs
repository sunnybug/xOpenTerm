using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>磁盘占用检查窗口：对 SSH 节点执行 df/du（按阈值展示），对云类型 RDP 节点通过云 API 查询磁盘容量并展示。</summary>
public partial class DiskUsageCheckWindow : Window
{
    private readonly List<Node> _targetNodes;
    private readonly IList<Node> _nodes;
    private readonly IList<Credential> _credentials;
    private readonly IList<Tunnel> _tunnels;
    private readonly Action<Node>? _openConnection;
    private CancellationTokenSource? _cts;
    private bool _completed;

    public DiskUsageCheckWindow(
        List<Node> targetNodes,
        IList<Node> nodes,
        IList<Credential> credentials,
        IList<Tunnel> tunnels,
        Action<Node>? openConnection = null)
    {
        InitializeComponent();
        _targetNodes = targetNodes;
        _nodes = nodes;
        _credentials = credentials;
        _tunnels = tunnels;
        _openConnection = openConnection;
    }

    private void UpdateUi(Action action)
    {
        var dispatcher = Dispatcher ?? Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    private async Task<(Node Node, int? SystemGb, IReadOnlyList<int> DataGb, string? Error)?> CheckOneCloudRdpNodeAsync(
        Node node,
        CancellationToken token,
        Action onComplete)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var groupNode = ConfigResolver.GetAncestorCloudGroupNode(node, _nodes);
            if (groupNode?.Config == null)
            {
                onComplete();
                return (node, null, Array.Empty<int>(), "未找到云组或未配置密钥");
            }
            var instanceId = node.Config!.ResourceId!.Trim();
            var region = node.Config.CloudRegionId ?? "";
            var isLightweight = node.Config.CloudIsLightweight == true;
            if (groupNode.Type == NodeType.tencentCloudGroup)
            {
                var sid = groupNode.Config.TencentSecretId ?? "";
                var skey = groupNode.Config.TencentSecretKey ?? "";
                if (string.IsNullOrEmpty(sid) || string.IsNullOrEmpty(skey))
                {
                    onComplete();
                    return (node, null, Array.Empty<int>(), "腾讯云组未配置 SecretId/SecretKey");
                }
                var (sys, data) = TencentCloudService.GetInstanceDiskInfo(sid, skey, instanceId, region, isLightweight, token);
                onComplete();
                return (node, sys, data, null);
            }
            if (groupNode.Type == NodeType.aliCloudGroup)
            {
                var ak = groupNode.Config.AliAccessKeyId ?? "";
                var sk = groupNode.Config.AliAccessKeySecret ?? "";
                if (string.IsNullOrEmpty(ak) || string.IsNullOrEmpty(sk))
                {
                    onComplete();
                    return (node, null, Array.Empty<int>(), "阿里云组未配置 AccessKey");
                }
                var (sys, data) = AliCloudService.GetInstanceDiskInfo(ak, sk, instanceId, region, isLightweight, token);
                onComplete();
                return (node, sys, data, null);
            }
            if (groupNode.Type == NodeType.kingsoftCloudGroup)
            {
                var ak = groupNode.Config.KsyunAccessKeyId ?? "";
                var sk = groupNode.Config.KsyunAccessKeySecret ?? "";
                if (string.IsNullOrEmpty(ak) || string.IsNullOrEmpty(sk))
                {
                    onComplete();
                    return (node, null, Array.Empty<int>(), "金山云组未配置 AccessKey");
                }
                var (sys, data) = KingsoftCloudService.GetInstanceDiskInfo(ak, sk, instanceId, region, token);
                onComplete();
                return (node, sys, data, null);
            }
            onComplete();
            return (node, null, Array.Empty<int>(), "不支持的云类型");
        }
        catch (OperationCanceledException)
        {
            onComplete();
            return null;
        }
        catch (Exception ex)
        {
            var nodeName = string.IsNullOrEmpty(node.Name) ? (node.Config?.Host ?? "未命名") : node.Name;
            ExceptionLog.Write(ex, $"云 RDP 磁盘检查节点 {nodeName}");
            onComplete();
            return (node, null, Array.Empty<int>(), ex.Message);
        }
    }

    /// <summary>同一 (host, port) 只保留一个节点，优先保留用户名为 root 的节点。</summary>
    private List<Node> DeduplicateByHostPortPreferRoot(List<Node> nodes)
    {
        var list = new List<(Node node, string host, ushort port, string username)>();
        foreach (var n in nodes)
        {
            try
            {
                var (host, port, username, _, _, _, _, _) = ConfigResolver.ResolveSsh(n, _nodes, _credentials, _tunnels);
                list.Add((n, host ?? "", port, username ?? ""));
            }
            catch
            {
                var host = n.Config?.Host ?? "";
                var port = (ushort)(n.Config?.Port ?? 22);
                list.Add((n, host, port, ""));
            }
        }
        return list
            .GroupBy(x => (x.host, x.port))
            .Select(g => g.OrderByDescending(x => string.Equals(x.username, "root", StringComparison.OrdinalIgnoreCase)).First().node)
            .ToList();
    }

    private async Task<(Node Node, double MaxPercent, IReadOnlyList<(string SizeText, string Path)> Dirs)?> CheckOneNodeAsync(
        Node node,
        int threshold,
        CancellationToken token,
        Action onComplete)
    {
        try
        {
            var (host, port, username, password, keyPath, keyPassphrase, jumpChain, useAgent) =
                ConfigResolver.ResolveSsh(node, _nodes, _credentials, _tunnels);
            var diskOutput = await SessionManager.RunSshCommandAsync(
                host, port, username, password, keyPath, keyPassphrase, jumpChain, useAgent,
                SshStatsHelper.DiskStatsCommand, token);
            var diskList = SshStatsHelper.ParseDiskStatsOutput(diskOutput);
            var maxPercent = diskList.Count > 0 ? diskList.Max(x => x.UsePercent) : 0;
            if (maxPercent >= threshold)
            {
                var duOutput = await SessionManager.RunSshCommandAsync(
                    host, port, username, password, keyPath, keyPassphrase, jumpChain, useAgent,
                    SshStatsHelper.LargestDirsCommand, token);
                var dirs = SshStatsHelper.ParseLargestDirsOutput(duOutput);
                onComplete();
                return (node, maxPercent, dirs);
            }
            onComplete();
            return null;
        }
        catch (OperationCanceledException)
        {
            onComplete();
            return null;
        }
        catch (Exception ex)
        {
            var nodeName = string.IsNullOrEmpty(node.Name) ? (node.Config?.Host ?? "未命名") : node.Name;
            ExceptionLog.Write(ex, $"磁盘占用检查节点 {nodeName}");
            onComplete();
            return (node, -1, new List<(string, string)> { ($"错误: {ex.Message}", "") });
        }
    }

    private async void StartBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ThresholdBox?.Text?.Trim(), out var threshold) || threshold < 1 || threshold > 100)
        {
            MessageBox.Show("请输入 1–100 之间的整数作为阈值（仅用于 SSH 节点）。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var sshNodes = _targetNodes.Where(n => n.Type == NodeType.ssh).ToList();
        var cloudRdpNodes = _targetNodes.Where(n => ConfigResolver.IsCloudRdpNode(n, _nodes)).ToList();
        var toCheckSsh = DeduplicateByHostPortPreferRoot(sshNodes);
        var total = toCheckSsh.Count + cloudRdpNodes.Count;
        if (total == 0)
        {
            MessageBox.Show("没有可检查的 SSH 或云 RDP 节点。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        StartBtn.IsEnabled = false;
        ThresholdBox.IsEnabled = false;
        ProgressText.Visibility = Visibility.Visible;
        ProgressBar.Visibility = Visibility.Visible;
        ProgressBar.Maximum = total;
        ProgressBar.Value = 0;
        ResultPanel.Children.Clear();
        _completed = false;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var completed = 0;

        var sshTasks = toCheckSsh.Select(node => CheckOneNodeAsync(node, threshold, token, () =>
        {
            var n = Interlocked.Increment(ref completed);
            UpdateUi(() => { ProgressText.Text = $"正在检查… 已完成 {n}/{total}"; ProgressBar.Value = n; });
        })).ToList();
        var cloudTasks = cloudRdpNodes.Select(node => CheckOneCloudRdpNodeAsync(node, token, () =>
        {
            var n = Interlocked.Increment(ref completed);
            UpdateUi(() => { ProgressText.Text = $"正在检查… 已完成 {n}/{total}"; ProgressBar.Value = n; });
        })).ToList();

        var sshResults = (await Task.WhenAll(sshTasks)).Where(r => r != null).Select(r => r!.Value).ToList();
        var cloudResults = (await Task.WhenAll(cloudTasks)).Where(r => r != null).Select(r => r!.Value).ToList();

        _completed = true;
        UpdateUi(() =>
        {
            ProgressText.Text = _cts?.IsCancellationRequested == true ? "已取消" : "检查完成";
            ProgressBar.Value = total;
            StartBtn.IsEnabled = true;
            ThresholdBox.IsEnabled = true;
            CancelBtn.Content = "关闭";
            BuildResultUi(sshResults, cloudResults);
        });
    }

    private void BuildResultUi(
        List<(Node Node, double MaxPercent, IReadOnlyList<(string SizeText, string Path)> Dirs)> sshResults,
        List<(Node Node, int? SystemGb, IReadOnlyList<int> DataGb, string? Error)> cloudResults)
    {
        ResultPanel.Children.Clear();
        if (sshResults.Count == 0 && cloudResults.Count == 0)
        {
            var noResult = new TextBlock
            {
                Text = "没有超过阈值的 SSH 节点，且无云 RDP 结果；或未采集到数据。",
                Foreground = (Brush)FindResource("TextSecondary"),
                Margin = new Thickness(0, 8, 0, 0)
            };
            ResultPanel.Children.Add(noResult);
            return;
        }

        foreach (var (node, maxPercent, dirs) in sshResults)
        {
            var nodeName = string.IsNullOrEmpty(node.Name) ? (node.Config?.Host ?? "未命名") : node.Name;
            var headerText = maxPercent >= 0 ? $"{nodeName} — 磁盘占用 {maxPercent:F0}%" : $"{nodeName} — 采集失败";
            var pathsPanel = new StackPanel { Margin = new Thickness(12, 0, 0, 8) };
            foreach (var (sizeText, path) in dirs)
            {
                if (string.IsNullOrEmpty(path)) continue;
                var line = new TextBlock
                {
                    Text = $"{sizeText}  {path}",
                    Margin = new Thickness(0, 2, 0, 2),
                    Foreground = (Brush)FindResource("TextSecondary")
                };
                pathsPanel.Children.Add(line);
            }
            AddResultExpander(node, headerText, pathsPanel);
        }

        foreach (var (node, systemGb, dataGb, error) in cloudResults)
        {
            var nodeName = string.IsNullOrEmpty(node.Name) ? (node.Config?.Host ?? "未命名") : node.Name;
            string headerText;
            var contentPanel = new StackPanel { Margin = new Thickness(12, 0, 0, 8) };
            if (error != null)
            {
                headerText = $"{nodeName} — 采集失败";
                contentPanel.Children.Add(new TextBlock { Text = error, Foreground = (Brush)FindResource("TextSecondary"), TextWrapping = TextWrapping.Wrap });
            }
            else
            {
                var parts = new List<string>();
                if (systemGb.HasValue && systemGb.Value > 0) parts.Add($"系统盘 {systemGb} GB");
                if (dataGb.Count > 0) parts.Add("数据盘 " + string.Join(", ", dataGb.Select(g => $"{g} GB")));
                headerText = parts.Count > 0 ? $"{nodeName} — {string.Join(", ", parts)}" : $"{nodeName} — 无磁盘信息";
                contentPanel.Children.Add(new TextBlock { Text = "来自云控制台，非主机内 df。", Foreground = (Brush)FindResource("TextSecondary") });
            }
            AddResultExpander(node, headerText, contentPanel);
        }
    }

    private void AddResultExpander(Node node, string headerText, System.Windows.Controls.Panel contentPanel)
    {
        var expander = new Expander
        {
            Header = headerText,
            Content = contentPanel,
            IsExpanded = false,
            Foreground = (Brush)FindResource("TextPrimary"),
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(0, 4, 0, 4),
            Tag = node
        };
        if (_openConnection != null)
        {
            var menu = new ContextMenu();
            var connectItem = new MenuItem { Header = "连接(_L)" };
            connectItem.Click += (_, _) => _openConnection(node);
            menu.Items.Add(connectItem);
            expander.ContextMenu = menu;
        }
        ResultPanel.Children.Add(expander);
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_completed)
            Close();
        else
            _cts?.Cancel();
    }
}
