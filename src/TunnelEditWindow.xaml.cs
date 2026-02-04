using System.Windows;
using System.Windows.Controls;
using xOpenTerm2.Models;
using xOpenTerm2.Services;

namespace xOpenTerm2;

public partial class TunnelEditWindow : Window
{
    private readonly Tunnel _tunnel;
    private readonly List<Tunnel> _all;
    private readonly List<Credential> _credentials;
    private readonly StorageService _storage;

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
        AuthCombo.SelectedIndex = _tunnel.AuthType == AuthType.key ? 1 : 0;
        NameBox.Text = _tunnel.Name;
        HostBox.Text = _tunnel.Host ?? "";
        PortBox.Text = (_tunnel.Port ?? 22).ToString();
        UsernameBox.Text = _tunnel.Username ?? "";
        PasswordBox.Password = _tunnel.Password ?? "";
        KeyPathBox.Text = _tunnel.KeyPath ?? "";
        AuthCombo.SelectionChanged += (_, _) => UpdateAuthVisibility();
        UpdateAuthVisibility();
    }

    private void UpdateAuthVisibility()
    {
        var isKey = AuthCombo.SelectedIndex == 1;
        PasswordRow.Visibility = isKey ? Visibility.Collapsed : Visibility.Visible;
        KeyRow.Visibility = isKey ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TestBtn_Click(object sender, RoutedEventArgs e)
    {
        var host = HostBox.Text?.Trim();
        if (string.IsNullOrEmpty(host)) { MessageBox.Show("请填写主机。", "xOpenTerm2"); return; }
        if (!ushort.TryParse(PortBox.Text, out var port) || port == 0) port = 22;
        var username = UsernameBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(username)) { MessageBox.Show("请填写用户名。", "xOpenTerm2"); return; }
        string? password = null;
        string? keyPath = null;
        string? keyPassphrase = null;
        if (AuthCombo.SelectedIndex == 0)
            password = PasswordBox.Password;
        else
            keyPath = KeyPathBox.Text?.Trim();
        var ok = SshTester.Test(host, port, username, password, keyPath, keyPassphrase);
        MessageBox.Show(ok ? "连接成功" : "连接失败", "测试连接");
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        _tunnel.Name = NameBox.Text?.Trim() ?? _tunnel.Host ?? "跳板机";
        _tunnel.Host = HostBox.Text?.Trim() ?? "";
        _tunnel.Port = ushort.TryParse(PortBox.Text, out var p) && p > 0 ? p : (ushort)22;
        _tunnel.Username = UsernameBox.Text?.Trim() ?? "";
        _tunnel.AuthType = AuthCombo.SelectedIndex == 1 ? AuthType.key : AuthType.password;
        _tunnel.Password = AuthCombo.SelectedIndex == 0 ? PasswordBox.Password : null;
        _tunnel.KeyPath = AuthCombo.SelectedIndex == 1 ? KeyPathBox.Text?.Trim() : null;
        var list = new List<Tunnel>(_all);
        var idx = list.FindIndex(t => t.Id == _tunnel.Id);
        if (idx >= 0) list[idx] = _tunnel;
        else list.Add(_tunnel);
        _storage.SaveTunnels(list);
        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
