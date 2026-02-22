using System.Collections.Concurrent;
using System.Collections.ObjectModel;
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

/// <summary>端口扫描目标项：用于列表显示，可来自树节点或手动添加。</summary>
public class PortScanTargetItem
{
    /// <summary>显示名称（列表行）；名称为空时仅显示主机与类型</summary>
    public string DisplayLine => string.IsNullOrEmpty(Name) ? $"{Host} ({TypeLabel})" : $"{Name} — {Host} ({TypeLabel})";

    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public NodeType Type { get; set; }
    public string TypeLabel => Type == NodeType.ssh ? "SSH" : Type == NodeType.rdp ? "RDP" : "主机";

    /// <summary>若来自服务器树则为对应 Node，否则为 null（手动添加）。</summary>
    public Node? Node { get; set; }

    /// <summary>转为扫描用的 Node（手动项会生成临时 Node）。</summary>
    public Node ToScanNode()
    {
        if (Node != null)
            return Node;
        return new Node
        {
            Id = Guid.NewGuid().ToString(),
            Name = Name,
            Type = Type,
            Config = new ConnectionConfig { Host = Host }
        };
    }
}

/// <summary>端口扫描窗口：对所有主机节点（SSH/RDP）进行端口扫描，检测常用服务端口的开放状态。</summary>
public partial class PortScanWindow : Window
{
    private readonly ObservableCollection<PortScanTargetItem> _targetItems;
    private readonly IList<Node> _nodes;
    private readonly IList<Credential> _credentials;
    private readonly IList<Tunnel> _tunnels;
    private readonly AppSettings _settings;
    private readonly IStorageService _storage;
    /// <summary>为 true 时不弹出确认框且仅扫描常用端口，用于自动化测试。</summary>
    private readonly bool _noPrompts;

    /// <summary>测试模式下的常用端口（仅扫描这些，保证测试快速无交互）。</summary>
    private const string TestModePorts = "22,80,443,3389,8080";
    private CancellationTokenSource? _cts;
    private bool _completed;
    /// <summary>每个目标对应一个 Expander，顺序与 _targetItems 一致，用于合并目标列表与扫描结果。</summary>
    private readonly List<Expander> _targetExpanders;
    private int _selectedTargetIndex = -1;

    /// <summary>扫描完成事件（成功）</summary>
    public event EventHandler? ScanCompleted;

    /// <summary>扫描错误事件</summary>
    public event EventHandler<string>? ScanError;

    public PortScanWindow(
        List<Node> targetNodes,
        IList<Node> nodes,
        IList<Credential> credentials,
        IList<Tunnel> tunnels,
        AppSettings settings,
        bool noPrompts = false)
    {
        InitializeComponent();
        _targetItems = new ObservableCollection<PortScanTargetItem>();
        _nodes = nodes;
        _credentials = credentials;
        _tunnels = tunnels;
        _settings = settings;
        _storage = new StorageService();
        _noPrompts = noPrompts;
        _targetExpanders = new List<Expander>();

        // 从传入的节点初始化目标列表
        foreach (var n in targetNodes)
        {
            var host = n.Config?.Host ?? "";
            if (string.IsNullOrEmpty(host)) continue;
            _targetItems.Add(new PortScanTargetItem
            {
                Name = string.IsNullOrEmpty(n.Name) ? host : n.Name,
                Host = host,
                Type = n.Type == NodeType.ssh || n.Type == NodeType.rdp ? n.Type : NodeType.ssh,
                Node = n
            });
        }

        RefreshMergedPanel();
        UpdateTargetButtonsState();

        LoadSettings();
        InitializePresetCombo();
        if (_noPrompts)
            PortsCombo.Text = TestModePorts;

        Closed += (_, _) => (Application.Current.MainWindow as MainWindow)?.BringMainWindowToFront();
    }

    /// <summary>加载配置和历史记录</summary>
    private void LoadSettings()
    {
        // 加载默认配置
        TimeoutBox.Text = _settings.PortScanSettings.DefaultTimeoutSeconds.ToString();
        ConcurrencyBox.Text = _settings.PortScanSettings.DefaultConcurrency.ToString();
        DeepScanRadio.IsChecked = _settings.PortScanSettings.DefaultUseDeepScan;
        QuickScanRadio.IsChecked = !_settings.PortScanSettings.DefaultUseDeepScan;

        // 加载端口历史记录（测试模式固定为常用端口）
        PortsCombo.ItemsSource = _settings.PortScanSettings.PortHistory;
        if (_noPrompts)
            PortsCombo.Text = TestModePorts;
        else if (_settings.PortScanSettings.PortHistory.Count > 0)
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

            // 测试模式不恢复预设，避免“所有端口”等覆盖常用端口
            if (_noPrompts) return;

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

            if (_noPrompts) return;

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
        (Application.Current.MainWindow as MainWindow)?.BringMainWindowToFront();
    }

