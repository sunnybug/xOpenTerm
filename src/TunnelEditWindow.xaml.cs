using System.Windows;
using System.Windows.Controls;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

public partial class TunnelEditWindow : Window
{
    private readonly Tunnel _tunnel;
    private readonly IList<Tunnel> _all;
    private readonly IList<Credential> _credentials;
    private readonly INodeEditContext _context;
    private readonly string _initialName;
    private readonly string _initialHost;
    private readonly string _initialPort;
    private readonly string _initialUsername;
    private readonly int _initialAuthIndex;
    private readonly string _initialPassword;
    private readonly string _initialKeyPath;
    private readonly string? _initialCredentialId;
    private bool _closingConfirmed;

    public TunnelEditWindow(Tunnel tunnel, IList<Tunnel> all, IList<Credential> credentials, INodeEditContext context, bool isNew)
    {
        InitializeComponent();
        _tunnel = tunnel;
        _all = all;
        _credentials = credentials;
        _context = context;
        Title = isNew ? "新增跳板机" : "编辑跳板机";
        AuthCombo.Items.Add("同父节点");
        AuthCombo.Items.Add("登录凭证");
        AuthCombo.Items.Add("密码");
        AuthCombo.Items.Add("私钥");
        AuthCombo.Items.Add("SSH Agent");
        CredentialCombo.ItemsSource = _credentials.OrderBy(c => c.AuthType).ThenBy(c => c.Name).ToList();
        var useCredential = !string.IsNullOrEmpty(_tunnel.CredentialId);
        AuthCombo.SelectedIndex = _tunnel.AuthSource == AuthSource.parent ? 0 : (useCredential ? 1 : (_tunnel.AuthType switch { AuthType.key => 3, AuthType.agent => 4, _ => 2 }));
        TunnelUseParentCheckBox.Visibility = string.IsNullOrEmpty(_tunnel.ParentId) ? Visibility.Collapsed : Visibility.Visible;
        var useParentTunnel = _tunnel.TunnelSource == AuthSource.parent;
        TunnelUseParentCheckBox.IsChecked = useParentTunnel;
        NameBox.Text = _tunnel.Name;
        HostBox.Text = _tunnel.Host ?? "";
        PortBox.Text = (_tunnel.Port ?? 22).ToString();
        UsernameBox.Text = _tunnel.Username ?? "";
        PasswordBox.Password = _tunnel.Password ?? "";
        KeyPathBox.Text = _tunnel.KeyPath ?? "";
        if (useCredential) CredentialCombo.SelectedValue = _tunnel.CredentialId;
        _initialName = NameBox.Text ?? "";
        _initialHost = HostBox.Text ?? "";
        _initialPort = PortBox.Text ?? "";
        _initialUsername = UsernameBox.Text ?? "";
        _initialAuthIndex = AuthCombo.SelectedIndex;
        _initialPassword = PasswordBox.Password ?? "";
        _initialKeyPath = KeyPathBox.Text ?? "";
        _initialCredentialId = CredentialCombo.SelectedValue as string;
        AuthCombo.SelectionChanged += (_, _) => UpdateAuthVisibility();
        UpdateAuthVisibility();
        Closing += TunnelEditWindow_Closing;
    }

    private bool IsDirty()
    {
        var credNow = CredentialCombo.SelectedValue as string;
        return (NameBox.Text ?? "") != _initialName
            || (HostBox.Text ?? "") != _initialHost
            || (PortBox.Text ?? "") != _initialPort
            || (UsernameBox.Text ?? "") != _initialUsername
            || AuthCombo.SelectedIndex != _initialAuthIndex
            || !string.Equals(credNow, _initialCredentialId, StringComparison.Ordinal)
            || PasswordBox.Password != _initialPassword
            || (KeyPathBox.Text ?? "") != _initialKeyPath;
    }

