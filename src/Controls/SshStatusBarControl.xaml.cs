using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using xOpenTerm.Services;

namespace xOpenTerm.Controls;

/// <summary>SSH 连接标签页底部状态栏：连接状态、CPU/内存占用率、网络流量走势图、TCP/UDP 连接数。</summary>
public partial class SshStatusBarControl : UserControl
{
    private const int MaxHistory = 30;
    private readonly List<double> _cpuHistory = new();
    private readonly List<double> _memHistory = new();
    private readonly List<double> _rxHistory = new();
    private readonly List<double> _txHistory = new();
    private static readonly SolidColorBrush CpuBrush = new(Color.FromRgb(0xF5, 0x9E, 0x0B));     // 橙
    private static readonly SolidColorBrush MemBrush = new(Color.FromRgb(0x8B, 0x5C, 0x3C));     // 棕
    private static readonly SolidColorBrush RxBrush = new(Color.FromRgb(0x22, 0xC5, 0x5E));     // 绿
    private static readonly SolidColorBrush TxBrush = new(Color.FromRgb(0x3B, 0x82, 0xF6));     // 蓝

    /// <summary>在流量区右键选择「查看进程流量」时触发，参数为要在 SSH 终端中执行的命令（含换行）。</summary>
    public event EventHandler<string>? RequestSendCommand;

    public SshStatusBarControl()
    {
        InitializeComponent();
        Loaded += (_, _) => RedrawCharts();
        SizeChanged += (_, _) => RedrawCharts();
    }

    private void TrafficContextMenu_ViewProcessTraffic_Click(object sender, RoutedEventArgs e)
    {
        // 在终端中执行 nethogs -t：按流量排序显示进程网络占用（一次输出）；若无 nethogs 可改用手动安装
        RequestSendCommand?.Invoke(this, "nethogs -t");
    }

