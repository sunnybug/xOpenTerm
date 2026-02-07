using System.Diagnostics;
using System.IO;
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

    public MainWindow()
    {
        InitializeComponent();
        var settings = _storage.LoadAppSettings();
        ApplyAppSettings(settings);
        ApplyWindowAndLayout(settings);
        LoadData();
        BuildTree();
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
        foreach (var tabId in _tabIdToRdpControl.Keys.ToList())
        {
            if (_tabIdToRdpControl.TryGetValue(tabId, out var rdp))
            {
                rdp.Disconnect();
                rdp.Dispose();
            }
        }
        _tabIdToRdpControl.Clear();
        foreach (var tabId in _tabIdToPuttyControl.Keys.ToList())
        {
            if (_tabIdToPuttyControl.TryGetValue(tabId, out var putty))
                putty.Close();
        }
        _tabIdToPuttyControl.Clear();
        foreach (var tabId in _tabIdToTerminal.Keys.ToList())
            _sessionManager.CloseSession(tabId);
        base.OnClosed(e);
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

    private void MenuOpenConfigDir_Click(object sender, RoutedEventArgs e)
    {
        var configDir = StorageService.GetConfigDir();
        Directory.CreateDirectory(configDir);
        Process.Start(new ProcessStartInfo
        {
            FileName = configDir,
            UseShellExecute = true
        });
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
