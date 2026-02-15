using System.Linq;
using System.Windows;
using System.Windows.Controls;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>SSH 节点编辑窗口：主机、端口、认证方式、跳板机、测试连接。</summary>
public partial class SshNodeEditWindow : NodeEditWindowBase
{
    private readonly string _initialName;
    private readonly string _initialHost;
    private readonly string _initialPort;
    private readonly string _initialUsername;
    private readonly string _initialPassword;
    private readonly string _initialKeyPath;
    private readonly int _initialAuthIndex;
    private readonly string? _initialCredentialId;
    private readonly bool _initialTunnelUseParent;
    private readonly HashSet<string> _initialTunnelIds;

    public SshNodeEditWindow(Node node, INodeEditContext context, bool isExistingNode = true)
        : base(node, context, isExistingNode)
    {
        InitializeComponent();
        NameBox.Text = node.Name ?? "";
        HostBox.Text = node.Config?.Host ?? "";
        PortBox.Text = node.Config?.Port?.ToString() ?? "22";
        UsernameBox.Text = node.Config?.Username ?? "";
        PasswordBox.Password = node.Config?.Password ?? "";
        KeyPathBox.Text = node.Config?.KeyPath ?? "";

        AuthCombo.Items.Add("同父节点");
        AuthCombo.Items.Add("登录凭证");
        AuthCombo.Items.Add("密码");
        AuthCombo.Items.Add("私钥");
        AuthCombo.Items.Add("SSH Agent");
        CredentialCombo.DisplayMemberPath = "Name";
        CredentialCombo.SelectedValuePath = "Id";

        var cfg = node.Config;
        if (cfg != null)
        {
            var asrc = cfg.AuthSource ?? AuthSource.inline;
            var authType = cfg.AuthType ?? AuthType.password;
            AuthCombo.SelectedIndex = asrc switch
            {
                AuthSource.parent => 0,
                AuthSource.credential => 1,
                AuthSource.agent => 4,
                _ => authType == AuthType.key ? 3 : 2
            };
            RefreshCredentialCombo(CredentialCombo);
            CredentialCombo.SelectedValue = cfg.CredentialId;
            TunnelUseParentCheckBox.Visibility = string.IsNullOrEmpty(node.ParentId) ? Visibility.Collapsed : Visibility.Visible;
            var useParentTunnel = cfg.TunnelSource == AuthSource.parent;
            TunnelUseParentCheckBox.IsChecked = useParentTunnel;
            RefreshTunnelList(TunnelListBox, useParentTunnel ? null : cfg.TunnelIds);
            UpdateTunnelListEnabled();
        }
        else
        {
            TunnelUseParentCheckBox.Visibility = string.IsNullOrEmpty(node.ParentId) ? Visibility.Collapsed : Visibility.Visible;
            TunnelUseParentCheckBox.IsChecked = false;
            RefreshTunnelList(TunnelListBox, null);
            UpdateTunnelListEnabled();
        }

        AuthCombo_SelectionChanged(null!, null!);

        _initialName = NameBox.Text ?? "";
        _initialHost = HostBox.Text ?? "";
        _initialPort = PortBox.Text ?? "";
        _initialUsername = UsernameBox.Text ?? "";
        _initialPassword = PasswordBox.Password ?? "";
        _initialKeyPath = KeyPathBox.Text ?? "";
        _initialAuthIndex = AuthCombo.SelectedIndex;
        _initialCredentialId = CredentialCombo.SelectedValue as string;
        _initialTunnelUseParent = TunnelUseParentCheckBox.IsChecked == true;
        _initialTunnelIds = TunnelListBox.SelectedItems.Cast<Tunnel>().Select(t => t.Id).ToHashSet();
        RegisterClosing();
    }

    protected override bool IsDirty()
    {
        if ((NameBox.Text ?? "") != _initialName) return true;
        if ((HostBox.Text ?? "") != _initialHost) return true;
        if ((PortBox.Text ?? "") != _initialPort) return true;
        if ((UsernameBox.Text ?? "") != _initialUsername) return true;
        if (PasswordBox.Password != _initialPassword) return true;
        if ((KeyPathBox.Text ?? "") != _initialKeyPath) return true;
        if (AuthCombo.SelectedIndex != _initialAuthIndex) return true;
        var credNow = CredentialCombo.SelectedValue as string;
        if (!string.Equals(credNow, _initialCredentialId, StringComparison.Ordinal)) return true;
        if ((TunnelUseParentCheckBox.IsChecked == true) != _initialTunnelUseParent) return true;
        var tunnelIdsNow = TunnelListBox.SelectedItems.Cast<Tunnel>().Select(t => t.Id).ToHashSet();
        if (!_initialTunnelIds.SetEquals(tunnelIdsNow)) return true;
        return false;
    }

