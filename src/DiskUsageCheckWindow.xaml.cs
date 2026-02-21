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

    /// <summary>云 RDP 节点：阿里/腾讯/金山云优先用云监控 API 获取磁盘占用率；其他云或 API 不可用时通过 SSH(22) 执行 Windows 磁盘命令。仅当最大占用率 ≥ 阈值时返回。</summary>
    private async Task<(Node Node, double MaxPercent, IReadOnlyList<(string DiskName, double UsePercent)> DiskList)?> CheckOneCloudRdpNodeAsync(
        Node node,
        int threshold,
        CancellationToken token,
        Action onComplete)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var groupNode = ConfigResolver.GetAncestorCloudGroupNode(node, _nodes);
            var instanceId = node.Config?.ResourceId?.Trim() ?? "";
            var region = node.Config?.CloudRegionId ?? "";

            if (groupNode?.Config != null && !string.IsNullOrEmpty(instanceId) && !string.IsNullOrEmpty(region))
            {
                if (groupNode.Type == NodeType.aliCloudGroup && !(node.Config?.CloudIsLightweight == true))
                {
                    var ak = groupNode.Config.AliAccessKeyId ?? "";
                    var sk = groupNode.Config.AliAccessKeySecret ?? "";
                    if (!string.IsNullOrEmpty(ak) && !string.IsNullOrEmpty(sk))
                    {
                        var apiResult = AliCloudService.GetInstanceDiskUsageFromApi(ak, sk, instanceId, region, false, token);
                        if (apiResult.HasValue && apiResult.Value.MaxPercent >= threshold && apiResult.Value.ByDevice != null)
                        {
                            var diskList = apiResult.Value.ByDevice.Select(d => (d.Device, d.Percent)).ToList();
                            onComplete();
                            return (node, apiResult.Value.MaxPercent, diskList);
                        }
                    }
                }
                else if (groupNode.Type == NodeType.tencentCloudGroup)
                {
                    var sid = groupNode.Config?.TencentSecretId ?? "";
                    var skey = groupNode.Config?.TencentSecretKey ?? "";
                    var isLight = node.Config?.CloudIsLightweight == true;
                    if (!string.IsNullOrEmpty(sid) && !string.IsNullOrEmpty(skey))
                    {
                        var apiResult = TencentCloudService.GetInstanceDiskUsageFromApi(sid, skey, instanceId, region, isLight, token);
                        if (apiResult.HasValue && apiResult.Value.MaxPercent >= threshold && apiResult.Value.ByDevice != null)
                        {
                            var diskList = apiResult.Value.ByDevice.Select(d => (d.Device, d.Percent)).ToList();
                            onComplete();
                            return (node, apiResult.Value.MaxPercent, diskList);
                        }
                    }
                }
                else if (groupNode.Type == NodeType.kingsoftCloudGroup)
                {
                    var ak = groupNode.Config?.KsyunAccessKeyId ?? "";
                    var sk = groupNode.Config?.KsyunAccessKeySecret ?? "";
                    if (!string.IsNullOrEmpty(ak) && !string.IsNullOrEmpty(sk))
                    {
                        var apiResult = KingsoftCloudService.GetInstanceDiskUsageFromApi(ak, sk, instanceId, region, token);
                        if (apiResult.HasValue && apiResult.Value.MaxPercent >= threshold && apiResult.Value.ByDevice != null)
                        {
                            var diskList = apiResult.Value.ByDevice.Select(d => (d.Device, d.Percent)).ToList();
                            onComplete();
                            return (node, apiResult.Value.MaxPercent, diskList);
                        }
                    }
                }
            }

            var (host, _, username, _, password, _) = ConfigResolver.ResolveRdp(node, _nodes, _credentials);
            if (string.IsNullOrEmpty(host))
            {
                onComplete();
                return (node, -1, new List<(string, double)> { ("无主机或未配置", 0) });
            }
            const ushort sshPort = 22;
            var (diskOutput, sshFailureReason) = await SessionManager.RunSshCommandAsync(
                host, sshPort, username ?? "", password, null, null, null, false,
                RdpStatsHelper.DiskStatsCommand, token);
            onComplete();
            if (diskOutput == null)
                return (node, -1, new List<(string, double)> { (sshFailureReason ?? "SSH 连接失败", 0) });
            var diskListSsh = RdpStatsHelper.ParseDiskStatsOutput(diskOutput);
            if (diskListSsh.Count == 0)
                return (node, -1, new List<(string, double)> { ("无磁盘数据或解析失败", 0) });
            var maxPercent = diskListSsh.Max(x => x.UsePercent);
            if (maxPercent < threshold) return null;
            return (node, maxPercent, diskListSsh);
        }
        catch (OperationCanceledException)
        {
            onComplete();
            return null;
        }
        catch (Exception ex)
        {
            var nodeName = string.IsNullOrEmpty(node.Name) ? (node.Config?.Host ?? "未命名") : node.Name;
            ExceptionLog.Write(ex, $"云 RDP 磁盘占用检查节点 {nodeName}");
            onComplete();
            return (node, -1, new List<(string, double)> { ($"错误: {ex.Message}", 0) });
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
            var (diskOutput, failureReason) = await SessionManager.RunSshCommandAsync(
                host, port, username, password, keyPath, keyPassphrase, jumpChain, useAgent,
                SshStatsHelper.DiskStatsCommand, token);
            if (diskOutput == null)
            {
                onComplete();
                return (node, -1, new List<(string, string)> { (failureReason ?? "连接或认证失败", "") });
            }
            var diskList = SshStatsHelper.ParseDiskStatsOutput(diskOutput);
            var maxPercent = diskList.Count > 0 ? diskList.Max(x => x.UsePercent) : 0;
            if (maxPercent >= threshold)
            {
                var (duOutput, _) = await SessionManager.RunSshCommandAsync(
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

        // 检查期间禁用「开始检查」和阈值输入，防止重复点击
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

        List<(Node Node, double MaxPercent, IReadOnlyList<(string SizeText, string Path)> Dirs)>? sshResults = null;
        List<(Node Node, double MaxPercent, IReadOnlyList<(string DiskName, double UsePercent)> DiskList)>? cloudRdpUsageResults = null;
        try
        {
            var sshTasks = toCheckSsh.Select(node => CheckOneNodeAsync(node, threshold, token, () =>
            {
                var n = Interlocked.Increment(ref completed);
                UpdateUi(() => { ProgressText.Text = $"正在检查… 已完成 {n}/{total}"; ProgressBar.Value = n; });
            })).ToList();
            var cloudTasks = cloudRdpNodes.Select(node => CheckOneCloudRdpNodeAsync(node, threshold, token, () =>
            {
                var n = Interlocked.Increment(ref completed);
                UpdateUi(() => { ProgressText.Text = $"正在检查… 已完成 {n}/{total}"; ProgressBar.Value = n; });
            })).ToList();

            var sshWhenAll = Task.WhenAll(sshTasks);
            var cloudWhenAll = Task.WhenAll(cloudTasks);
            await Task.WhenAll(sshWhenAll, cloudWhenAll);

            sshResults = (await sshWhenAll).Where(r => r != null).Select(r => r!.Value).ToList();
            cloudRdpUsageResults = (await cloudWhenAll).Where(r => r != null).Select(r => r!.Value).ToList();
        }
        finally
        {
            _completed = true;
            // 无论正常结束、取消或异常，都恢复按钮状态
            UpdateUi(() =>
            {
                ProgressText.Text = _cts?.IsCancellationRequested == true ? "已取消" : "检查完成";
                ProgressBar.Value = total;
                StartBtn.IsEnabled = true;
                ThresholdBox.IsEnabled = true;
                CancelBtn.Content = "关闭";
                if (sshResults != null && cloudRdpUsageResults != null)
                    BuildResultUi(sshResults, cloudRdpUsageResults);
            });
        }
    }

    private void BuildResultUi(
        List<(Node Node, double MaxPercent, IReadOnlyList<(string SizeText, string Path)> Dirs)> sshResults,
        List<(Node Node, double MaxPercent, IReadOnlyList<(string DiskName, double UsePercent)> DiskList)> cloudRdpUsageResults)
    {
        ResultPanel.Children.Clear();
        if (sshResults.Count == 0 && cloudRdpUsageResults.Count == 0)
        {
            var noResult = new TextBlock
            {
                Text = "没有超过阈值的节点；或云 RDP 主机未开启 SSH(22)，无法采集占用率。",
                Foreground = (Brush)FindResource("TextSecondary"),
                Margin = new Thickness(0, 8, 0, 0)
            };
            ResultPanel.Children.Add(noResult);
            return;
        }

        foreach (var (node, maxPercent, dirs) in sshResults)
        {
            var nodeName = string.IsNullOrEmpty(node.Name) ? (node.Config?.Host ?? "未命名") : node.Name;
            var firstFailureReason = maxPercent < 0 && dirs.Count > 0 ? dirs[0].SizeText?.Trim() : null;
            var headerText = maxPercent >= 0
                ? $"{nodeName} — 磁盘占用 {maxPercent:F0}%"
                : (string.IsNullOrEmpty(firstFailureReason) ? $"{nodeName} — 采集失败" : $"{nodeName} — 采集失败：{firstFailureReason}");
            var pathsPanel = new StackPanel { Margin = new Thickness(12, 0, 0, 8) };
            foreach (var (sizeText, path) in dirs)
            {
                if (string.IsNullOrEmpty(path))
                {
                    if (!string.IsNullOrEmpty(sizeText))
                    {
                        pathsPanel.Children.Add(new TextBlock
                        {
                            Text = sizeText,
                            Margin = new Thickness(0, 2, 0, 2),
                            Foreground = (Brush)FindResource("TextSecondary")
                        });
                    }
                    continue;
                }
                pathsPanel.Children.Add(new TextBlock
                {
                    Text = $"{sizeText}  {path}",
                    Margin = new Thickness(0, 2, 0, 2),
                    Foreground = (Brush)FindResource("TextSecondary")
                });
            }
            AddResultExpander(node, headerText, pathsPanel);
        }

        foreach (var (node, maxPercent, diskList) in cloudRdpUsageResults)
        {
            var nodeName = string.IsNullOrEmpty(node.Name) ? (node.Config?.Host ?? "未命名") : node.Name;
            var firstFailureReason = maxPercent < 0 && diskList.Count > 0 ? diskList[0].DiskName?.Trim() : null;
            var headerText = maxPercent >= 0
                ? $"{nodeName} — 磁盘占用 {maxPercent:F0}%"
                : (string.IsNullOrEmpty(firstFailureReason) ? $"{nodeName} — 采集失败" : $"{nodeName} — 采集失败：{firstFailureReason}");
            var contentPanel = new StackPanel { Margin = new Thickness(12, 0, 0, 8) };
            if (maxPercent >= 0)
            {
                var driveText = string.Join("  ", diskList.Select(d => $"{d.DiskName}: {d.UsePercent:F0}%"));
                contentPanel.Children.Add(new TextBlock { Text = driveText, Foreground = (Brush)FindResource("TextSecondary") });
            }
            else
            {
                foreach (var d in diskList)
                    contentPanel.Children.Add(new TextBlock { Text = d.DiskName, Foreground = (Brush)FindResource("TextSecondary"), Margin = new Thickness(0, 2, 0, 2) });
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
        {
            Close();
        }
        else
        {
            // 取消操作：请求取消任务
            _cts?.Cancel();
            // 更新 UI 状态，让用户知道正在取消
            CancelBtn.IsEnabled = false;
            CancelBtn.Content = "正在取消...";
            ProgressText.Text = "正在取消检查...";
        }
    }
}
