using System.Linq;
using System.Windows;
using System.Windows.Controls;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>RDP 节点编辑窗口：主机、端口、认证（同父节点/登录凭证/密码）。</summary>
public partial class RdpNodeEditWindow : NodeEditWindowBase
{
    private readonly string _initialName;
    private readonly string _initialHost;
    private readonly string _initialPort;
    private readonly string _initialUsername;
    private readonly string _initialPassword;
    private readonly int _initialAuthIndex;
    private readonly string? _initialCredentialId;

    public RdpNodeEditWindow(Node node, List<Node> nodes, List<Credential> credentials, List<Tunnel> tunnels, StorageService storage, bool isExistingNode = true)
        : base(node, nodes, credentials, tunnels, storage, isExistingNode)
    {
        InitializeComponent();
        NameBox.Text = node.Name ?? "";
        HostBox.Text = node.Config?.Host ?? "";
        PortBox.Text = node.Config?.Port?.ToString() ?? "3389";
        UsernameBox.Text = node.Config?.Username ?? "administrator";
        PasswordBox.Password = node.Config?.Password ?? "";

        AuthCombo.Items.Add("同父节点");
        AuthCombo.Items.Add("登录凭证");
        AuthCombo.Items.Add("密码");
        CredentialCombo.DisplayMemberPath = "Name";
        CredentialCombo.SelectedValuePath = "Id";

        var cfg = node.Config;
        if (cfg != null)
        {
            var authIdx = cfg.AuthSource switch
            {
                AuthSource.parent => 0,
                AuthSource.credential => 1,
                _ => 2
            };
            AuthCombo.SelectedIndex = authIdx;
            RefreshCredentialCombo(CredentialCombo);
            CredentialCombo.SelectedValue = cfg.CredentialId;
        }
        AuthCombo_SelectionChanged(null!, null!);

        _initialName = NameBox.Text ?? "";
        _initialHost = HostBox.Text ?? "";
        _initialPort = PortBox.Text ?? "";
        _initialUsername = UsernameBox.Text ?? "";
        _initialPassword = PasswordBox.Password ?? "";
        _initialAuthIndex = AuthCombo.SelectedIndex;
        _initialCredentialId = CredentialCombo.SelectedValue as string;
        RegisterClosing();
    }

    protected override bool IsDirty()
    {
        if ((NameBox.Text ?? "") != _initialName) return true;
        if ((HostBox.Text ?? "") != _initialHost) return true;
        if ((PortBox.Text ?? "") != _initialPort) return true;
        if ((UsernameBox.Text ?? "") != _initialUsername) return true;
        if (PasswordBox.Password != _initialPassword) return true;
        if (AuthCombo.SelectedIndex != _initialAuthIndex) return true;
        var credNow = CredentialCombo.SelectedValue as string;
        return !string.Equals(credNow, _initialCredentialId, StringComparison.Ordinal);
    }

    protected override bool SaveToNode()
    {
        var name = NameBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(HostBox.Text?.Trim()))
            name = HostBox.Text!.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show(this, "请输入名称。", "xOpenTerm");
            return false;
        }
        _node.Name = name;
        _node.Config ??= new ConnectionConfig();
        _node.Config.Host = HostBox.Text?.Trim();
        _node.Config.Port = ushort.TryParse(PortBox.Text, out var pr) && pr > 0 ? pr : (ushort)3389;
        var authIdx = AuthCombo.SelectedIndex;
        _node.Config.AuthSource = authIdx switch { 0 => AuthSource.parent, 1 => AuthSource.credential, _ => AuthSource.inline };
        _node.Config.CredentialId = authIdx == 1 && CredentialCombo.SelectedValue is string rdpCid ? rdpCid : null;
        _node.Config.Password = authIdx != 0 && authIdx != 1 ? PasswordBox.Password : null;
        _node.Config.Username = (authIdx == 0 || authIdx == 1) ? null : (UsernameBox.Text?.Trim() ?? "administrator");
        return true;
    }

    private void AuthCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var idx = AuthCombo.SelectedIndex;
        CredentialRow.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        PasswordRow.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
        UsernameRow.Visibility = (idx == 0 || idx == 1) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveToNode()) return;
        ConfirmCloseAndSave();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        if (IsDirty() && MessageBox.Show(this, "是否放弃修改？", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        ConfirmCloseAndCancel();
    }
}
