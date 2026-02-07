using System.Linq;
using System.Windows;
using System.Windows.Controls;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

public partial class NodeEditWindow : Window
{
    private readonly Node _node;
    private readonly List<Node> _nodes;
    private readonly List<Credential> _credentials;
    private readonly List<Tunnel> _tunnels;
    private readonly StorageService _storage;
    private readonly string _initialName;
    private readonly int _initialTypeIndex;
    private readonly string _initialHost;
    private readonly string _initialPort;
    private readonly string _initialUsername;
    private readonly string _initialPassword;
    private readonly string _initialKeyPath;
    private readonly string _initialDomain;
    private readonly int _initialAuthIndex;
    private readonly string? _initialCredentialId;
    private readonly bool _initialTunnelUseParent;
    private readonly HashSet<string> _initialTunnelIds;
    private readonly int _initialProtocolIndex;
    private bool _closingConfirmed;

    public Node? SavedNode { get; private set; }

    public NodeEditWindow(Node node, List<Node> nodes, List<Credential> credentials, List<Tunnel> tunnels, StorageService storage)
    {
        InitializeComponent();
        _node = node;
        _nodes = nodes;
        _credentials = credentials;
        _tunnels = tunnels;
        _storage = storage;

        NameBox.Text = node.Name;
        // 与 NodeType 枚举顺序一致：group, tencentCloudGroup, ssh, local, rdp
        TypeCombo.Items.Add("分组");
        TypeCombo.Items.Add("腾讯云组");
        TypeCombo.Items.Add("SSH");
        TypeCombo.Items.Add("本地终端");
        TypeCombo.Items.Add("RDP");
        var typeIndex = (int)node.Type;
        TypeCombo.SelectedIndex = typeIndex >= 0 && typeIndex < TypeCombo.Items.Count ? typeIndex : 2; // 默认 SSH

        AuthCombo.Items.Add("同父节点");
        AuthCombo.Items.Add("登录凭证");
        AuthCombo.Items.Add("密码");
        AuthCombo.Items.Add("私钥");
        AuthCombo.Items.Add("SSH Agent");
        CredentialCombo.DisplayMemberPath = "Name";
        CredentialCombo.SelectedValuePath = "Id";

        if (node.Config != null)
        {
            HostBox.Text = node.Config.Host ?? "";
            PortBox.Text = node.Config.Port?.ToString() ?? (node.Type == NodeType.rdp ? "3389" : "22");
            UsernameBox.Text = node.Config.Username ?? (node.Type == NodeType.rdp ? "administrator" : "");
            PasswordBox.Password = node.Config.Password ?? "";
            KeyPathBox.Text = node.Config.KeyPath ?? "";
            DomainBox.Text = node.Config.Domain ?? "";
            var asrc = node.Config.AuthSource ?? AuthSource.inline;
            var authType = node.Config.AuthType ?? AuthType.password;
            AuthCombo.SelectedIndex = asrc switch
            {
                AuthSource.parent => 0,
                AuthSource.credential => 1,
                AuthSource.agent => 4,
                _ => authType == AuthType.key ? 3 : 2
            };
            RefreshCredentialCombo();
            CredentialCombo.SelectedValue = node.Config.CredentialId;
            TunnelUseParentCheckBox.Visibility = string.IsNullOrEmpty(node.ParentId) ? Visibility.Collapsed : Visibility.Visible;
            var useParentTunnel = node.Config.TunnelSource == AuthSource.parent;
            TunnelUseParentCheckBox.IsChecked = useParentTunnel;
            RefreshTunnelList(useParentTunnel ? null : node.Config.TunnelIds);
            UpdateTunnelListEnabled();
        }
        else
        {
            PortBox.Text = "22";
            if (node.Type == NodeType.ssh)
            {
                TunnelUseParentCheckBox.Visibility = string.IsNullOrEmpty(node.ParentId) ? Visibility.Collapsed : Visibility.Visible;
                TunnelUseParentCheckBox.IsChecked = false;
                RefreshTunnelList(null);
                UpdateTunnelListEnabled();
            }
        }

        TypeCombo.SelectionChanged += (_, _) => UpdateConfigVisibility();
        AuthCombo.SelectionChanged += (_, _) => { UpdateAuthVisibility(); UpdateAuthSourceVisibility(); };
        RefreshAuthComboForType();
        UpdateConfigVisibility();
        UpdateAuthVisibility();
        UpdateAuthSourceVisibility();
        if (node.Config != null)
            UpdateTunnelListEnabled();

        _initialName = NameBox.Text ?? "";
        _initialTypeIndex = TypeCombo.SelectedIndex;
        _initialHost = HostBox.Text ?? "";
        _initialPort = PortBox.Text ?? "";
        _initialUsername = UsernameBox.Text ?? "";
        _initialPassword = PasswordBox.Password ?? "";
        _initialKeyPath = KeyPathBox.Text ?? "";
        _initialDomain = DomainBox.Text ?? "";
        _initialAuthIndex = AuthCombo.SelectedIndex;
        _initialCredentialId = CredentialCombo.SelectedValue as string;
        _initialTunnelUseParent = TunnelUseParentCheckBox.IsChecked == true;
        _initialTunnelIds = TunnelListBox.SelectedItems.Cast<Tunnel>().Select(t => t.Id).ToHashSet();
        _initialProtocolIndex = ProtocolCombo.SelectedIndex;
        Closing += NodeEditWindow_Closing;
    }