    protected override bool SaveToNode()
    {
        var name = NameBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show(this, "请输入名称。", "xOpenTerm");
            return false;
        }
        _node.Name = name;
        _node.Config ??= new ConnectionConfig();
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
        return true;
    }

    private void AuthCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var idx = AuthCombo.SelectedIndex;
        var hideUsername = idx == 0 || idx == 1;
        UsernameRow.Visibility = hideUsername ? Visibility.Collapsed : Visibility.Visible;
        CredentialRow.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        PasswordRow.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
        KeyRow.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TunnelUseParentCheckBox_Changed(object sender, RoutedEventArgs e) => UpdateTunnelListEnabled();

    private void UpdateTunnelListEnabled()
    {
        var useParent = TunnelUseParentCheckBox.IsChecked == true;
        TunnelListBox.IsEnabled = !useParent;
        TunnelManageBtn.IsEnabled = !useParent;
    }

    private void TunnelManageBtn_Click(object sender, RoutedEventArgs e)
    {
        var win = new TunnelManagerWindow(this);
        win.ShowDialog();
        ReloadTunnels();
        RefreshTunnelList(TunnelListBox);
    }

    private void TestConnectionBtn_Click(object sender, RoutedEventArgs e)
    {
        var host = HostBox.Text?.Trim();
        if (string.IsNullOrEmpty(host)) { MessageBox.Show(this, "请填写主机。", "xOpenTerm"); return; }
        if (!ushort.TryParse(PortBox.Text, out var port) || port == 0) port = 22;
        string username; string? password = null; string? keyPath = null; string? keyPassphrase = null;
        var useAgent = AuthCombo.SelectedIndex == 4;
        if (AuthCombo.SelectedIndex == 1 && CredentialCombo.SelectedValue is string cid)
        {
            var cred = _credentials.FirstOrDefault(c => c.Id == cid);
            if (cred == null) { MessageBox.Show(this, "请选择登录凭证。", "xOpenTerm"); return; }
            username = cred.Username ?? "";
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
        else if (AuthCombo.SelectedIndex == 0)
        {
            if (string.IsNullOrEmpty(_node.ParentId))
            {
                MessageBox.Show(this, "同父节点请保存后在实际连接时验证。", "xOpenTerm");
                return;
            }
            var tempNode = new Node
            {
                ParentId = _node.ParentId,
                Type = NodeType.ssh,
                Config = new ConnectionConfig
                {
                    Host = host,
                    Port = port,
                    AuthSource = AuthSource.parent,
                    TunnelSource = TunnelUseParentCheckBox.IsChecked == true ? AuthSource.parent : null,
                    TunnelIds = TunnelUseParentCheckBox.IsChecked != true
                        ? TunnelListBox.SelectedItems.Cast<Tunnel>().OrderBy(t => t.AuthType).ThenBy(t => t.Name).Select(t => t.Id).ToList()
                        : null
                }
            };
            try
            {
                var resolved = ConfigResolver.ResolveSsh(tempNode, _nodes, _credentials, _tunnels);
                username = resolved.username ?? "";
                password = resolved.password;
                keyPath = resolved.keyPath;
                keyPassphrase = resolved.keyPassphrase;
                useAgent = resolved.useAgent;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "解析父节点凭证失败：\n" + ex.Message, "xOpenTerm");
                return;
            }
            if (string.IsNullOrEmpty(username)) { MessageBox.Show(this, "父节点未配置 SSH 认证，请先在分组设置中配置。", "xOpenTerm"); return; }
        }
        else
        {
            MessageBox.Show(this, "同父节点请保存后在实际连接时验证。", "xOpenTerm");
            return;
        }
        if (string.IsNullOrEmpty(username)) { MessageBox.Show(this, "请填写用户名。", "xOpenTerm"); return; }
        var result = SshTester.Test(host, port, username, password, keyPath, keyPassphrase, useAgent, logContext: "节点设置");
        MessageBox.Show(this, result.Success ? "连接成功" : ("连接失败：\n" + (result.FailureReason ?? "未知原因")), "测试连接");
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveToNode()) return;
        RecordInputHistory();
        ConfirmCloseAndSave();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        if (IsDirty() && MessageBox.Show(this, "是否放弃修改？", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        ConfirmCloseAndCancel();
    }
}
