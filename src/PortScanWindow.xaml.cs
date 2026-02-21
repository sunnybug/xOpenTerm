using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using xOpenTerm.Models;
using xOpenTerm.Services;
using static xOpenTerm.Services.ExceptionLog;

namespace xOpenTerm;

/// <summary>端口扫描窗口：对所有主机节点（SSH/RDP）进行端口扫描，检测常用服务端口的开放状态。</summary>
public partial class PortScanWindow : Window
{
    private readonly List<Node> _targetNodes;
    private readonly IList<Node> _nodes;
    private readonly IList<Credential> _credentials;
    private readonly IList<Tunnel> _tunnels;
    private readonly AppSettings _settings;
    private readonly IStorageService _storage;
    private CancellationTokenSource? _cts;
    private bool _completed;
    private readonly Dictionary<string, Expander> _nodeResultExpanders; // 节点ID对应的Expander

    /// <summary>扫描完成事件（成功）</summary>
    public event EventHandler? ScanCompleted;

    /// <summary>扫描错误事件</summary>
    public event EventHandler<string>? ScanError;

    public PortScanWindow(
        List<Node> targetNodes,
        IList<Node> nodes,
        IList<Credential> credentials,
        IList<Tunnel> tunnels,
        AppSettings settings)
    {
        InitializeComponent();
        _targetNodes = targetNodes;
        _nodes = nodes;
        _credentials = credentials;
        _tunnels = tunnels;
        _settings = settings;
        _storage = new StorageService();
        _nodeResultExpanders = new Dictionary<string, Expander>();

        LoadSettings();
        InitializePresetCombo();
    }

    /// <summary>加载配置和历史记录</summary>
    private void LoadSettings()
    {
        // 加载默认配置
        TimeoutBox.Text = _settings.PortScanSettings.DefaultTimeoutSeconds.ToString();
        ConcurrencyBox.Text = _settings.PortScanSettings.DefaultConcurrency.ToString();
        DeepScanRadio.IsChecked = _settings.PortScanSettings.DefaultUseDeepScan;
        QuickScanRadio.IsChecked = !_settings.PortScanSettings.DefaultUseDeepScan;

        // 加载端口历史记录
        PortsCombo.ItemsSource = _settings.PortScanSettings.PortHistory;
        if (_settings.PortScanSettings.PortHistory.Count > 0)
            PortsCombo.Text = _settings.PortScanSettings.PortHistory[0];
        else
            PortsCombo.Text = "22,80,443,3306,3389,8080";
    }

    /// <summary>初始化端口预设下拉框</summary>
    private void InitializePresetCombo()
    {
        PresetCombo.Items.Clear();

        var presets = _settings.PortScanSettings.PortPresets;
        if (presets != null && presets.Count > 0)
        {
            foreach (var preset in presets)
            {
                PresetCombo.Items.Add(preset.Name);
            }

            // 恢复上次选择的预设
            var lastSelected = _settings.PortScanSettings.LastSelectedPreset;
            if (!string.IsNullOrEmpty(lastSelected))
            {
                var index = presets.FindIndex(p => p.Name == lastSelected);
                PresetCombo.SelectedIndex = index >= 0 ? index : 0;
            }
            else
            {
                PresetCombo.SelectedIndex = 0;
            }
        }
        else
        {
            // 向后兼容：如果配置中没有预设，使用硬编码的默认值
            ExceptionLog.WriteInfo("PortScanWindow.InitializePresetCombo: 配置中没有端口预设，使用硬编码默认值");
            PresetCombo.Items.Add("Top 20 常用端口");
            PresetCombo.Items.Add("Web 服务端口");
            PresetCombo.Items.Add("数据库端口");
            PresetCombo.Items.Add("SSH 常见端口");

            // 恢复上次选择的预设
            var lastSelected = _settings.PortScanSettings.LastSelectedPreset;
            if (!string.IsNullOrEmpty(lastSelected))
            {
                var index = PresetCombo.Items.IndexOf(lastSelected);
                PresetCombo.SelectedIndex = index >= 0 ? index : 0;
            }
            else
            {
                PresetCombo.SelectedIndex = 0;
            }
        }
    }

