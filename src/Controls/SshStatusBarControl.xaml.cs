using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace xOpenTerm.Controls;

/// <summary>SSH 连接标签页底部状态栏：连接状态、CPU/内存占用率、网络流量走势图、TCP/UDP 连接数。</summary>
public partial class SshStatusBarControl : UserControl
{
    private const int MaxNetworkHistory = 30;
    private readonly List<(double RxBps, double TxBps)> _networkHistory = new();
    private static readonly SolidColorBrush ReceiveBrush = new(Color.FromRgb(0x22, 0xC5, 0x5E)); // 绿
    private static readonly SolidColorBrush SendBrush = new(Color.FromRgb(0x3B, 0x82, 0xF6));     // 蓝

    public SshStatusBarControl()
    {
        InitializeComponent();
        Loaded += (_, _) => RedrawNetworkChart();
        SizeChanged += (_, _) => RedrawNetworkChart();
    }

    /// <summary>更新状态栏显示。null 表示暂无数据或未连接。</summary>
    public void UpdateStats(bool connected, double? cpuPercent, double? memPercent,
        double? rxBps, double? txBps, int? tcpCount, int? udpCount)
    {
        StatusText.Text = connected ? "已连接" : "未连接";
        StatusText.Foreground = connected
            ? (SolidColorBrush)FindResource("Accent")
            : (SolidColorBrush)FindResource("TextSecondary");

        CpuText.Text = cpuPercent.HasValue ? $"{cpuPercent.Value:F1}%" : "—%";
        MemText.Text = memPercent.HasValue ? $"{memPercent.Value:F1}%" : "—%";
        TcpText.Text = tcpCount.HasValue ? tcpCount.Value.ToString() : "—";
        UdpText.Text = udpCount.HasValue ? udpCount.Value.ToString() : "—";

        if (rxBps.HasValue && txBps.HasValue)
        {
            _networkHistory.Add((rxBps.Value, txBps.Value));
            if (_networkHistory.Count > MaxNetworkHistory)
                _networkHistory.RemoveAt(0);
        }

        RedrawNetworkChart();
    }

    private void RedrawNetworkChart()
    {
        NetworkChart.Children.Clear();
        var w = NetworkChart.ActualWidth;
        var h = NetworkChart.ActualHeight;
        if (w <= 0 || h <= 0 || _networkHistory.Count < 2) return;

        double maxRate = 1;
        foreach (var (rx, tx) in _networkHistory)
        {
            if (rx > maxRate) maxRate = rx;
            if (tx > maxRate) maxRate = tx;
        }

        var n = _networkHistory.Count;
        var step = n > 1 ? w / (n - 1) : 0d;
        var pointsRx = new PointCollection();
        var pointsTx = new PointCollection();
        for (var i = 0; i < n; i++)
        {
            var (rx, tx) = _networkHistory[i];
            var x = i * step;
            var yRx = h - (maxRate > 0 ? (rx / maxRate) * (h - 2) : 0);
            var yTx = h - (maxRate > 0 ? (tx / maxRate) * (h - 2) : 0);
            pointsRx.Add(new Point(x, yRx));
            pointsTx.Add(new Point(x, yTx));
        }

        var polyRx = new Polyline
        {
            Points = pointsRx,
            Stroke = ReceiveBrush,
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round
        };
        var polyTx = new Polyline
        {
            Points = pointsTx,
            Stroke = SendBrush,
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round
        };
        NetworkChart.Children.Add(polyRx);
        NetworkChart.Children.Add(polyTx);
    }
}
