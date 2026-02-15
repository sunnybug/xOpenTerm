using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>磁盘占用检查窗口：输入阈值，对目标 SSH 节点执行 df/du，展示超过阈值的节点及大文件/目录，点击路径可在对应 SSH 标签页粘贴打开命令。</summary>
public partial class DiskUsageCheckWindow : Window
{
    private readonly List<Node> _targetNodes;
    private readonly IList<Node> _nodes;
    private readonly IList<Credential> _credentials;
    private readonly IList<Tunnel> _tunnels;
    private readonly Action<Node, string> _focusTabAndCopyCommand;
    private CancellationTokenSource? _cts;
    private bool _completed;

    public DiskUsageCheckWindow(
        List<Node> targetNodes,
        IList<Node> nodes,
        IList<Credential> credentials,
        IList<Tunnel> tunnels,
        Action<Node, string> focusTabAndCopyCommand)
    {
        InitializeComponent();
        _targetNodes = targetNodes;
        _nodes = nodes;
        _credentials = credentials;
        _tunnels = tunnels;
        _focusTabAndCopyCommand = focusTabAndCopyCommand;
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

    private async void StartBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ThresholdBox?.Text?.Trim(), out var threshold) || threshold < 1 || threshold > 100)
        {
            MessageBox.Show("请输入 1–100 之间的整数作为阈值。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StartBtn.IsEnabled = false;
        ThresholdBox.IsEnabled = false;
        ProgressText.Visibility = Visibility.Visible;
        ProgressBar.Visibility = Visibility.Visible;
        ProgressBar.Maximum = _targetNodes.Count;
        ProgressBar.Value = 0;
        ResultPanel.Children.Clear();
        _completed = false;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        var results = new List<(Node Node, double MaxPercent, IReadOnlyList<(string SizeText, string Path)> Dirs)>();

        for (var i = 0; i < _targetNodes.Count; i++)
        {
            if (token.IsCancellationRequested) break;
            var node = _targetNodes[i];
            var nodeName = string.IsNullOrEmpty(node.Name) ? (node.Config?.Host ?? "未命名") : node.Name;
            UpdateUi(() =>
            {
                ProgressText.Text = $"正在检查 {i + 1}/{_targetNodes.Count}：{nodeName}";
                ProgressBar.Value = i;
            });

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
                    results.Add((node, maxPercent, dirs));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ExceptionLog.Write(ex, $"磁盘占用检查节点 {nodeName}");
                results.Add((node, -1, new List<(string, string)> { ($"错误: {ex.Message}", "") }));
            }
        }

        _completed = true;
        UpdateUi(() =>
        {
            ProgressText.Text = _cts?.IsCancellationRequested == true ? "已取消" : "检查完成";
            ProgressBar.Value = _targetNodes.Count;
            StartBtn.IsEnabled = true;
            ThresholdBox.IsEnabled = true;
            CancelBtn.Content = "关闭";
            BuildResultUi(results);
        });
    }

    private void BuildResultUi(List<(Node Node, double MaxPercent, IReadOnlyList<(string SizeText, string Path)> Dirs)> results)
    {
        ResultPanel.Children.Clear();
        if (results.Count == 0)
        {
            var noResult = new TextBlock
            {
                Text = "没有超过阈值的节点，或未采集到数据。",
                Foreground = (Brush)FindResource("TextSecondary"),
                Margin = new Thickness(0, 8, 0, 0)
            };
            ResultPanel.Children.Add(noResult);
            return;
        }

        foreach (var (node, maxPercent, dirs) in results)
        {
            var nodeName = string.IsNullOrEmpty(node.Name) ? (node.Config?.Host ?? "未命名") : node.Name;
            var headerText = maxPercent >= 0 ? $"{nodeName} — 磁盘占用 {maxPercent:F0}%" : $"{nodeName} — 采集失败";
            var header = new TextBlock
            {
                Text = headerText,
                Foreground = (Brush)FindResource("TextPrimary"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 12, 0, 4)
            };
            ResultPanel.Children.Add(header);
            var pathsPanel = new StackPanel { Margin = new Thickness(12, 0, 0, 12) };
            foreach (var (sizeText, path) in dirs)
            {
                if (string.IsNullOrEmpty(path)) continue;
                var line = new TextBlock
                {
                    Margin = new Thickness(0, 2, 0, 2),
                    Foreground = (Brush)FindResource("TextSecondary"),
                    Cursor = Cursors.Hand,
                    Tag = (node, path)
                };
                var run = new Run($"{sizeText}  {path}");
                line.Inlines.Add(run);
                line.MouseLeftButtonDown += (_, _) =>
                {
                    if (line.Tag is (Node n, string p) && !string.IsNullOrEmpty(p))
                    {
                        var cmd = "cd " + p + "\n";
                        _focusTabAndCopyCommand(n, cmd);
                    }
                };
                pathsPanel.Children.Add(line);
            }
            ResultPanel.Children.Add(pathsPanel);
        }
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_completed)
            Close();
        else
            _cts?.Cancel();
    }
}