    /// <summary>管理预设按钮点击</summary>
    private void ManagePresetsBtn_Click(object sender, RoutedEventArgs e)
    {
        var manageWindow = new PortPresetManageWindow(_settings, _storage, InitializePresetCombo);
        manageWindow.Owner = this;
        manageWindow.ShowDialog();
        (Application.Current.MainWindow as MainWindow)?.BringMainWindowToFront();
    }

    /// <summary>根据 _targetItems 刷新“目标与扫描结果”合并列表，每个目标一个 Expander。</summary>
    private void RefreshMergedPanel()
    {
        MergedPanel.Children.Clear();
        _targetExpanders.Clear();
        for (var i = 0; i < _targetItems.Count; i++)
        {
            var item = _targetItems[i];
            var index = i;
            var contentPanel = new StackPanel { Margin = new Thickness(12, 0, 0, 8) };
            var expander = new Expander
            {
                Header = item.DisplayLine,
                Content = contentPanel,
                IsExpanded = false,
                Foreground = (Brush)FindResource("TextPrimary"),
                Margin = new Thickness(0, 4, 0, 0),
                Padding = new Thickness(0, 4, 0, 4),
                Tag = index
            };
            // 展开时视为选中该目标
            expander.Expanded += (_, _) =>
            {
                _selectedTargetIndex = index;
                UpdateTargetButtonsState();
            };
            _targetExpanders.Add(expander);
            MergedPanel.Children.Add(expander);
        }
    }

    private void UpdateTargetButtonsState()
    {
        var hasSelection = _selectedTargetIndex >= 0 && _selectedTargetIndex < _targetItems.Count;
        TargetEditBtn.IsEnabled = hasSelection;
        TargetRemoveBtn.IsEnabled = hasSelection;
    }