    private bool IsDirty()
    {
        if ((NameBox.Text ?? "") != _initialName) return true;
        if (TypeCombo.SelectedIndex != _initialTypeIndex) return true;
        if ((HostBox.Text ?? "") != _initialHost) return true;
        if ((PortBox.Text ?? "") != _initialPort) return true;
        if ((UsernameBox.Text ?? "") != _initialUsername) return true;
        if (PasswordBox.Password != _initialPassword) return true;
        if ((KeyPathBox.Text ?? "") != _initialKeyPath) return true;
        if ((DomainBox.Text ?? "") != _initialDomain) return true;
        if (AuthCombo.SelectedIndex != _initialAuthIndex) return true;
        var credNow = CredentialCombo.SelectedValue as string;
        if (!string.Equals(credNow, _initialCredentialId, StringComparison.Ordinal)) return true;
        if ((TunnelUseParentCheckBox.IsChecked == true) != _initialTunnelUseParent) return true;
        var tunnelIdsNow = TunnelListBox.SelectedItems.Cast<Tunnel>().Select(t => t.Id).ToHashSet();
        if (!_initialTunnelIds.SetEquals(tunnelIdsNow)) return true;
        if (ProtocolCombo.SelectedIndex != _initialProtocolIndex) return true;
        return false;
    }

