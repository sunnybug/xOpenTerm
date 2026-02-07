using System.Windows;
using System.Windows.Controls;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

public partial class TunnelEditWindow : Window
{
    private readonly Tunnel _tunnel;
    private readonly List<Tunnel> _all;
    private readonly List<Credential> _credentials;
    private readonly StorageService _storage;
    private readonly string _initialName;
    private readonly string _initialHost;
    private readonly string _initialPort;
    private readonly string _initialUsername;
    private readonly int _initialAuthIndex;
    private readonly string _initialPassword;
    private readonly string _initialKeyPath;
    private readonly string? _initialCredentialId;
    private bool _closingConfirmed;

    public TunnelEditWindow(Tunnel tunnel, List<Tunnel> all, List<Credential> credentials, StorageService storage, bool isNew)
    {
        InitializeComponent();
        _tunnel = tunnel;
        _all = all;
        _credentials = credentials;
        _storage = storage;
        Title = isNew ? "新增跳板机" : "编辑跳板机";
        AuthCombo.Items.Add("登录凭证");
        AuthCombo.Items.Add("密码");
        AuthCombo.Items.Add("私钥");
        AuthCombo.Items.Add("SSH Agent");
        CredentialCombo.ItemsSource = _credentials.OrderBy(c => c.AuthType).ThenBy(c => c.Name).ToList();
        var useCredential = !string.IsNullOrEmpty(_tunnel.CredentialId);
        AuthCombo.SelectedIndex = useCredential ? 0 : (_tunnel.AuthType switch { AuthType.key => 2, AuthType.agent => 3, _ => 1 });
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
        if (IsDirty() && MessageBox.Show("是否放弃修改？", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            e.Cancel = true;
    }

    private void UpdateAuthVisibility()
    {
        var idx = AuthCombo.SelectedIndex;
        var isCredential = idx == 0;
        var isKey = idx == 2;
        var isAgent = idx == 3;
        CredentialRow.Visibility = isCredential ? Visibility.Visible : Visibility.Collapsed;
        PasswordRow.Visibility = (idx == 1) ? Visibility.Visible : Visibility.Collapsed;
        KeyRow.Visibility = isKey ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TestBtn_Click(object sender, RoutedEventArgs e)
    {
        var host = HostBox.Text?.Trim();
        if (string.IsNullOrEmpty(host)) { MessageBox.Show("请填写主机。", "xOpenTerm"); return; }
        if (!ushort.TryParse(PortBox.Text, out var port) || port == 0) port = 22;
        var username = UsernameBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(username)) { MessageBox.Show("请填写用户名。", "xOpenTerm"); return; }
        string? password = null;
        string? keyPath = null;
        string? keyPassphrase = null;
        var useAgent = false;
        var authIdx = AuthCombo.SelectedIndex;
        if (authIdx == 0)
        {
            if (CredentialCombo.SelectedValue is not string cid)
            { MessageBox.Show("请选择登录凭证。", "xOpenTerm"); return; }
            var cred = _credentials.FirstOrDefault(c => c.Id == cid);
            if (cred == null) { MessageBox.Show("请选择登录凭证。", "xOpenTerm"); return; }
            username = cred.Username ?? username;
            switch (cred.AuthType)
            {
                case AuthType.password: password = cred.Password; break;
                case AuthType.key: keyPath = cred.KeyPath; keyPassphrase = cred.KeyPassphrase; break;
                case AuthType.agent: useAgent = true; break;
            }
        }
        else if (authIdx == 1)
            password = PasswordBox.Password;
        else if (authIdx == 2)
            keyPath = KeyPathBox.Text?.Trim();
        else
            useAgent = true;
        var result = SshTester.Test(host, port, username, password, keyPath, keyPassphrase, useAgent);
        MessageBox.Show(result.Success ? "连接成功" : ("连接失败：\n" + (result.FailureReason ?? "未知原因")), "测试连接");
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
            _tunnel.CredentialId = CredentialCombo.SelectedValue as string;
            _tunnel.AuthType = AuthType.password;
            _tunnel.Password = null;
            _tunnel.KeyPath = null;
            _tunnel.KeyPassphrase = null;
        }
        else
        {
            _tunnel.CredentialId = null;
            _tunnel.AuthType = authIdx switch { 2 => AuthType.key, 3 => AuthType.agent, _ => AuthType.password };
            _tunnel.Password = authIdx == 1 ? PasswordBox.Password : null;
            _tunnel.KeyPath = authIdx == 2 ? KeyPathBox.Text?.Trim() : null;
            _tunnel.KeyPassphrase = null;
        }
        var list = new List<Tunnel>(_all);
        var idx = list.FindIndex(t => t.Id == _tunnel.Id);
        if (idx >= 0) list[idx] = _tunnel;
        else list.Add(_tunnel);
        _storage.SaveTunnels(list);
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
