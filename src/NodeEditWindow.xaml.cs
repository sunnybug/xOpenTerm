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
        TypeCombo.Items.Add("分组");
        TypeCombo.Items.Add("SSH");
        TypeCombo.Items.Add("本地终端");
        TypeCombo.Items.Add("RDP");
        TypeCombo.SelectedIndex = (int)node.Type;

        AuthCombo.Items.Add("密码");
        AuthCombo.Items.Add("私钥");
        AuthCombo.Items.Add("同父节点");
        AuthCombo.Items.Add("SSH Agent");
        AuthCombo.Items.Add("登录凭证");
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
                AuthSource.parent => 2,
                AuthSource.agent => 3,
                AuthSource.credential => 4,
                _ => authType == AuthType.key ? 1 : 0
            };
            RefreshCredentialCombo();
            CredentialCombo.SelectedValue = node.Config.CredentialId;
            RefreshTunnelList(node.Config.TunnelIds);
        }
        else
        {
            PortBox.Text = "22";
        }

        TypeCombo.SelectionChanged += (_, _) => UpdateConfigVisibility();
        AuthCombo.SelectionChanged += (_, _) => { UpdateAuthVisibility(); UpdateAuthSourceVisibility(); };
        UpdateConfigVisibility();
        UpdateAuthVisibility();
        UpdateAuthSourceVisibility();
    }

    private void RefreshCredentialCombo()
    {
        CredentialCombo.ItemsSource = null;
        CredentialCombo.ItemsSource = _credentials;
    }

    private void RefreshTunnelList(List<string>? initialSelectedIds = null)
    {
        var sel = initialSelectedIds ?? TunnelListBox.SelectedItems.Cast<Tunnel>().Select(t => t.Id).ToList();
        TunnelListBox.ItemsSource = null;
        TunnelListBox.ItemsSource = _tunnels;
        TunnelListBox.DisplayMemberPath = "Name";
        foreach (var t in _tunnels.Where(t => sel.Contains(t.Id)))
            TunnelListBox.SelectedItems.Add(t);
    }

    private void UpdateAuthSourceVisibility()
    {
        CredentialRow.Visibility = AuthCombo.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateConfigVisibility()
    {
        var idx = TypeCombo.SelectedIndex;
        var isGroup = idx == 0;
        var isSsh = idx == 1;
        var isLocal = idx == 2;
        var isRdp = idx == 3;
        ConfigLabel.Visibility = isGroup ? Visibility.Collapsed : Visibility.Visible;
        ConfigPanel.Visibility = isGroup ? Visibility.Collapsed : Visibility.Visible;
        CredentialRow.Visibility = Visibility.Collapsed;
        TunnelRow.Visibility = isSsh ? Visibility.Visible : Visibility.Collapsed;
        DomainRow.Visibility = Visibility.Collapsed; // RDP 不用域
        TestConnectionRow.Visibility = isSsh ? Visibility.Visible : Visibility.Collapsed;
        if (isLocal)
        {
            HostBox.Visibility = Visibility.Collapsed;
            PortBox.Visibility = Visibility.Collapsed;
            UsernameBox.Visibility = Visibility.Collapsed;
            AuthCombo.Visibility = Visibility.Collapsed;
            PasswordRow.Visibility = Visibility.Collapsed;
            KeyRow.Visibility = Visibility.Collapsed;
            ProtocolRow.Visibility = Visibility.Visible;
        }
        else
        {
            HostBox.Visibility = Visibility.Visible;
            PortBox.Visibility = Visibility.Visible;
            UsernameBox.Visibility = Visibility.Visible;
            AuthCombo.Visibility = (isRdp || isLocal) ? Visibility.Collapsed : Visibility.Visible;
            PasswordRow.Visibility = Visibility.Collapsed;
            KeyRow.Visibility = Visibility.Collapsed;
            ProtocolRow.Visibility = isLocal ? Visibility.Visible : Visibility.Collapsed;
            if (isRdp)
            {
                if (string.IsNullOrEmpty(PortBox.Text) || PortBox.Text == "22") PortBox.Text = "3389";
                if (string.IsNullOrEmpty(UsernameBox.Text)) UsernameBox.Text = "administrator";
                PasswordRow.Visibility = Visibility.Visible;
                KeyRow.Visibility = Visibility.Collapsed;
            }
            UpdateAuthVisibility();
            UpdateAuthSourceVisibility();
        }
    }

    private void UpdateAuthVisibility()
    {
        if (TypeCombo.SelectedIndex == 3) return; // RDP
        var idx = AuthCombo.SelectedIndex;
        var showPassword = idx == 0;
        var showKey = idx == 1;
        PasswordRow.Visibility = showPassword ? Visibility.Visible : Visibility.Collapsed;
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
        var useAgent = AuthCombo.SelectedIndex == 3;
        if (AuthCombo.SelectedIndex == 4 && CredentialCombo.SelectedValue is string cid)
        {
            var cred = _credentials.FirstOrDefault(c => c.Id == cid);
            if (cred == null) { MessageBox.Show("请选择登录凭证。", "xOpenTerm"); return; }
            username = cred.Username;
            if (cred.AuthType == AuthType.password) password = cred.Password;
            else { keyPath = cred.KeyPath; keyPassphrase = cred.KeyPassphrase; }
        }
        else if (AuthCombo.SelectedIndex == 0 || AuthCombo.SelectedIndex == 1)
        {
            username = UsernameBox.Text?.Trim() ?? "";
            if (AuthCombo.SelectedIndex == 0) password = PasswordBox.Password;
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
        var ok = SshTester.Test(host, port, username, password, keyPath, keyPassphrase, useAgent);
        MessageBox.Show(ok ? "连接成功" : "连接失败", "测试连接");
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
        if (_node.Type != NodeType.group)
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
                _node.Config.Username = UsernameBox.Text?.Trim() ?? "administrator";
                _node.Config.Domain = ""; // RDP 不用域
                _node.Config.Password = PasswordBox.Password;
            }
            else
            {
                _node.Config.Host = HostBox.Text?.Trim();
                _node.Config.Port = ushort.TryParse(PortBox.Text, out var p) && p > 0 ? p : (ushort)22;
                _node.Config.Username = UsernameBox.Text?.Trim();
                var authIdx = AuthCombo.SelectedIndex;
                _node.Config.AuthSource = authIdx switch { 2 => AuthSource.parent, 3 => AuthSource.agent, 4 => AuthSource.credential, _ => AuthSource.inline };
                _node.Config.CredentialId = authIdx == 4 && CredentialCombo.SelectedValue is string cid ? cid : null;
                _node.Config.AuthType = authIdx == 1 ? AuthType.key : AuthType.password;
                _node.Config.Password = authIdx == 0 ? PasswordBox.Password : null;
                _node.Config.KeyPath = authIdx == 1 ? KeyPathBox.Text?.Trim() : null;
                _node.Config.TunnelIds = TunnelListBox.SelectedItems.Cast<Tunnel>().OrderBy(t => _tunnels.IndexOf(t)).Select(t => t.Id).ToList();
            }
        }
        else
        {
            _node.Config = null;
        }
        SavedNode = _node;
        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