    /// <summary>添加目标：弹窗输入主机、显示名、类型</summary>
    private void TargetAddBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ShowTargetEditDialog(this, isAdd: true, name: "", host: "", type: NodeType.ssh, out var outName, out var outHost, out var outType))
        {
            _targetItems.Add(new PortScanTargetItem { Name = outName, Host = outHost, Type = outType, Node = null });
            RefreshMergedPanel();
        }
    }

    /// <summary>编辑目标：仅可改显示名与主机（手动项可改类型）</summary>
    private void TargetEditBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTargetIndex < 0 || _selectedTargetIndex >= _targetItems.Count) return;
        var item = _targetItems[_selectedTargetIndex];
        if (!ShowTargetEditDialog(this, isAdd: false, item.Name, item.Host, item.Type, out var outName, out var outHost, out var outType))
            return;
        item.Name = outName;
        item.Host = outHost;
        item.Type = outType;
        // 更新对应 Expander 标题
        if (_selectedTargetIndex < _targetExpanders.Count)
        {
            _targetExpanders[_selectedTargetIndex].Header = item.DisplayLine;
        }
    }

    /// <summary>删除选中的目标</summary>
    private void TargetRemoveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTargetIndex < 0 || _selectedTargetIndex >= _targetItems.Count) return;
        _targetItems.RemoveAt(_selectedTargetIndex);
        _selectedTargetIndex = -1;
        RefreshMergedPanel();
        UpdateTargetButtonsState();
    }

    /// <summary>显示添加/编辑目标对话框，返回是否确认及输出名称、主机、类型。</summary>
    private static bool ShowTargetEditDialog(Window owner, bool isAdd, string name, string host, NodeType type,
        out string outName, out string outHost, out NodeType outType)
    {
        outName = name;
        outHost = host;
        outType = type;

        var title = isAdd ? "添加目标服务器" : "编辑目标服务器";
        var win = new Window
        {
            Owner = owner,
            Title = title,
            Width = 380,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)Application.Current.FindResource("BgDark")
        };

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var textSecondary = (Brush)Application.Current.FindResource("TextSecondary");
        var textPrimary = (Brush)Application.Current.FindResource("TextPrimary");

        var nameLabel = new TextBlock { Text = "显示名称（可选）：", Foreground = textSecondary, Margin = new Thickness(0, 0, 0, 4) };
        var nameBox = new System.Windows.Controls.TextBox
        {
            Text = name,
            Margin = new Thickness(0, 0, 0, 8),
            Style = (Style)Application.Current.FindResource("DialogTextBoxStyle")
        };
        Grid.SetRow(nameLabel, 0);
        Grid.SetRow(nameBox, 1);

        var hostLabel = new TextBlock { Text = "主机地址：", Foreground = textSecondary, Margin = new Thickness(0, 0, 0, 4) };
        var hostBox = new System.Windows.Controls.TextBox
        {
            Text = host,
            Margin = new Thickness(0, 0, 0, 8),
            Style = (Style)Application.Current.FindResource("DialogTextBoxStyle")
        };
        Grid.SetRow(hostLabel, 2);
        Grid.SetRow(hostBox, 3);

        var typeLabel = new TextBlock { Text = "类型：", Foreground = textSecondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var typeCombo = new System.Windows.Controls.ComboBox
        {
            Margin = new Thickness(0, 0, 0, 8),
            Background = (Brush)Application.Current.FindResource("BgInput"),
            Foreground = textPrimary,
            BorderBrush = (Brush)Application.Current.FindResource("BorderBrush"),
            BorderThickness = new Thickness(1, 1, 1, 1),
            Padding = new Thickness(6, 4, 6, 4),
            MinWidth = 100
        };
        typeCombo.Items.Add("SSH");
        typeCombo.Items.Add("RDP");
        typeCombo.SelectedIndex = type == NodeType.rdp ? 1 : 0;
        var typePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        typePanel.Children.Add(typeLabel);
        typePanel.Children.Add(typeCombo);
        Grid.SetRow(typePanel, 4);

        grid.Children.Add(nameLabel);
        grid.Children.Add(nameBox);
        grid.Children.Add(hostLabel);
        grid.Children.Add(hostBox);
        grid.Children.Add(typePanel);

        var ok = new Button
        {
            Content = "确定",
            IsDefault = true,
            Style = (Style)Application.Current.FindResource("PrimaryButtonStyle"),
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var cancel = new Button
        {
            Content = "取消",
            IsCancel = true,
            Style = (Style)Application.Current.FindResource("SecondaryButtonStyle"),
            Padding = new Thickness(16, 6, 16, 6)
        };
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        btnPanel.Children.Add(ok);
        btnPanel.Children.Add(cancel);
        Grid.SetRow(btnPanel, 5);
        grid.Children.Add(btnPanel);

        bool confirmed = false;
        var resultName = outName;
        var resultHost = outHost;
        var resultType = outType;
        ok.Click += (_, _) =>
        {
            var h = (hostBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(h))
            {
                MessageBox.Show("请输入主机地址。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
                (Application.Current.MainWindow as MainWindow)?.BringMainWindowToFront();
                return;
            }
            resultName = (nameBox.Text ?? "").Trim();
            resultHost = h;
            resultType = typeCombo.SelectedIndex == 1 ? NodeType.rdp : NodeType.ssh;
            confirmed = true;
            win.Close();
        };
        cancel.Click += (_, _) => win.Close();

        win.Content = grid;
        win.ShowDialog();
        (Application.Current.MainWindow as MainWindow)?.BringMainWindowToFront();

        if (confirmed)
        {
            outName = resultName;
            outHost = resultHost;
            outType = resultType;
        }
        return confirmed;
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
                TargetAddBtn.IsEnabled = true;
                UpdateTargetButtonsState();
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
                    (Application.Current.MainWindow as MainWindow)?.BringMainWindowToFront();
                    return;
                }
                WriteInfo($"PortScanWindow.StartBtn_Click: 解析到 {ports.Count} 个端口");
            }
            catch (Exception ex)
            {
                WriteInfo($"PortScanWindow.StartBtn_Click: 端口输入格式错误: {ex.Message}");
                MessageBox.Show($"端口输入格式错误：{ex.Message}", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
                (Application.Current.MainWindow as MainWindow)?.BringMainWindowToFront();
                return;
            }

            // 验证超时设置
            if (!int.TryParse(TimeoutBox.Text, out var timeout) || timeout < 1 || timeout > 10)
            {
                MessageBox.Show("超时时间必须在 1-10 秒之间。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
                (Application.Current.MainWindow as MainWindow)?.BringMainWindowToFront();
                return;
            }

            // 验证并发设置
            if (!int.TryParse(ConcurrencyBox.Text, out var concurrency) || concurrency < 1 || concurrency > 1000)
            {
                MessageBox.Show("并发节点数必须在 1-1000 之间。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
                (Application.Current.MainWindow as MainWindow)?.BringMainWindowToFront();
                return;
            }

            // 保存配置（如果勾选）
            if (SaveConfigCheckBox.IsChecked == true)
            {
                SaveSettings();
            }

            // 从目标列表获取可扫描的主机节点（SSH 和 RDP）
            var targetNodes = _targetItems.Select(t => t.ToScanNode()).Where(n => n.Type == NodeType.ssh || n.Type == NodeType.rdp).ToList();
            WriteInfo($"PortScanWindow.StartBtn_Click: 筛选主机节点: {_targetItems.Count} 个目标, {targetNodes.Count} 个主机节点");
            if (targetNodes.Count == 0)
            {
                WriteInfo("PortScanWindow.StartBtn_Click: 没有主机节点可扫描");
                MessageBox.Show("没有可扫描的主机节点（SSH/RDP）。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Information);
                (Application.Current.MainWindow as MainWindow)?.BringMainWindowToFront();
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
            TargetAddBtn.IsEnabled = false;
            TargetEditBtn.IsEnabled = false;
            TargetRemoveBtn.IsEnabled = false;
            ProgressText.Visibility = Visibility.Visible;
            ProgressText.Text = "正在准备扫描…"; // 重置进度文本
            ProgressBar.Visibility = Visibility.Visible;
            ProgressBar.Maximum = targetNodes.Count;
            ProgressBar.Value = 0;
            // 可扫描目标在 _targetItems 中的下标，与 targetNodes 一一对应
            var targetIndices = new List<int>();
            for (var i = 0; i < _targetItems.Count; i++)
            {
                if (_targetItems[i].Type == NodeType.ssh || _targetItems[i].Type == NodeType.rdp)
                    targetIndices.Add(i);
            }
            _completed = false;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var completed = 0;
            var useDeepScan = DeepScanRadio.IsChecked == true;

            // 大量端口时提示用户（无交互模式如测试时不弹窗，直接使用快速扫描）
            if (ports.Count > 100 && useDeepScan)
            {
                if (_noPrompts)
                    useDeepScan = false;
                else
                {
                    UpdateUi(() =>
                    {
                        var result = MessageBox.Show(
                            $"将扫描 {ports.Count} 个端口，深度扫描可能耗时较久。\n是否使用深度扫描识别服务？\n\n是=深度扫描（准确但慢）\n否=快速扫描（仅检测开放）",
                            "xOpenTerm",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);
                        useDeepScan = result == MessageBoxResult.Yes;
                        (Application.Current.MainWindow as MainWindow)?.BringMainWindowToFront();
                    });
                }
            }

            try
            {
                var results = new ConcurrentBag<NodeScanResult>();
                var totalSw = Stopwatch.StartNew();

                    // 计划扫描的总端口数（用于统计显示）
                var plannedTotalPorts = targetNodes.Count * ports.Count;
                var actuallyScannedPorts = 0;

                // 按节点顺序扫描，每个节点的端口并发扫描
                for (var j = 0; j < targetNodes.Count; j++)
                {
                    token.ThrowIfCancellationRequested();
                    var node = targetNodes[j];
                    var targetIndex = targetIndices[j];

                    // 统一使用本地端口扫描（从本机发起 TCP 连接）
                    var result = await ScanLocalNodeAsync(node, ports, timeout, token, concurrency, targetIndex,
                        (idx, n, portResult) =>
                        {
                            UpdateUi(() => AddPortResultToNodeExpander(idx, n, portResult));
                        });

                    if (result != null)
                    {
                        results.Add(result);
                        actuallyScannedPorts += result.Ports.Count;
                        UpdateUi(() => UpdateNodeExpanderCompleted(targetIndex, result.Node, result.Ports, result.Duration));
                    }

                    completed++;
                    UpdateUi(() =>
                    {
                        ProgressText.Text = $"正在扫描… 已完成 {completed}/{targetNodes.Count} 节点";
                        ProgressBar.Value = completed;
                    });
                }

                totalSw.Stop();

                // 显示结果统计（插入到合并面板顶部）
                UpdateUi(() =>
                {
                    AddResultSummary(MergedPanel, results.ToList(), actuallyScannedPorts, plannedTotalPorts, totalSw.Elapsed);
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
                TargetAddBtn.IsEnabled = true;
                CancelBtn.Content = "关闭";
                UpdateTargetButtonsState();
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
        int targetIndex,
        Action<int, Node, PortResult>? onPortScanned = null)
    {
        var nodeName = string.IsNullOrEmpty(node.Name) ? (node.Config?.Host ?? "未命名") : node.Name;
        try
        {
            WriteInfo($"PortScanWindow.ScanLocalNodeAsync: 开始扫描节点 {nodeName}");

            if (onPortScanned != null)
            {
                UpdateUi(() => PrepareNodeExpanderForScan(targetIndex, node, ports.Count));
            }

            var host = node.Config?.Host ?? "";
            if (string.IsNullOrEmpty(host))
            {
                throw new Exception("节点配置中没有主机地址");
            }

            var sw = Stopwatch.StartNew();
            var timeoutMillis = timeout * 1000;

            var progress = new Progress<(int Port, PortResult Result)>(p =>
            {
                onPortScanned?.Invoke(targetIndex, node, p.Result);
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

    /// <summary>在合并面板顶部添加结果统计信息</summary>
    private void AddResultSummary(StackPanel mergedPanel, List<NodeScanResult> results, int actuallyScannedPorts, int plannedTotalPorts, TimeSpan totalDuration)
    {
        if (mergedPanel.Children.Count > 0 && mergedPanel.Children[0] is TextBlock firstChild && firstChild.Text.StartsWith("扫描统计"))
        {
            mergedPanel.Children.RemoveAt(0);
        }

        var openPorts = results.Sum(r => r.Ports.Count(p => p.IsOpen));
        var statsText = new TextBlock
        {
            Text = $"扫描统计：共 {results.Count} 个节点，{actuallyScannedPorts}/{plannedTotalPorts} 端口，发现 {openPorts} 个开放端口",
            Foreground = (Brush)FindResource("TextSecondary"),
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = System.Windows.FontWeights.SemiBold
        };
        mergedPanel.Children.Insert(0, statsText);
    }

    /// <summary>开始扫描前准备目标对应的 Expander（清空内容、显示“正在扫描”）</summary>
    private void PrepareNodeExpanderForScan(int targetIndex, Node node, int totalPorts)
    {
        if (targetIndex < 0 || targetIndex >= _targetExpanders.Count) return;

        var nodeName = string.IsNullOrEmpty(node.Name) ? (node.Config?.Host ?? "未命名") : node.Name;
        var host = node.Config?.Host ?? "";
        var contentPanel = new StackPanel { Margin = new Thickness(12, 0, 0, 8) };
        var expander = _targetExpanders[targetIndex];
        expander.Header = $"{nodeName} ({host}) — 正在扫描...";
        expander.Content = contentPanel;
        expander.Foreground = (Brush)FindResource("TextPrimary");
        expander.Tag = new NodeExpanderData { NodeId = node.Id ?? "", TotalPorts = totalPorts, ScannedCount = 0, OpenCount = 0 };
    }

    /// <summary>实时添加端口扫描结果到对应目标的 Expander</summary>
    private void AddPortResultToNodeExpander(int targetIndex, Node node, PortResult portResult)
    {
        if (targetIndex < 0 || targetIndex >= _targetExpanders.Count) return;
        var expander = _targetExpanders[targetIndex];
        if (expander.Tag is not NodeExpanderData data) return;
        var contentPanel = expander.Content as StackPanel;
        if (contentPanel == null) return;

        // 更新统计数据
        data.ScannedCount++;
        if (portResult.IsOpen)
            data.OpenCount++;

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

    /// <summary>节点扫描完成后更新对应 Expander 头部与颜色</summary>
    private void UpdateNodeExpanderCompleted(int targetIndex, Node node, List<PortResult> ports, TimeSpan duration)
    {
        if (targetIndex < 0 || targetIndex >= _targetExpanders.Count) return;
        var expander = _targetExpanders[targetIndex];

        var nodeName = string.IsNullOrEmpty(node.Name) ? (node.Config?.Host ?? "未命名") : node.Name;
        var host = node.Config?.Host ?? "";
        var openCount = ports.Count(p => p.IsOpen);

        expander.Header = $"{nodeName} ({host}) — {openCount}/{ports.Count} 端口开放 ({duration.TotalSeconds:F1}s)";
        expander.IsExpanded = false;

        if (openCount > 0)
            expander.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        else
            expander.Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94));
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
            if (_targetItems == null || _targetItems.Count == 0)
            {
                WriteInfo("PortScanWindow.AutoStartScan: 没有可扫描的节点");
                return;
            }

            var targetNodes = _targetItems.Select(t => t.ToScanNode()).Where(n => n.Type == NodeType.ssh || n.Type == NodeType.rdp).ToList();
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