    private void TunnelEditWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closingConfirmed) return;
        if (IsDirty() && MessageBox.Show(this, "是否放弃修改？", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            e.Cancel = true;
    }

    private void UpdateAuthVisibility()
    {
        var idx = AuthCombo.SelectedIndex;
        var isCredential = idx == 1;
        var isKey = idx == 3;
        var isAgent = idx == 4;
        CredentialRow.Visibility = isCredential ? Visibility.Visible : Visibility.Collapsed;
        PasswordRow.Visibility = (idx == 2) ? Visibility.Visible : Visibility.Collapsed;
        KeyRow.Visibility = isKey ? Visibility.Visible : Visibility.Collapsed;
        UsernameRow.Visibility = (idx == 0 || idx == 1) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void TestBtn_Click(object sender, RoutedEventArgs e)
    {
        var authIdx = AuthCombo.SelectedIndex;
        if (authIdx == 0)
        {
            if (string.IsNullOrEmpty(_tunnel.ParentId))
            {
                MessageBox.Show(this, "同父节点请保存后在实际连接时验证。", "xOpenTerm");
                return;
            }
            var tempTunnel = new Tunnel
            {
                ParentId = _tunnel.ParentId,
                AuthSource = AuthSource.parent,
                TunnelSource = TunnelUseParentCheckBox.IsChecked == true ? AuthSource.parent : null
            };
            try
            {
                var resolved = ConfigResolver.ResolveTunnel(tempTunnel, _all, _credentials);
                var host = HostBox.Text?.Trim();
                if (string.IsNullOrEmpty(host)) { MessageBox.Show(this, "请填写主机。", "xOpenTerm"); return; }
                if (!ushort.TryParse(PortBox.Text, out var port) || port == 0) port = 22;
                var username = resolved.username ?? "";
                var password = resolved.password;
                var keyPath = resolved.keyPath;
                var keyPassphrase = resolved.keyPassphrase;
                var useAgent = resolved.useAgent;
                var result = SshTester.Test(host, port, username, password, keyPath, keyPassphrase, useAgent);
                MessageBox.Show(this, result.Success ? "连接成功" : ("连接失败：\n" + (result.FailureReason ?? "未知原因")), "测试连接");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "解析父节点凭证失败：\n" + ex.Message, "xOpenTerm");
                return;
            }
        }
        else
        {
            var host = HostBox.Text?.Trim();
            if (string.IsNullOrEmpty(host)) { MessageBox.Show(this, "请填写主机。", "xOpenTerm"); return; }
            if (!ushort.TryParse(PortBox.Text, out var port) || port == 0) port = 22;
            var username = UsernameBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(username)) { MessageBox.Show(this, "请填写用户名。", "xOpenTerm"); return; }
            string? password = null;
            string? keyPath = null;
            string? keyPassphrase = null;
            var useAgent = false;
            if (authIdx == 1)
            {
                if (CredentialCombo.SelectedValue is not string cid)
                {
                    MessageBox.Show(this, "请选择登录凭证。", "xOpenTerm"); return;
                }
                var cred = _credentials.FirstOrDefault(c => c.Id == cid);
                if (cred == null) { MessageBox.Show(this, "请选择登录凭证。", "xOpenTerm"); return; }
                username = cred.Username ?? username;
                switch (cred.AuthType)
                {
                    case AuthType.password: password = cred.Password; break;
                    case AuthType.key: keyPath = cred.KeyPath; keyPassphrase = cred.KeyPassphrase; break;
                    case AuthType.agent: useAgent = true; break;
                }
            }
            else if (authIdx == 2)
                password = PasswordBox.Password;
            else if (authIdx == 3)
                keyPath = KeyPathBox.Text?.Trim();
            else
                useAgent = true;
            var result = SshTester.Test(host, port, username, password, keyPath, keyPassphrase, useAgent);
            MessageBox.Show(this, result.Success ? "连接成功" : ("连接失败：\n" + (result.FailureReason ?? "未知原因")), "测试连接");
        }
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        _tunnel.Name = NameBox.Text?.Trim() ?? _tunnel.Host ?? "跳板机";
        _tunnel.Host = HostBox.Text?.Trim() ?? "";
        _tunnel.Port = ushort.TryParse(PortBox.Text, out var p) && p > 0 ? p : (ushort)22;
        _tunnel.Username = UsernameBox.Text?.Trim() ?? "";
        var authIdx = AuthCombo.SelectedIndex;
        if (authIdx == 0)
        {
            _tunnel.AuthSource = AuthSource.parent;
            _tunnel.CredentialId = null;
            _tunnel.AuthType = AuthType.password;
            _tunnel.Password = null;
            _tunnel.KeyPath = null;
            _tunnel.KeyPassphrase = null;
        }
        else if (authIdx == 1)
        {
            _tunnel.AuthSource = AuthSource.credential;
            _tunnel.CredentialId = CredentialCombo.SelectedValue as string;
            _tunnel.AuthType = AuthType.password;
            _tunnel.Password = null;
            _tunnel.KeyPath = null;
            _tunnel.KeyPassphrase = null;
        }
        else
        {
            _tunnel.AuthSource = AuthSource.inline;
            _tunnel.CredentialId = null;
            _tunnel.AuthType = authIdx switch { 3 => AuthType.key, 4 => AuthType.agent, _ => AuthType.password };
            _tunnel.Password = authIdx == 2 ? PasswordBox.Password : null;
            _tunnel.KeyPath = authIdx == 3 ? KeyPathBox.Text?.Trim() : null;
            _tunnel.KeyPassphrase = null;
        }
        _tunnel.TunnelSource = TunnelUseParentCheckBox.IsChecked == true ? AuthSource.parent : null;
        var idx = -1;
        for (var i = 0; i < _all.Count; i++)
        {
            if (_all[i].Id == _tunnel.Id) { idx = i; break; }
        }
        if (idx >= 0) _all[idx] = _tunnel;
        else _all.Add(_tunnel);
        _context.SaveTunnels();
        _closingConfirmed = true;
        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        if (IsDirty() && MessageBox.Show(this, "是否放弃修改？", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _closingConfirmed = true;
        DialogResult = false;
        Close();
    }
}
