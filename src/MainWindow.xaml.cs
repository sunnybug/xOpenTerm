using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Forms.Integration;
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
    private readonly IStorageService _storage;
    private AppSettings _appSettings = new();
    private readonly SessionManager _sessionManager = new();
    private List<Node> _nodes = new();
    private List<Credential> _credentials = new();
    private List<Tunnel> _tunnels = new();
    private string _searchTerm = "";
    private readonly Dictionary<string, TerminalControl> _tabIdToTerminal = new();
    private readonly Dictionary<string, SshPuttyHostControl> _tabIdToPuttyControl = new();
    private readonly Dictionary<string, string> _tabIdToNodeId = new();
    private readonly Dictionary<string, RdpEmbeddedSession> _tabIdToRdpSession = new();
    /// <summary>仅断开（未关闭）的 PuTTY tab，用于右键重连。</summary>
    private readonly HashSet<string> _disconnectedPuttyTabIds = new();
    private ContextMenu? _treeContextMenu;
    private Node? _contextMenuNode;
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
    /// <summary>SSH 标签页底部状态栏控件（仅 SSH 连接 tab）。</summary>
    private readonly Dictionary<string, SshStatusBarControl> _tabIdToSshStatusBar = new();
    /// <summary>RDP 标签页底部状态栏控件（仅 RDP 连接 tab）。</summary>
    private readonly Dictionary<string, SshStatusBarControl> _tabIdToRdpStatusBar = new();
    /// <summary>状态栏轮询取消用（关闭/断开 tab 时取消）。</summary>
    private readonly Dictionary<string, CancellationTokenSource> _tabIdToStatsCts = new();
    /// <summary>PuTTY 类 SSH tab 的连接参数，用于状态栏远程采集（仅无跳板时存储）。</summary>
    private readonly Dictionary<string, (string host, int port, string username, string? password, string? keyPath, string? keyPassphrase, List<JumpHop>? jumpChain, bool useAgent)> _tabIdToSshStatsParams = new();
    /// <summary>RDP tab 的连接参数，用于状态栏远程采集。</summary>
    private readonly Dictionary<string, (string host, int port, string username, string? password)> _tabIdToRdpStatsParams = new();
    /// <summary>Tab 连接状态跟踪：tabId -> 是否已断开</summary>
    private readonly Dictionary<string, bool> _tabIdToDisconnected = new();
    /// <summary>上次关闭 RDP 会话的时间（UTC），关闭后短时内再打开则延迟执行以减轻 COM/RCW 已分离问题。</summary>
    private DateTime _lastRdpCloseUtc = DateTime.MinValue;
    /// <summary>搜索框防抖定时器，输入停止后 250ms 再执行 BuildTree。</summary>
    private DispatcherTimer? _searchDebounceTimer;
    /// <summary>BuildTree 期间使用的“匹配搜索”节点 ID 集合，用于 MatchesSearch 查表。</summary>
    private HashSet<string>? _buildVisibleNodeIds;
    /// <summary>节点 ID → TreeViewItem 映射，Build 时填充、Clear 时清空，用于 RestoreSelection 与多选样式更新。</summary>
    private readonly Dictionary<string, TreeViewItem> _nodeIdToTvi = new();
    /// <summary>上次多选样式更新时的选中节点 ID 集合，用于只更新变化的项。</summary>
    private HashSet<string>? _prevSelectedNodeIds;

    public MainWindow()
    {
        _storage = App.GetStorageService() ?? new StorageService();
        InitializeComponent();
        var settings = _storage.LoadAppSettings();
        ApplyAppSettings(settings);
        ApplyWindowAndLayout(settings);

        if (Program.IsTestRdpMode)
        {
            Loaded += (_, _) =>
            {
                LoadData();
                BuildTree(expandNodes: true, initialExpandedIds: null);
                UpdateServerSearchPlaceholder();
                RunTestRdp();
            };
            return;
        }

        // 启动时检查是否已询问过主密码：未询问则弹窗设置，已设置则弹窗输入；取消输入则退出
        if (!EnsureMasterPasswordThenContinue(settings))
        {
            Application.Current.Shutdown();
            return;
        }
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
        UpdateServerSearchPlaceholder();
        _sessionManager.DataReceived += OnSessionDataReceived;
        _sessionManager.SessionClosed += OnSessionClosed;
        _sessionManager.SessionConnected += OnSessionConnected;
        RemotePathBox.Text = ".";
        Closing += MainWindow_Closing;
        Activated += MainWindow_Activated;

        if (Program.IsTestRdpMode)
        {
            RunTestRdp();
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Q || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        LeftTabControl.SelectedIndex = 0;
        ServerSearchBox.Focus();
        ServerSearchBox.SelectionStart = ServerSearchBox.Text.Length;
        ServerSearchBox.SelectionLength = 0;
        e.Handled = true;
    }

    private async void RunTestRdp()
    {
        var rdpNode = _nodes.FirstOrDefault(n => n.Type == NodeType.rdp);
        if (rdpNode == null)
        {
            ExceptionLog.WriteInfo("TestRDP: No RDP node found");
            Application.Current.Shutdown(1);
            return;
        }

        ExceptionLog.WriteInfo($"TestRDP: Found RDP node: {rdpNode.Name} ({rdpNode.Config?.Host})");

        var connected = false;
        var failed = false;
        var taskCompletionSource = new TaskCompletionSource<bool>();

        void OnConnected(object? sender, EventArgs e)
        {
            connected = true;
            ExceptionLog.WriteInfo("TestRDP: Connected successfully");
            if (_tabIdToRdpSession.TryGetValue("test-rdp", out var session))
            {
                session.Connected -= OnConnected;
                session.Disconnected -= OnDisconnected;
                session.ErrorOccurred -= OnError;
            }
            taskCompletionSource.TrySetResult(true);
        }

        void OnDisconnected(object? sender, EventArgs e)
        {
            ExceptionLog.WriteInfo("TestRDP: Disconnected");
            if (!connected)
            {
                failed = true;
                taskCompletionSource.TrySetResult(false);
            }
        }

        void OnError(object? sender, string msg)
        {
            ExceptionLog.WriteInfo($"TestRDP: Error - {msg}");
            failed = true;
            taskCompletionSource.TrySetResult(false);
        }

        var tabId = "test-rdp";
        var sameCount = _tabIdToNodeId.Values.Count(id => id == rdpNode.Id);
        var tabTitle = sameCount == 0 ? rdpNode.Name : $"{rdpNode.Name} ({sameCount + 1})";

        try
        {
            var (host, port, username, domain, password, rdpOptions) = ConfigResolver.ResolveRdp(rdpNode, _nodes, _credentials);
            var panel = new System.Windows.Forms.Panel { Dock = System.Windows.Forms.DockStyle.Fill, MinimumSize = new System.Drawing.Size(400, 300) };
            var session = new RdpEmbeddedSession(host, port, username, domain, password, rdpOptions, panel, SynchronizationContext.Current!);
            session.Connected += OnConnected;
            session.Disconnected += OnDisconnected;
            session.ErrorOccurred += OnError;

            _tabIdToRdpSession[tabId] = session;

            var hostWpf = new WindowsFormsHost { Child = panel };
            var statusBar = new SshStatusBarControl();
            statusBar.UpdateStats(false, null, null, null, null, null, null);
            var dock = new DockPanel();
            DockPanel.SetDock(statusBar, Dock.Bottom);
            dock.Children.Add(statusBar);
            dock.Children.Add(hostWpf);
            var tabItem = new TabItem
            {
                Header = tabTitle,
                Content = dock,
                Tag = tabId,
                Style = (Style)FindResource("AppTabItemStyle")
            };
            TabsControl.Items.Add(tabItem);
            TabsControl.SelectedItem = tabItem;
            _tabIdToNodeId[tabId] = rdpNode.Id;

            session.Start();

            var timeout = TimeSpan.FromSeconds(30);
            var delayTask = Task.Delay(timeout);
            var connectTask = taskCompletionSource.Task;

            var completedTask = await Task.WhenAny(connectTask, delayTask);

            if (completedTask == delayTask && !connected && !failed)
            {
                ExceptionLog.WriteInfo("TestRDP: Timeout after 30 seconds");
                Application.Current.Shutdown(1);
                return;
            }

            if (connected)
            {
                ExceptionLog.WriteInfo("TestRDP: Connection test passed");
                await Task.Delay(1000);
                Application.Current.Shutdown(0);
            }
            else if (failed)
            {
                ExceptionLog.WriteInfo("TestRDP: Connection test failed");
                Application.Current.Shutdown(1);
            }
        }
        catch (Exception ex)
        {
            ExceptionLog.Write(ex, "TestRDP: Exception");
            Application.Current.Shutdown(1);
        }
    }

    /// <summary>确保主密码已设置或已输入：未询问则弹窗设置，已设置则弹窗输入。返回 true 表示可继续加载数据，false 表示用户取消输入主密码应退出。</summary>
    private bool EnsureMasterPasswordThenContinue(AppSettings settings)
    {
        if (settings.MasterPasswordSkipped)
            return true; // 用户曾选择「不再提醒」，直接继续运行（使用原有固定密钥加解密）

        if (!settings.MasterPasswordAsked)
        {
            // 构造函数中主窗口尚未 Show，不能设置 Owner，否则 WPF 抛错；无 Owner 时改为屏幕居中并置顶，否则窗口可能看不见
            var dlg = new MasterPasswordWindow(isSetMode: true, salt: null, verifier: null);
            if (IsLoaded) dlg.Owner = this; else { dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen; dlg.Topmost = true; }
            if (dlg.ShowDialog() != true)
            {
                if (dlg.DontRemindAgain)
                {
                    settings.MasterPasswordSkipped = true;
                    _storage.SaveAppSettings(settings);
                }
                return true; // 用户取消设置或不再提醒，仍继续运行（使用原有固定密钥加解密）
            }
            var password = dlg.ResultPassword;
            if (string.IsNullOrEmpty(password))
                return true;
            var salt = MasterPasswordService.GenerateSalt();
            var key = MasterPasswordService.DeriveKey(password, salt);
            var verifier = MasterPasswordService.GetVerifierFromKey(key);
            settings.MasterPasswordAsked = true;
            settings.MasterPasswordSalt = Convert.ToBase64String(salt);
            settings.MasterPasswordVerifier = Convert.ToBase64String(verifier);
            _storage.SaveAppSettings(settings);
            SecretService.SetSessionMasterKey(key);
            MasterPasswordService.SaveKeyToFile(key); // 保存到本地文件，以后启动无需输入主密码
            return true;
        }

        byte[]? saltBytes = null;
        byte[]? verifierBytes = null;
        try
        {
            if (!string.IsNullOrEmpty(settings.MasterPasswordSalt))
                saltBytes = Convert.FromBase64String(settings.MasterPasswordSalt);
            if (!string.IsNullOrEmpty(settings.MasterPasswordVerifier))
                verifierBytes = Convert.FromBase64String(settings.MasterPasswordVerifier);
        }
        catch
        {
            // 盐或验证码损坏，视为未设置，改为弹出设置主密码
            settings.MasterPasswordAsked = false;
            settings.MasterPasswordSalt = null;
            settings.MasterPasswordVerifier = null;
            _storage.SaveAppSettings(settings);
            return EnsureMasterPasswordThenContinue(settings);
        }

        if (saltBytes == null || verifierBytes == null)
        {
            settings.MasterPasswordAsked = false;
            _storage.SaveAppSettings(settings);
            return EnsureMasterPasswordThenContinue(settings);
        }

        // 优先从本地文件读取已保存的密钥（DPAPI 加密），校验通过则无需弹窗
        var savedKey = MasterPasswordService.TryLoadKeyFromFile(verifierBytes);
        if (savedKey != null)
        {
            SecretService.SetSessionMasterKey(savedKey);
            return true;
        }

        var enterDlg = new MasterPasswordWindow(isSetMode: false, saltBytes, verifierBytes);
        if (IsLoaded) enterDlg.Owner = this; else { enterDlg.WindowStartupLocation = WindowStartupLocation.CenterScreen; enterDlg.Topmost = true; }
        if (enterDlg.ShowDialog() != true)
        {
            // 取消或不再提醒均视为放弃输入，无法解密配置则退出
            return false;
        }
        // 对话框内已设置会话密钥并在输入成功后保存到文件，此处直接返回
        return true;
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
        _searchDebounceTimer?.Stop();
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

        // 停止所有状态栏轮询线程，防止后台线程访问已清理的资源
        var statsCtsCount = _tabIdToStatsCts.Count;
        foreach (var tabId in _tabIdToStatsCts.Keys.ToList())
        {
            if (_tabIdToStatsCts.TryGetValue(tabId, out var cts))
            {
                try { cts.Cancel(); } catch { }
            }
        }
        _tabIdToStatsCts.Clear();
        ExceptionLog.WriteInfo($"进程退出: 状态栏轮询已停止 (共 {statsCtsCount} 个)");

        var rdpCount = _tabIdToRdpSession.Count;
        foreach (var tabId in _tabIdToRdpSession.Keys.ToList())
        {
            if (_tabIdToRdpSession.TryGetValue(tabId, out var session))
            {
                try { session.Close(); } catch { }
            }
        }
        _tabIdToRdpSession.Clear();
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

    private void MenuMasterPasswordSet_Click(object sender, RoutedEventArgs e)
    {
        var settings = _storage.LoadAppSettings();
        // 已设置主密码且未跳过时，提示先清除再设置
        if (settings.MasterPasswordAsked && !settings.MasterPasswordSkipped
            && !string.IsNullOrEmpty(settings.MasterPasswordSalt) && !string.IsNullOrEmpty(settings.MasterPasswordVerifier))
        {
            MessageBox.Show("您已设置主密码。若要修改请先使用「清除主密码」，再重新设置。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new MasterPasswordWindow(isSetMode: true, salt: null, verifier: null);
        dlg.Owner = this;
        if (dlg.ShowDialog() != true || string.IsNullOrEmpty(dlg.ResultPassword))
            return;
        var password = dlg.ResultPassword;
        var salt = MasterPasswordService.GenerateSalt();
        var key = MasterPasswordService.DeriveKey(password, salt);
        var verifier = MasterPasswordService.GetVerifierFromKey(key);
        settings.MasterPasswordAsked = true;
        settings.MasterPasswordSkipped = false;
        settings.MasterPasswordSalt = Convert.ToBase64String(salt);
        settings.MasterPasswordVerifier = Convert.ToBase64String(verifier);
        _storage.SaveAppSettings(settings);
        SecretService.SetSessionMasterKey(key);
        MasterPasswordService.SaveKeyToFile(key);
        // 用新主密码重新加密保存节点与凭证（当前内存中已是解密状态）
        _storage.SaveNodes(_nodes);
        _storage.SaveCredentials(_credentials);
        MessageBox.Show("主密码已设置。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void MenuMasterPasswordClear_Click(object sender, RoutedEventArgs e)
    {
        var settings = _storage.LoadAppSettings();
        var hasMasterPassword = settings.MasterPasswordAsked && !settings.MasterPasswordSkipped
            && !string.IsNullOrEmpty(settings.MasterPasswordSalt) && !string.IsNullOrEmpty(settings.MasterPasswordVerifier);
        if (!hasMasterPassword)
        {
            MessageBox.Show("当前未使用主密码。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        // 清除前要求输入当前主密码以确认身份
        byte[]? saltBytes = null;
        byte[]? verifierBytes = null;
        if (!string.IsNullOrEmpty(settings.MasterPasswordSalt))
            saltBytes = Convert.FromBase64String(settings.MasterPasswordSalt);
        if (!string.IsNullOrEmpty(settings.MasterPasswordVerifier))
            verifierBytes = Convert.FromBase64String(settings.MasterPasswordVerifier);
        if (saltBytes == null || verifierBytes == null || verifierBytes.Length != 32)
        {
            MessageBox.Show("主密码配置异常，无法执行清除。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var verifyDlg = new MasterPasswordWindow(isSetMode: false, saltBytes, verifierBytes, verifyOnly: true);
        if (verifyDlg.ShowDialog() != true)
            return;
        if (MessageBox.Show("清除后，下次启动将不再使用主密码；本地保存的密钥文件也会删除。配置中的密码将改用固定密钥重新保存。是否继续？",
                "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        SecretService.SetSessionMasterKey(null);
        settings.MasterPasswordAsked = false;
        settings.MasterPasswordSalt = null;
        settings.MasterPasswordVerifier = null;
        settings.MasterPasswordSkipped = false;
        _storage.SaveAppSettings(settings);
        MasterPasswordService.DeleteSavedKeyFile();
        // 用固定密钥重新保存节点与凭证（当前内存中已是解密状态，保存时会按无主密码加密）
        _storage.SaveNodes(_nodes);
        _storage.SaveCredentials(_credentials);
        MessageBox.Show("主密码已清除。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