    private void NodeEditWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closingConfirmed) return;
        if (IsDirty() && MessageBox.Show("是否放弃修改？", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            e.Cancel = true;
    }

    private void TunnelUseParentCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateTunnelListEnabled();
    }

    private void UpdateTunnelListEnabled()
    {
        var useParent = TunnelUseParentCheckBox.IsChecked == true;
        TunnelListBox.IsEnabled = !useParent;
        TunnelManageBtn.IsEnabled = !useParent;
    }

    private void RefreshCredentialCombo()
    {
        CredentialCombo.ItemsSource = null;
        CredentialCombo.ItemsSource = _credentials.OrderBy(c => c.AuthType).ThenBy(c => c.Name).ToList();
    }

    private void RefreshTunnelList(List<string>? initialSelectedIds = null)
    {
        var sel = initialSelectedIds ?? TunnelListBox.SelectedItems.Cast<Tunnel>().Select(t => t.Id).ToList();
        var sorted = _tunnels.OrderBy(t => t.AuthType).ThenBy(t => t.Name).ToList();
        TunnelListBox.ItemsSource = null;
        TunnelListBox.ItemsSource = sorted;
        TunnelListBox.DisplayMemberPath = "Name";
        foreach (var t in sorted.Where(t => sel.Contains(t.Id)))
            TunnelListBox.SelectedItems.Add(t);
    }

    private void UpdateAuthSourceVisibility()
    {
        if (TypeCombo.SelectedIndex == (int)NodeType.rdp) return; // RDP 的凭证行由 UpdateAuthVisibility 控制
        CredentialRow.Visibility = AuthCombo.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshAuthComboForType()
    {
        var isRdp = TypeCombo.SelectedIndex == (int)NodeType.rdp;
        var count = AuthCombo.Items.Count;
        var oldIdx = AuthCombo.SelectedIndex;
        if (isRdp)
        {
            if (count != 3)
            {
                var newIdx = (oldIdx >= 0 && oldIdx <= 2) ? oldIdx : 2;
                AuthCombo.Items.Clear();
                AuthCombo.Items.Add("同父节点");
                AuthCombo.Items.Add("登录凭证");
                AuthCombo.Items.Add("密码");
                AuthCombo.SelectedIndex = newIdx >= 0 ? newIdx : 0;
            }
        }
        else if (TypeCombo.SelectedIndex != (int)NodeType.ssh) // 非 SSH（分组/腾讯云组/本地终端）
        {
            if (count != 5)
            {
                var newIdx = (oldIdx >= 0 && oldIdx <= 2) ? oldIdx : 2;
                AuthCombo.Items.Clear();
                AuthCombo.Items.Add("同父节点");
                AuthCombo.Items.Add("登录凭证");
                AuthCombo.Items.Add("密码");
                AuthCombo.Items.Add("私钥");
                AuthCombo.Items.Add("SSH Agent");
                AuthCombo.SelectedIndex = newIdx >= 0 ? newIdx : 0;
            }
        }
    }

    private void UpdateConfigVisibility()
    {
        var idx = TypeCombo.SelectedIndex;
        var isGroup = idx == (int)NodeType.group;
        var isTencentGroup = idx == (int)NodeType.tencentCloudGroup;
        var isSsh = idx == (int)NodeType.ssh;
        var isLocal = idx == (int)NodeType.local;
        var isRdp = idx == (int)NodeType.rdp;
        var hideConfig = isGroup || isTencentGroup;
        ConfigLabel.Visibility = hideConfig ? Visibility.Collapsed : Visibility.Visible;
        ConfigPanel.Visibility = hideConfig ? Visibility.Collapsed : Visibility.Visible;
        CredentialRow.Visibility = Visibility.Collapsed;
        TunnelRow.Visibility = isSsh ? Visibility.Visible : Visibility.Collapsed;
        if (isSsh)
            TunnelUseParentCheckBox.Visibility = string.IsNullOrEmpty(_node.ParentId) ? Visibility.Collapsed : Visibility.Visible;
        DomainRow.Visibility = Visibility.Collapsed; // RDP 不用域
        TestConnectionRow.Visibility = isSsh ? Visibility.Visible : Visibility.Collapsed;
        if (isLocal)
        {
            HostBox.Visibility = Visibility.Collapsed;
            PortBox.Visibility = Visibility.Collapsed;
            UsernameRow.Visibility = Visibility.Collapsed;
            AuthCombo.Visibility = Visibility.Collapsed;
            PasswordRow.Visibility = Visibility.Collapsed;
            KeyRow.Visibility = Visibility.Collapsed;
            ProtocolRow.Visibility = Visibility.Visible;
        }
        else
        {
            HostBox.Visibility = Visibility.Visible;
            PortBox.Visibility = Visibility.Visible;
            UsernameRow.Visibility = Visibility.Visible;
            AuthCombo.Visibility = isLocal ? Visibility.Collapsed : Visibility.Visible;
            PasswordRow.Visibility = Visibility.Collapsed;
            KeyRow.Visibility = Visibility.Collapsed;
            ProtocolRow.Visibility = isLocal ? Visibility.Visible : Visibility.Collapsed;
            RefreshAuthComboForType();
            if (isRdp)
            {
                if (string.IsNullOrEmpty(PortBox.Text) || PortBox.Text == "22") PortBox.Text = "3389";
                if (string.IsNullOrEmpty(UsernameBox.Text)) UsernameBox.Text = "administrator";
            }
            UpdateAuthVisibility();
            UpdateAuthSourceVisibility();
        }
    }

    private void UpdateAuthVisibility()
    {
        var idx = AuthCombo.SelectedIndex;
        // 同父节点(0)、登录凭证(1) 时不显示用户名，也不使用其值
        var hideUsername = idx == 0 || idx == 1;
        UsernameRow.Visibility = hideUsername ? Visibility.Collapsed : Visibility.Visible;

        if (TypeCombo.SelectedIndex == (int)NodeType.rdp) // RDP：同父节点(0)、登录凭证(1)、密码(2)
        {
            CredentialRow.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
            PasswordRow.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
            KeyRow.Visibility = Visibility.Collapsed;
            return;
        }
        var showPasswordSsh = idx == 2;
        var showKey = idx == 3;
        PasswordRow.Visibility = showPasswordSsh ? Visibility.Visible : Visibility.Collapsed;
        KeyRow.Visibility = showKey ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TunnelManageBtn_Click(object sender, RoutedEventArgs e)
    {
        var win = new TunnelManagerWindow(this);
        win.ShowDialog();
        _tunnels.Clear();
        _tunnels.AddRange(_storage.LoadTunnels());
        RefreshTunnelList();
    }

    private void TestConnectionBtn_Click(object sender, RoutedEventArgs e)
    {
        var host = HostBox.Text?.Trim();
        if (string.IsNullOrEmpty(host)) { MessageBox.Show("请填写主机。", "xOpenTerm"); return; }
        if (!ushort.TryParse(PortBox.Text, out var port) || port == 0) port = 22;
        string username; string? password = null; string? keyPath = null; string? keyPassphrase = null;
        var useAgent = AuthCombo.SelectedIndex == 4;
        if (AuthCombo.SelectedIndex == 1 && CredentialCombo.SelectedValue is string cid)
        {
            var cred = _credentials.FirstOrDefault(c => c.Id == cid);
            if (cred == null) { MessageBox.Show("请选择登录凭证。", "xOpenTerm"); return; }
            username = cred.Username;
            if (cred.AuthType == AuthType.password) password = cred.Password;
            else { keyPath = cred.KeyPath; keyPassphrase = cred.KeyPassphrase; }
        }
        else if (AuthCombo.SelectedIndex == 2 || AuthCombo.SelectedIndex == 3)
        {
            username = UsernameBox.Text?.Trim() ?? "";
            if (AuthCombo.SelectedIndex == 2) password = PasswordBox.Password;
            else { keyPath = KeyPathBox.Text?.Trim(); keyPassphrase = null; }
        }
        else if (useAgent)
        {
            username = UsernameBox.Text?.Trim() ?? "";
        }
        else
        {
            MessageBox.Show("同父节点请保存后在实际连接时验证。", "xOpenTerm");
            return;
        }
        if (string.IsNullOrEmpty(username)) { MessageBox.Show("请填写用户名。", "xOpenTerm"); return; }
        var result = SshTester.Test(host, port, username, password, keyPath, keyPassphrase, useAgent);
        MessageBox.Show(result.Success ? "连接成功" : ("连接失败：\n" + (result.FailureReason ?? "未知原因")), "测试连接");
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim() ?? "";
        _node.Type = (NodeType)TypeCombo.SelectedIndex;
        if (_node.Type == NodeType.rdp && string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(HostBox.Text?.Trim()))
            name = HostBox.Text!.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("请输入名称。", "xOpenTerm");
            return;
        }
        _node.Name = name;
        if (_node.Type != NodeType.group && _node.Type != NodeType.tencentCloudGroup)
        {
            _node.Config ??= new ConnectionConfig();
            if (_node.Type == NodeType.local)
            {
                _node.Config.Protocol = ProtocolCombo.SelectedIndex == 1 ? Protocol.cmd : Protocol.powershell;
            }
            else if (_node.Type == NodeType.rdp)
            {
                _node.Config.Host = HostBox.Text?.Trim();
                _node.Config.Port = ushort.TryParse(PortBox.Text, out var pr) && pr > 0 ? pr : (ushort)3389;
                var authIdx = AuthCombo.SelectedIndex;
                _node.Config.AuthSource = authIdx switch { 0 => AuthSource.parent, 1 => AuthSource.credential, _ => AuthSource.inline };
                _node.Config.CredentialId = authIdx == 1 && CredentialCombo.SelectedValue is string rdpCid ? rdpCid : null;
                _node.Config.Password = authIdx != 0 && authIdx != 1 ? PasswordBox.Password : null;
                _node.Config.Username = (authIdx == 0 || authIdx == 1) ? null : (UsernameBox.Text?.Trim() ?? "administrator");
            }
            else
            {
                _node.Config.Host = HostBox.Text?.Trim();
                _node.Config.Port = ushort.TryParse(PortBox.Text, out var p) && p > 0 ? p : (ushort)22;
                var authIdx = AuthCombo.SelectedIndex;
                _node.Config.Username = (authIdx == 0 || authIdx == 1) ? null : UsernameBox.Text?.Trim();
                _node.Config.AuthSource = authIdx switch { 0 => AuthSource.parent, 1 => AuthSource.credential, 4 => AuthSource.agent, _ => AuthSource.inline };
                _node.Config.CredentialId = authIdx == 1 && CredentialCombo.SelectedValue is string cid ? cid : null;
                _node.Config.AuthType = authIdx == 3 ? AuthType.key : AuthType.password;
                _node.Config.Password = authIdx == 2 ? PasswordBox.Password : null;
                _node.Config.KeyPath = authIdx == 3 ? KeyPathBox.Text?.Trim() : null;
                if (TunnelUseParentCheckBox.IsChecked == true)
                {
                    _node.Config.TunnelSource = AuthSource.parent;
                    _node.Config.TunnelIds = null;
                }
                else
                {
                    _node.Config.TunnelSource = null;
                    _node.Config.TunnelIds = TunnelListBox.SelectedItems.Cast<Tunnel>().OrderBy(t => t.AuthType).ThenBy(t => t.Name).Select(t => t.Id).ToList();
                }
            }
        }
        else
        {
            _node.Config = null; // 分组、腾讯云组不保存主机配置
        }
        SavedNode = _node;
        _closingConfirmed = true;
        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        if (IsDirty() && MessageBox.Show("是否放弃修改？", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _closingConfirmed = true;
        DialogResult = false;
        Close();
    }
}