    /// <summary>保存配置和历史记录</summary>
    private void SaveSettings()
    {
        // 保存用户当前配置
        if (int.TryParse(TimeoutBox.Text, out var timeout))
            _settings.PortScanSettings.DefaultTimeoutSeconds = Math.Clamp(timeout, 1, 10);
        if (int.TryParse(ConcurrencyBox.Text, out var concurrency))
            _settings.PortScanSettings.DefaultConcurrency = Math.Clamp(concurrency, 1, 1000);
        _settings.PortScanSettings.DefaultUseDeepScan = DeepScanRadio.IsChecked == true;

        // 保存端口范围到历史记录
        var currentPorts = PortsCombo.Text.Trim();
        if (!string.IsNullOrEmpty(currentPorts) &&
            !_settings.PortScanSettings.PortHistory.Contains(currentPorts))
        {
            // 插入到最前面
            _settings.PortScanSettings.PortHistory.Insert(0, currentPorts);

            // 限制历史记录数量（最多20条）
            while (_settings.PortScanSettings.PortHistory.Count > 20)
                _settings.PortScanSettings.PortHistory.RemoveAt(20);
        }

        // 保存到文件
        _storage.SaveAppSettings(_settings);
    }

    /// <summary>端口预设选择变更</summary>
    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetCombo.SelectedItem == null) return;

        var selectedName = PresetCombo.SelectedItem.ToString();
        if (selectedName == null) return;

        var ports = GetPortsFromPreset(selectedName);

        if (!string.IsNullOrEmpty(ports))
        {
            PortsCombo.Text = ports;
        }

        // 保存用户选择的预设
        _settings.PortScanSettings.LastSelectedPreset = selectedName;
        _storage.SaveAppSettings(_settings);
    }

    /// <summary>根据预设名称获取端口列表</summary>
    private string GetPortsFromPreset(string presetName)
    {
        var presets = _settings.PortScanSettings.PortPresets;
        if (presets != null && presets.Count > 0)
        {
            var preset = presets.FirstOrDefault(p => p.Name == presetName);
            if (preset != null)
            {
                return preset.Ports;
            }
        }

        // 向后兼容：如果配置中没有找到预设，使用硬编码的默认值
        return presetName switch
        {
            "Top 20 常用端口" => string.Join(",", PortScanHelper.Top20Ports),
            "Web 服务端口" => string.Join(",", PortScanHelper.WebServicePorts),
            "数据库端口" => string.Join(",", PortScanHelper.DatabasePorts),
            "SSH 常见端口" => "22,2222",
            _ => ""
        };
    }

    /// <summary>按回车键直接开始扫描</summary>
    private void PortsCombo_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            StartBtn_Click(sender, new RoutedEventArgs());
        }
    }

    /// <summary>清除历史记录</summary>
    private void ClearHistoryBtn_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "确定清除所有端口范围历史记录？",
            "xOpenTerm",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _settings.PortScanSettings.PortHistory.Clear();
            PortsCombo.ItemsSource = null;
            PortsCombo.Text = "";
        }
    }

    /// <summary>管理预设按钮点击</summary>
    private void ManagePresetsBtn_Click(object sender, RoutedEventArgs e)
    {
        var manageWindow = new PortPresetManageWindow(_settings, _storage, InitializePresetCombo);
        manageWindow.Owner = this;
        manageWindow.ShowDialog();
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
        // 如果正在扫描，则停止扫描
        if (_cts != null && !_completed)
        {
            // 立即更新 UI 显示正在停止
            UpdateUi(() =>
            {
                StartBtn.IsEnabled = false;
                StartBtn.Content = "正在停止…";
                ProgressText.Text = "正在停止扫描…";
            });

            // 取消扫描
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
            _completed = true;

            // 恢复 UI 状态
            UpdateUi(() =>
            {
                StartBtn.Content = "开始扫描";
                StartBtn.IsEnabled = true;
                PresetCombo.IsEnabled = true;
                PortsCombo.IsEnabled = true;
                TimeoutBox.IsEnabled = true;
                ConcurrencyBox.IsEnabled = true;
                SaveConfigCheckBox.IsEnabled = true;
            });
            return;
        }

        try
        {
            WriteInfo("PortScanWindow.StartBtn_Click: 开始执行扫描按钮点击");

            // 验证端口输入
            WriteInfo($"PortScanWindow.StartBtn_Click: 解析端口输入: {PortsCombo.Text}");
            List<int> ports;
            try
            {
                ports = PortScanHelper.ParsePortInput(PortsCombo.Text);
                if (ports.Count == 0)
                {
                    WriteInfo("PortScanWindow.StartBtn_Click: 端口列表为空，退出");
                    MessageBox.Show("请输入有效的端口范围。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                WriteInfo($"PortScanWindow.StartBtn_Click: 解析到 {ports.Count} 个端口");
            }
            catch (Exception ex)
            {
                WriteInfo($"PortScanWindow.StartBtn_Click: 端口输入格式错误: {ex.Message}");
                MessageBox.Show($"端口输入格式错误：{ex.Message}", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 验证超时设置
            if (!int.TryParse(TimeoutBox.Text, out var timeout) || timeout < 1 || timeout > 10)
            {
                MessageBox.Show("超时时间必须在 1-10 秒之间。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 验证并发设置
            if (!int.TryParse(ConcurrencyBox.Text, out var concurrency) || concurrency < 1 || concurrency > 1000)
            {
                MessageBox.Show("并发节点数必须在 1-1000 之间。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 保存配置（如果勾选）
            if (SaveConfigCheckBox.IsChecked == true)
            {
                SaveSettings();
            }

            // 获取所有可扫描的主机节点（SSH 和 RDP）
            var targetNodes = _targetNodes.Where(n => n.Type == NodeType.ssh || n.Type == NodeType.rdp).ToList();
            WriteInfo($"PortScanWindow.StartBtn_Click: 筛选主机节点: {_targetNodes.Count} 个总节点, {targetNodes.Count} 个主机节点");
            if (targetNodes.Count == 0)
            {
                WriteInfo("PortScanWindow.StartBtn_Click: 没有主机节点可扫描");
                MessageBox.Show("没有可扫描的主机节点（SSH/RDP）。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 开始扫描
            WriteInfo("PortScanWindow.StartBtn_Click: 开始扫描流程");
            StartBtn.Content = "停止扫描";
            StartBtn.IsEnabled = true;
            PresetCombo.IsEnabled = false;
            PortsCombo.IsEnabled = false;
            TimeoutBox.IsEnabled = false;
            ConcurrencyBox.IsEnabled = false;
            SaveConfigCheckBox.IsEnabled = false;
            ProgressText.Visibility = Visibility.Visible;
            ProgressText.Text = "正在准备扫描…"; // 重置进度文本
            ProgressBar.Visibility = Visibility.Visible;
            ProgressBar.Maximum = targetNodes.Count;
            ProgressBar.Value = 0;
            ResultPanel.Children.Clear();
            _nodeResultExpanders.Clear();
            _completed = false;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var completed = 0;
            var useDeepScan = DeepScanRadio.IsChecked == true;

            // 大量端口时提示用户
                if (ports.Count > 100 && useDeepScan)
            {
                UpdateUi(() =>
                {
                    var result = MessageBox.Show(
                        $"将扫描 {ports.Count} 个端口，深度扫描可能耗时较久。\n是否使用深度扫描识别服务？\n\n是=深度扫描（准确但慢）\n否=快速扫描（仅检测开放）",
                        "xOpenTerm",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    useDeepScan = result == MessageBoxResult.Yes;
                });
            }

            try
            {
                var results = new ConcurrentBag<NodeScanResult>();
                var totalSw = Stopwatch.StartNew();

                    // 计划扫描的总端口数（用于统计显示）
                var plannedTotalPorts = targetNodes.Count * ports.Count;
                var actuallyScannedPorts = 0;

                // 按节点顺序扫描，每个节点的端口并发扫描
                foreach (var node in targetNodes)
                {
                    token.ThrowIfCancellationRequested();

                    // 统一使用本地端口扫描（从本机发起 TCP 连接）
                    // SSH 和 RDP 节点都使用相同的方式扫描
                    var result = await ScanLocalNodeAsync(node, ports, timeout, token, concurrency,
                        (node, portResult) =>
                        {
                            // 实时更新UI回调
                            UpdateUi(() => AddPortResultToNodeExpander(node, portResult));
                        });

                    if (result != null)
                    {
                        results.Add(result);
                        actuallyScannedPorts += result.Ports.Count;
                        // 扫描完成后更新Expander头部
                        UpdateUi(() => UpdateNodeExpanderCompleted(result.Node, result.Ports, result.Duration));
                    }

                    // 更新进度
                    completed++;
                    UpdateUi(() =>
                    {
                        ProgressText.Text = $"正在扫描… 已完成 {completed}/{targetNodes.Count} 节点";
                        ProgressBar.Value = completed;
                    });
                }

                totalSw.Stop();

                // 显示结果统计
                UpdateUi(() =>
                {
                    AddResultSummary(results.ToList(), actuallyScannedPorts, plannedTotalPorts, totalSw.Elapsed);
                    ProgressText.Text = $"扫描完成，共扫描 {targetNodes.Count} 个节点，{actuallyScannedPorts}/{plannedTotalPorts} 端口，耗时 {totalSw.Elapsed.TotalSeconds:F1} 秒";
                    ProgressBar.Value = targetNodes.Count;
                });

                // 触发扫描完成事件
                ScanCompleted?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                UpdateUi(() =>
                {
                    ProgressText.Text = "已取消";
                });
            }
            catch (Exception ex)
            {
                Write(ex, "PortScanWindow.StartBtn_Click");
                var errorMessage = ex.Message;
                UpdateUi(() =>
                {
                    ProgressText.Text = $"扫描出错：{errorMessage}";
                });

                // 触发错误事件
                ScanError?.Invoke(this, errorMessage);
            }
            finally
            {
                _completed = true;
                UpdateUi(() =>
                {
                    StartBtn.Content = "开始扫描";
                    StartBtn.IsEnabled = true;
                    PresetCombo.IsEnabled = true;
                    PortsCombo.IsEnabled = true;
                    TimeoutBox.IsEnabled = true;
                    ConcurrencyBox.IsEnabled = true;
                    SaveConfigCheckBox.IsEnabled = true;
                    CancelBtn.Content = "关闭";
                });
            }
        }
        catch (Exception ex)
        {
            Write(ex, "PortScanWindow.StartBtn_Click (Outer)");
        }
    }

    /// <summary>扫描节点的所有端口（从本机发起 TCP 连接，适用于 SSH 和 RDP 节点）</summary>
    private async Task<NodeScanResult?> ScanLocalNodeAsync(
        Node node,
        List<int> ports,
        int timeout,
        CancellationToken token,
        int portConcurrency,
        Action<Node, PortResult>? onPortScanned = null)
    {
        var nodeName = string.IsNullOrEmpty(node.Name) ? (node.Config?.Host ?? "未命名") : node.Name;
        try
        {
            WriteInfo($"PortScanWindow.ScanLocalNodeAsync: 开始扫描节点 {nodeName}");

            // 在开始扫描前创建Expander（如果需要实时更新）
            if (onPortScanned != null)
            {
                UpdateUi(() => GetOrCreateNodeExpander(node, ports.Count));
            }

            var host = node.Config?.Host ?? "";
            if (string.IsNullOrEmpty(host))
            {
                throw new Exception("节点配置中没有主机地址");
            }

            var sw = Stopwatch.StartNew();
            var timeoutMillis = timeout * 1000;

            // 使用本地端口扫描器
            var progress = new Progress<(int Port, PortResult Result)>(p =>
            {
                // 实时回调更新UI
                onPortScanned?.Invoke(node, p.Result);
            });
            var portResults = await LocalPortScanner.ScanPortsAsync(
                host, ports, timeoutMillis, portConcurrency, progress, token);

            sw.Stop();
            return new NodeScanResult(node, portResults, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            WriteInfo($"PortScanWindow.ScanLocalNodeAsync: 节点 {nodeName} 扫描被取消");
            return null;
        }
        catch (Exception ex)
        {
            Write(ex, $"PortScanWindow.ScanLocalNodeAsync: 节点 {nodeName} 扫描失败");

            // 返回错误结果
            var errorResult = new NodeScanResult(node, new List<PortResult>
            {
                new PortResult(0, false, $"扫描失败：{ex.Message}", null)
            }, TimeSpan.Zero);

            return errorResult;
        }
    }

    /// <summary>添加结果统计信息到结果面板顶部</summary>
    private void AddResultSummary(List<NodeScanResult> results, int actuallyScannedPorts, int plannedTotalPorts, TimeSpan totalDuration)
    {
        // 如果已经添加过统计信息，先移除
        if (ResultPanel.Children.Count > 0 && ResultPanel.Children[0] is TextBlock firstChild && firstChild.Text.StartsWith("扫描统计"))
        {
            ResultPanel.Children.RemoveAt(0);
        }

        // 统计信息
        var openPorts = results.Sum(r => r.Ports.Count(p => p.IsOpen));
        var statsText = new TextBlock
        {
            Text = $"扫描统计：共 {results.Count} 个节点，{actuallyScannedPorts}/{plannedTotalPorts} 端口，发现 {openPorts} 个开放端口",
            Foreground = (Brush)FindResource("TextSecondary"),
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = System.Windows.FontWeights.SemiBold
        };
        ResultPanel.Children.Insert(0, statsText);
    }

    /// <summary>获取或创建节点的Expander（用于实时更新）</summary>
    private Expander GetOrCreateNodeExpander(Node node, int totalPorts)
    {
        var nodeId = node.Id ?? Guid.NewGuid().ToString();

        if (_nodeResultExpanders.TryGetValue(nodeId, out var existingExpander))
        {
            return existingExpander;
        }

        var nodeName = string.IsNullOrEmpty(node.Name) ? (node.Config?.Host ?? "未命名") : node.Name;
        var host = node.Config?.Host ?? "";
        var headerText = $"{nodeName} ({host}) — 正在扫描...";

        var contentPanel = new StackPanel { Margin = new Thickness(12, 0, 0, 8) };

        var expander = new Expander
        {
            Header = headerText,
            Content = contentPanel,
            IsExpanded = false, // 默认折叠
            Foreground = (Brush)FindResource("TextPrimary"),
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(0, 4, 0, 4),
            Tag = new NodeExpanderData { NodeId = nodeId, TotalPorts = totalPorts, ScannedCount = 0, OpenCount = 0 }
        };

        // 右键菜单：连接功能
        var menu = new ContextMenu();
        var connectItem = new MenuItem { Header = "连接(_L)" };
        connectItem.Click += (_, _) =>
        {
            // 在主窗口中打开连接（需要传入主窗口的引用）
            // 这里暂时留空，因为当前没有主窗口引用
        };
        menu.Items.Add(connectItem);
        expander.ContextMenu = menu;

        _nodeResultExpanders[nodeId] = expander;
        ResultPanel.Children.Add(expander);

        return expander;
    }

    /// <summary>实时添加端口扫描结果到节点Expander</summary>
    private void AddPortResultToNodeExpander(Node node, PortResult portResult)
    {
        var expander = GetOrCreateNodeExpander(node, 0);
        var data = (NodeExpanderData)expander.Tag;
        var contentPanel = (StackPanel)expander.Content;

        // 更新统计数据
        data.ScannedCount++;
        if (portResult.IsOpen)
            data.OpenCount++;

        // 更新Expander头部
        var nodeName = string.IsNullOrEmpty(node.Name) ? (node.Config?.Host ?? "未命名") : node.Name;
        var host = node.Config?.Host ?? "";
        expander.Header = $"{nodeName} ({host}) — 扫描中 {data.ScannedCount}/{data.TotalPorts}，开放 {data.OpenCount}";

        // 检查是否为错误结果
        if (portResult.Port == 0 && !portResult.IsOpen)
        {
            // 错误信息，不添加端口详情
            return;
        }

        // 添加端口结果
        var portText = new TextBlock
        {
            Margin = new Thickness(0, 2, 0, 2)
        };

        if (portResult.IsOpen)
        {
            portText.Text = $"端口 {portResult.Port}: 开放 ({portResult.Service})";
            portText.Foreground = (Brush)FindResource("Accent");

            // 如果有 banner，添加可展开的详情
            if (!string.IsNullOrEmpty(portResult.Banner))
            {
                var bannerPreview = portResult.Banner.Length > 100
                    ? portResult.Banner.Substring(0, 100) + "..."
                    : portResult.Banner;
                var bannerText = new TextBlock
                {
                    Text = $"  Banner: {bannerPreview.Replace("\r", "").Replace("\n", " ")}",
                    Foreground = (Brush)FindResource("TextSecondary"),
                    Margin = new Thickness(16, 0, 0, 2)
                };
                contentPanel.Children.Add(bannerText);
            }
        }
        else
        {
            portText.Text = $"端口 {portResult.Port}: 关闭";
            portText.Foreground = (Brush)FindResource("TextSecondary");
        }

        contentPanel.Children.Add(portText);
    }

    /// <summary>节点扫描完成后更新Expander头部</summary>
    private void UpdateNodeExpanderCompleted(Node node, List<PortResult> ports, TimeSpan duration)
    {
        var nodeId = node.Id ?? Guid.NewGuid().ToString();
        if (!_nodeResultExpanders.TryGetValue(nodeId, out var expander))
            return;

        var data = (NodeExpanderData)expander.Tag;
        var nodeName = string.IsNullOrEmpty(node.Name) ? (node.Config?.Host ?? "未命名") : node.Name;
        var host = node.Config?.Host ?? "";
        var openCount = ports.Count(p => p.IsOpen);

        expander.Header = $"{nodeName} ({host}) — {openCount}/{ports.Count} 端口开放 ({duration.TotalSeconds:F1}s)";
        expander.IsExpanded = false; // 默认保持折叠

        // 根据是否有开放端口设置颜色
        // 有开放端口：红色；无开放端口：绿色
        if (openCount > 0)
        {
            expander.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38)); // 红色
        }
        else
        {
            expander.Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94)); // 绿色
        }
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        // 如果正在扫描，则取消扫描
        if (_cts != null && !_completed)
        {
            _cts.Cancel();
        }
        // 否则（未开始扫描或已完成）直接关闭窗口
        else
        {
            Close();
        }
    }

    /// <summary>窗口关闭时保存配置</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (SaveConfigCheckBox.IsChecked == true)
        {
            SaveSettings();
        }
        base.OnClosing(e);
    }

    /// <summary>自动开始扫描（用于测试模式）</summary>
    public void AutoStartScan()
    {
        try
        {
            WriteInfo("PortScanWindow.AutoStartScan: 调用自动开始扫描");

            // 检查必要的控件是否已初始化
            if (StartBtn == null)
            {
                WriteInfo("PortScanWindow.AutoStartScan: StartBtn 为 null，无法开始扫描");
                return;
            }

            if (PortsCombo == null || string.IsNullOrEmpty(PortsCombo.Text))
            {
                WriteInfo("PortScanWindow.AutoStartScan: PortsCombo 为 null 或为空");
                return;
            }

            WriteInfo($"PortScanWindow.AutoStartScan: 准备扫描端口: {PortsCombo.Text}");

            // 检查是否有可扫描的节点
            if (_targetNodes == null || _targetNodes.Count == 0)
            {
                WriteInfo("PortScanWindow.AutoStartScan: 没有可扫描的节点");
                return;
            }

            var targetNodes = _targetNodes.Where(n => n.Type == NodeType.ssh || n.Type == NodeType.rdp).ToList();
            WriteInfo($"PortScanWindow.AutoStartScan: 找到 {targetNodes.Count} 个主机节点");

            // 模拟点击开始按钮
            WriteInfo("PortScanWindow.AutoStartScan: 触发 StartBtn_Click");
            StartBtn_Click(this, new RoutedEventArgs());
        }
        catch (Exception ex)
        {
            Write(ex, "PortScanWindow.AutoStartScan");
        }
    }
}

/// <summary>节点Expander数据（用于实时更新）</summary>
public class NodeExpanderData
{
    public string NodeId { get; set; } = string.Empty;
    public int TotalPorts { get; set; }
    public int ScannedCount { get; set; }
    public int OpenCount { get; set; }
}
