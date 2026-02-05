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
    private bool _closingConfirmed;

    public TunnelEditWindow(Tunnel tunnel, List<Tunnel> all, List<Credential> credentials, StorageService storage, bool isNew)
    {
        InitializeComponent();
        _tunnel = tunnel;
        _all = all;
        _credentials = credentials;
        _storage = storage;
        Title = isNew ? "新增跳板机" : "编辑跳板机";
        AuthCombo.Items.Add("密码");
        AuthCombo.Items.Add("私钥");
        AuthCombo.Items.Add("SSH Agent");
        AuthCombo.SelectedIndex = _tunnel.AuthType switch { AuthType.key => 1, AuthType.agent => 2, _ => 0 };
        NameBox.Text = _tunnel.Name;
        HostBox.Text = _tunnel.Host ?? "";
        PortBox.Text = (_tunnel.Port ?? 22).ToString();
        UsernameBox.Text = _tunnel.Username ?? "";
        PasswordBox.Password = _tunnel.Password ?? "";
        KeyPathBox.Text = _tunnel.KeyPath ?? "";
        _initialName = NameBox.Text ?? "";
        _initialHost = HostBox.Text ?? "";
        _initialPort = PortBox.Text ?? "";
        _initialUsername = UsernameBox.Text ?? "";
        _initialAuthIndex = AuthCombo.SelectedIndex;
        _initialPassword = PasswordBox.Password ?? "";
        _initialKeyPath = KeyPathBox.Text ?? "";
        AuthCombo.SelectionChanged += (_, _) => UpdateAuthVisibility();
        UpdateAuthVisibility();
        Closing += TunnelEditWindow_Closing;
    }

    private bool IsDirty()
    {
        return (NameBox.Text ?? "") != _initialName
            || (HostBox.Text ?? "") != _initialHost
            || (PortBox.Text ?? "") != _initialPort
            || (UsernameBox.Text ?? "") != _initialUsername
            || AuthCombo.SelectedIndex != _initialAuthIndex
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
        var isAgent = idx == 2;
        var isKey = idx == 1;
        PasswordRow.Visibility = (isKey || isAgent) ? Visibility.Collapsed : Visibility.Visible;
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
        var authIdx = AuthCombo.SelectedIndex;
        if (authIdx == 0)
            password = PasswordBox.Password;
        else if (authIdx == 1)
            keyPath = KeyPathBox.Text?.Trim();
        var useAgent = authIdx == 2;
        var ok = SshTester.Test(host, port, username, password, keyPath, keyPassphrase, useAgent);
        MessageBox.Show(ok ? "连接成功" : "连接失败", "测试连接");
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        _tunnel.Name = NameBox.Text?.Trim() ?? _tunnel.Host ?? "跳板机";
        _tunnel.Host = HostBox.Text?.Trim() ?? "";
        _tunnel.Port = ushort.TryParse(PortBox.Text, out var p) && p > 0 ? p : (ushort)22;
        _tunnel.Username = UsernameBox.Text?.Trim() ?? "";
        var authIdx = AuthCombo.SelectedIndex;
        _tunnel.AuthType = authIdx switch { 1 => AuthType.key, 2 => AuthType.agent, _ => AuthType.password };
        _tunnel.Password = authIdx == 0 ? PasswordBox.Password : null;
        _tunnel.KeyPath = authIdx == 1 ? KeyPathBox.Text?.Trim() : null;
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
