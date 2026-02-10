using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using xOpenTerm.Controls;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

public partial class MainWindow : Window
{
    private const double MinWindowWidth = 400;
    private const double MinWindowHeight = 300;
    private const double MinLeftPanelWidth = 180;
    private const double MaxLeftPanelWidth = 800;
    private readonly StorageService _storage = new();
    private AppSettings _appSettings = new();
    private readonly SessionManager _sessionManager = new();
    private List<Node> _nodes = new();
    private List<Credential> _credentials = new();
    private List<Tunnel> _tunnels = new();
    private string _searchTerm = "";
    private readonly Dictionary<string, TerminalControl> _tabIdToTerminal = new();
    private readonly Dictionary<string, SshPuttyHostControl> _tabIdToPuttyControl = new();
    private readonly Dictionary<string, string> _tabIdToNodeId = new();
    private readonly Dictionary<string, RdpHostControl> _tabIdToRdpControl = new();
    /// <summary>仅断开（未关闭）的 PuTTY tab，用于右键重连。</summary>
    private readonly HashSet<string> _disconnectedPuttyTabIds = new();
    private ContextMenu? _treeContextMenu;
    private Node? _contextMenuNode;
    private Node? _draggedNode;
    private Point _dragStartPoint;
    /// <summary>多选时选中的节点 ID 集合（Ctrl/Shift）</summary>
    private readonly HashSet<string> _selectedNodeIds = new();
    /// <summary>Shift 范围选择时的锚点节点 ID</summary>
    private string? _lastSelectedNodeId;
    /// <summary>远程文件列表当前对应的 SSH 节点 ID</summary>
    private string? _remoteFileNodeId;
    /// <summary>远程文件当前路径</summary>
    private string _remoteFilePath = ".";
    /// <summary>远程文件 tab 缓存：nodeId -> (路径, 列表)。该连接对应的所有 tab 关闭后清除。</summary>
    private readonly Dictionary<string, (string Path, List<RemoteFileItem> List)> _remoteFileCacheByNodeId = new();

    public MainWindow()
    {
        InitializeComponent();
        var settings = _storage.LoadAppSettings();
        ApplyAppSettings(settings);
        ApplyWindowAndLayout(settings);
        LoadData();
        // 恢复上次关闭时的选中节点（仅保留仍存在的节点）
        if (settings.ServerTreeSelectedIds != null && settings.ServerTreeSelectedIds.Count > 0)
        {
            var nodeIds = _nodes.Select(n => n.Id).ToHashSet();
            foreach (var id in settings.ServerTreeSelectedIds)
                if (nodeIds.Contains(id))
                    _selectedNodeIds.Add(id);
        }
        // 恢复上次关闭时的展开状态（若有）；否则默认展开
        var initialExpanded = settings.ServerTreeExpandedIds != null && settings.ServerTreeExpandedIds.Count > 0
            ? new HashSet<string>(settings.ServerTreeExpandedIds)
            : null;
        BuildTree(expandNodes: true, initialExpandedIds: initialExpanded);
        _sessionManager.DataReceived += OnSessionDataReceived;
        _sessionManager.SessionClosed += OnSessionClosed;
        _sessionManager.SessionConnected += OnSessionConnected;
        RemotePathBox.Text = ".";
        Closing += MainWindow_Closing;
        Activated += MainWindow_Activated;
    }

