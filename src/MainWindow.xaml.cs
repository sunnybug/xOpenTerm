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
    /// <summary>远程文件列表当前对应的 SSH 节点 ID</summary>
    private string? _remoteFileNodeId;
    /// <summary>远程文件当前路径</summary>
    private string _remoteFilePath = ".";

    public MainWindow()
    {
        InitializeComponent();
        ApplyAppSettings(_storage.LoadAppSettings());
        LoadData();
        BuildTree();
        _sessionManager.DataReceived += OnSessionDataReceived;
        _sessionManager.SessionClosed += OnSessionClosed;
        _sessionManager.SessionConnected += OnSessionConnected;
        RemotePathBox.Text = ".";
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
        var configDir = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),
            "config");
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