    /// <summary>更新状态栏显示。null 表示暂无数据或未连接。</summary>
    public void UpdateStats(bool connected, double? cpuPercent, double? memPercent,
        double? rxBps, double? txBps, int? tcpCount, int? udpCount)
    {
        ExceptionLog.Debug($"[SshStatusBar] 更新状态 - 连接: {connected}, CPU: {cpuPercent}, 内存: {memPercent}, 下行: {rxBps}, 上行: {txBps}, TCP: {tcpCount}, UDP: {udpCount}");

        StatusText.Text = connected ? "已连接" : "未连接";
        try
        {
            StatusText.Foreground = connected
                ? (SolidColorBrush)FindResource("Accent")
                : (SolidColorBrush)FindResource("TextSecondary");
        }
        catch
        {
            // 资源已清理时使用默认颜色
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xa3, 0xb8));
        }

        CpuText.Text = cpuPercent.HasValue ? $"{cpuPercent.Value:F1}%" : "—%";
        MemText.Text = memPercent.HasValue ? $"{memPercent.Value:F1}%" : "—%";
        RxText.Text = rxBps.HasValue ? FormatBytesPerSecond(rxBps.Value) : "—";
        TxText.Text = txBps.HasValue ? FormatBytesPerSecond(txBps.Value) : "—";
        TcpText.Text = tcpCount.HasValue ? tcpCount.Value.ToString() : "—";
        UdpText.Text = udpCount.HasValue ? udpCount.Value.ToString() : "—";

        // 设置文本颜色：有数据时使用正常颜色，无数据时使用灰色
        SolidColorBrush textPrimary;
        SolidColorBrush textSecondary;
        try
        {
            textPrimary = (SolidColorBrush)FindResource("TextPrimary");
            textSecondary = (SolidColorBrush)FindResource("TextSecondary");
        }
        catch
        {
            // 资源已清理时使用默认颜色
            textPrimary = new SolidColorBrush(Color.FromRgb(0xf8, 0xfa, 0xfc));
            textSecondary = new SolidColorBrush(Color.FromRgb(0x94, 0xa3, 0xb8));
        }
        CpuText.Foreground = cpuPercent.HasValue ? textPrimary : textSecondary;
        MemText.Foreground = memPercent.HasValue ? textPrimary : textSecondary;
        RxText.Foreground = rxBps.HasValue ? textPrimary : textSecondary;
        TxText.Foreground = txBps.HasValue ? textPrimary : textSecondary;
        TcpText.Foreground = tcpCount.HasValue ? textPrimary : textSecondary;
        UdpText.Foreground = udpCount.HasValue ? textPrimary : textSecondary;

        if (cpuPercent.HasValue)
        {
            _cpuHistory.Add(cpuPercent.Value);
            if (_cpuHistory.Count > MaxHistory)
                _cpuHistory.RemoveAt(0);
        }

        if (memPercent.HasValue)
        {
            _memHistory.Add(memPercent.Value);
            if (_memHistory.Count > MaxHistory)
                _memHistory.RemoveAt(0);
        }

        if (rxBps.HasValue)
        {
            _rxHistory.Add(rxBps.Value);
            if (_rxHistory.Count > MaxHistory)
                _rxHistory.RemoveAt(0);
        }

        if (txBps.HasValue)
        {
            _txHistory.Add(txBps.Value);
            if (_txHistory.Count > MaxHistory)
                _txHistory.RemoveAt(0);
        }

        RedrawCharts();
    }

    /// <summary>显示RDP无SSH服务的提示，并将所有数据控件变灰色表示无效。</summary>
    public void ShowRdpNoSshMessage()
    {
        StatusText.Text = "已连接 (无SSH)";
        try
        {
            StatusText.Foreground = (SolidColorBrush)FindResource("TextSecondary");
        }
        catch
        {
            // 资源已清理时使用默认颜色
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xa3, 0xb8));
        }

        // 设置所有数据文本为灰色和默认值
        SolidColorBrush textSecondary;
        try
        {
            textSecondary = (SolidColorBrush)FindResource("TextSecondary");
        }
        catch
        {
            // 资源已清理时使用默认颜色
            textSecondary = new SolidColorBrush(Color.FromRgb(0x94, 0xa3, 0xb8));
        }
        CpuText.Text = "—%";
        CpuText.Foreground = textSecondary;
        MemText.Text = "—%";
        MemText.Foreground = textSecondary;
        RxText.Text = "—";
        RxText.Foreground = textSecondary;
        TxText.Text = "—";
        TxText.Foreground = textSecondary;
        TcpText.Text = "—";
        TcpText.Foreground = textSecondary;
        UdpText.Text = "—";
        UdpText.Foreground = textSecondary;

        // 清空历史数据并重新绘制图表
        _cpuHistory.Clear();
        _memHistory.Clear();
        _rxHistory.Clear();
        _txHistory.Clear();
        RedrawCharts();
    }

    private void RedrawCharts()
    {
        RedrawCpuChart();
        RedrawMemChart();
        RedrawRxChart();
        RedrawTxChart();
    }

    private void RedrawCpuChart()
    {
        CpuChart.Children.Clear();
        var w = CpuChart.ActualWidth;
        var h = CpuChart.ActualHeight;
        if (w <= 0 || h <= 0 || _cpuHistory.Count < 2) return;

        double maxValue = 100;
        var n = _cpuHistory.Count;
        var step = n > 1 ? w / (n - 1) : 0d;
        var points = new PointCollection();
        for (var i = 0; i < n; i++)
        {
            var value = _cpuHistory[i];
            var x = i * step;
            var y = h - (value / maxValue) * (h - 2);
            points.Add(new Point(x, y));
        }

        DrawAreaChart(CpuChart, points, CpuBrush, w, h);
    }

    private void RedrawMemChart()
    {
        MemChart.Children.Clear();
        var w = MemChart.ActualWidth;
        var h = MemChart.ActualHeight;
        if (w <= 0 || h <= 0 || _memHistory.Count < 2) return;

        double maxValue = 100;
        var n = _memHistory.Count;
        var step = n > 1 ? w / (n - 1) : 0d;
        var points = new PointCollection();
        for (var i = 0; i < n; i++)
        {
            var value = _memHistory[i];
            var x = i * step;
            var y = h - (value / maxValue) * (h - 2);
            points.Add(new Point(x, y));
        }

        DrawAreaChart(MemChart, points, MemBrush, w, h);
    }

    private void RedrawRxChart()
    {
        RxChart.Children.Clear();
        var w = RxChart.ActualWidth;
        var h = RxChart.ActualHeight;
        if (w <= 0 || h <= 0 || _rxHistory.Count < 2) return;

        double maxValue = 1;
        foreach (var value in _rxHistory)
        {
            if (value > maxValue) maxValue = value;
        }

        var n = _rxHistory.Count;
        var step = n > 1 ? w / (n - 1) : 0d;
        var points = new PointCollection();
        for (var i = 0; i < n; i++)
        {
            var value = _rxHistory[i];
            var x = i * step;
            var y = h - (maxValue > 0 ? (value / maxValue) * (h - 2) : 0);
            points.Add(new Point(x, y));
        }

        DrawAreaChart(RxChart, points, RxBrush, w, h);
    }

    private void RedrawTxChart()
    {
        TxChart.Children.Clear();
        var w = TxChart.ActualWidth;
        var h = TxChart.ActualHeight;
        if (w <= 0 || h <= 0 || _txHistory.Count < 2) return;

        double maxValue = 1;
        foreach (var value in _txHistory)
        {
            if (value > maxValue) maxValue = value;
        }

        var n = _txHistory.Count;
        var step = n > 1 ? w / (n - 1) : 0d;
        var points = new PointCollection();
        for (var i = 0; i < n; i++)
        {
            var value = _txHistory[i];
            var x = i * step;
            var y = h - (maxValue > 0 ? (value / maxValue) * (h - 2) : 0);
            points.Add(new Point(x, y));
        }

        DrawAreaChart(TxChart, points, TxBrush, w, h);
    }

    private void DrawAreaChart(Canvas canvas, PointCollection points, SolidColorBrush brush, double width, double height)
    {
        if (points.Count < 2) return;

        var areaPoints = new PointCollection(points);
        areaPoints.Add(new Point(width, height));
        areaPoints.Add(new Point(0, height));

        var area = new Polygon
        {
            Points = areaPoints,
            Fill = new SolidColorBrush(Color.FromArgb(0x33, brush.Color.R, brush.Color.G, brush.Color.B))
        };

        var line = new Polyline
        {
            Points = points,
            Stroke = brush,
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round
        };

        canvas.Children.Add(area);
        canvas.Children.Add(line);
    }

    private string FormatBytesPerSecond(double bps)
    {
        if (bps < 1024) return $"{bps:F0} B/s";
        if (bps < 1024 * 1024) return $"{(bps / 1024):F1} KB/s";
        if (bps < 1024 * 1024 * 1024) return $"{(bps / (1024 * 1024)):F1} MB/s";
        return $"{(bps / (1024 * 1024 * 1024)):F1} GB/s";
    }
}