    /// <summary>切回本程序时，若有打开的模态子窗口（设置等），将整条 Owner 链中最顶层的对话框带到前台，避免被主窗口挡住导致无法操作。</summary>
    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        foreach (Window w in Application.Current.Windows)
        {
            if (w == this || !w.IsLoaded || !w.IsVisible || w.IsActive) continue;
            var o = w.Owner;
            while (o != null) { if (o == this) break; o = o.Owner; }
            if (o != this) continue;
            bool isTopmost = true;
            foreach (Window w2 in Application.Current.Windows)
            {
                if (w2 != w && w2.Owner == w && w2.IsLoaded && w2.IsVisible) { isTopmost = false; break; }
            }
            if (isTopmost) { w.Activate(); return; }
        }
    }

    private void ApplyWindowAndLayout(AppSettings settings)
    {
        var w = Math.Max(MinWindowWidth, Math.Min(settings.WindowWidth, SystemParameters.VirtualScreenWidth));
        var h = Math.Max(MinWindowHeight, Math.Min(settings.WindowHeight, SystemParameters.VirtualScreenHeight));
        Width = w;
        Height = h;
        if (settings.WindowLeft is double left && settings.WindowTop is double top)
        {
            var workLeft = SystemParameters.VirtualScreenLeft;
            var workTop = SystemParameters.VirtualScreenTop;
            var workW = SystemParameters.VirtualScreenWidth;
            var workH = SystemParameters.VirtualScreenHeight;
            if (left >= workLeft && top >= workTop && left + w <= workLeft + workW && top + h <= workTop + workH)
            {
                Left = left;
                Top = top;
            }
        }
        LeftColumn.Width = new GridLength(
            Math.Max(MinLeftPanelWidth, Math.Min(settings.LeftPanelWidth, MaxLeftPanelWidth)),
            GridUnitType.Pixel);
        if (settings.WindowState >= 0 && settings.WindowState <= 2)
            WindowState = (WindowState)settings.WindowState;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (TabsControl.Items.Count > 0 &&
            MessageBox.Show("当前有连接未关闭，确定要退出吗？", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        var s = _storage.LoadAppSettings();
        if (WindowState == WindowState.Maximized)
        {
            var r = RestoreBounds;
            s.WindowLeft = r.Left;
            s.WindowTop = r.Top;
            s.WindowWidth = r.Width;
            s.WindowHeight = r.Height;
        }
        else
        {
            s.WindowLeft = Left;
            s.WindowTop = Top;
            s.WindowWidth = Width;
            s.WindowHeight = Height;
        }
        s.WindowState = (int)WindowState;
        if (LeftColumn.ActualWidth >= MinLeftPanelWidth && LeftColumn.ActualWidth <= MaxLeftPanelWidth)
            s.LeftPanelWidth = LeftColumn.ActualWidth;
        // 保存节点树展开状态与选中节点，下次启动时恢复
        var expanded = CollectExpandedGroupNodeIds(ServerTree);
        s.ServerTreeExpandedIds = expanded != null && expanded.Count > 0 ? expanded.ToList() : null;
        s.ServerTreeSelectedIds = _selectedNodeIds.Count > 0 ? _selectedNodeIds.ToList() : null;
        _storage.SaveAppSettings(s);
    }

    private void ApplyAppSettings(AppSettings settings)
    {
        _appSettings = settings;
        try
        {
            FontFamily = new FontFamily(settings.InterfaceFontFamily);
            FontSize = settings.InterfaceFontSize;
        }
        catch
        {
            FontFamily = new FontFamily("Microsoft YaHei UI");
            FontSize = 14;
        }
    }

    private void LoadData()
    {
        _nodes = _storage.LoadNodes();
        _credentials = _storage.LoadCredentials();
        _tunnels = _storage.LoadTunnels();
    }

    protected override void OnClosed(EventArgs e)
    {
        ExceptionLog.WriteInfo("进程退出: 开始关闭");
        var rdpCount = _tabIdToRdpControl.Count;
        foreach (var tabId in _tabIdToRdpControl.Keys.ToList())
        {
            if (_tabIdToRdpControl.TryGetValue(tabId, out var rdp))
            {
                try { rdp.Disconnect(); rdp.Dispose(); } catch { }
            }
        }
        _tabIdToRdpControl.Clear();
        ExceptionLog.WriteInfo($"进程退出: RDP 已关闭 (共 {rdpCount} 个)");
        var puttyCount = _tabIdToPuttyControl.Count;
        foreach (var tabId in _tabIdToPuttyControl.Keys.ToList())
        {
            if (_tabIdToPuttyControl.TryGetValue(tabId, out var putty))
            {
                try { putty.Close(); } catch { }
            }
        }
        _tabIdToPuttyControl.Clear();
        ExceptionLog.WriteInfo($"进程退出: PuTTY 已关闭 (共 {puttyCount} 个)");
        // 关闭所有 SessionManager 会话（含未在 tab 中的），避免子进程/SSH 连接导致进程无法退出
        _sessionManager.CloseAllSessions();
        ExceptionLog.WriteInfo("进程退出: 会话已关闭");
        base.OnClosed(e);
        Application.Current.Shutdown();
        ExceptionLog.WriteInfo("进程退出: Shutdown 已调用");
    }

    private void MenuCredentials_Click(object sender, RoutedEventArgs e)
    {
        var win = new CredentialsWindow(this);
        win.ShowDialog();
        LoadData();
        BuildTree();
    }

    private void MenuFontSettings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(this);
        if (win.ShowDialog() == true)
            ApplyAppSettings(_storage.LoadAppSettings());
    }

    private void MenuTunnels_Click(object sender, RoutedEventArgs e)
    {
        var win = new TunnelManagerWindow(this);
        win.ShowDialog();
        LoadData();
        BuildTree();
    }

    private void MenuRestoreConfig_Click(object sender, RoutedEventArgs e)
    {
        var win = new BackupRestoreWindow(this);
        if (win.ShowDialog() == true)
        {
            LoadData();
            BuildTree();
            ApplyAppSettings(_storage.LoadAppSettings());
        }
    }

    private void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow(this).ShowDialog();
    }

    private void MenuUpdate_Click(object sender, RoutedEventArgs e)
    {
        new UpdateWindow(this).ShowDialog();
    }
}
