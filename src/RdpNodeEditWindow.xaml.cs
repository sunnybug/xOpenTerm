using System.Linq;
using System.Windows;
using System.Windows.Controls;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>RDP 节点编辑窗口：主机、端口、认证、域、控制台/剪贴板/智能缩放、RD Gateway（参考 mRemoteNG）。</summary>
public partial class RdpNodeEditWindow : NodeEditWindowBase
{
    private readonly string _initialName;
    private readonly string _initialHost;
    private readonly string _initialPort;
    private readonly string _initialUsername;
    private readonly string _initialPassword;
    private readonly string _initialDomain;
    private readonly int _initialAuthIndex;
    private readonly string? _initialCredentialId;
    private readonly bool _initialUseConsole;
    private readonly bool _initialRedirectClipboard;
    private readonly bool _initialSmartSizing;
    private readonly string _initialGatewayHost;
    private readonly int _initialGatewayUsage;
    private readonly int _initialGatewayCred;
    private readonly string _initialGatewayUser;
    private readonly string _initialGatewayDomain;
    private readonly string _initialGatewayPassword;

    public RdpNodeEditWindow(Node node, List<Node> nodes, List<Credential> credentials, List<Tunnel> tunnels, StorageService storage, bool isExistingNode = true)
        : base(node, nodes, credentials, tunnels, storage, isExistingNode)
    {
        InitializeComponent();
        NameBox.Text = node.Name ?? "";
        HostBox.Text = node.Config?.Host ?? "";
        PortBox.Text = node.Config?.Port?.ToString() ?? "3389";
        UsernameBox.Text = node.Config?.Username ?? "administrator";
        PasswordBox.Password = node.Config?.Password ?? "";
        DomainBox.Text = node.Config?.Domain ?? "";

        AuthCombo.Items.Add("同父节点");
        AuthCombo.Items.Add("登录凭证");
        AuthCombo.Items.Add("密码");
        CredentialCombo.DisplayMemberPath = "Name";
        CredentialCombo.SelectedValuePath = "Id";

        GatewayUsageCombo.Items.Add("从不");
        GatewayUsageCombo.Items.Add("始终");
        GatewayUsageCombo.Items.Add("自动检测");
        GatewayCredCombo.Items.Add("使用连接凭据");
        GatewayCredCombo.Items.Add("单独填写网关凭据");

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

            UseConsoleCheck.IsChecked = cfg.RdpUseConsoleSession == true;
            RedirectClipboardCheck.IsChecked = cfg.RdpRedirectClipboard == true;
            SmartSizingCheck.IsChecked = cfg.RdpSmartSizing == true;

            GatewayHostBox.Text = cfg.RdpGatewayHostname ?? "";
            GatewayUsageCombo.SelectedIndex = Math.Clamp(cfg.RdpGatewayUsageMethod ?? 0, 0, 2);
            GatewayCredCombo.SelectedIndex = (cfg.RdpGatewayUseConnectionCredentials ?? 1) == 1 ? 0 : 1;
            GatewayUserBox.Text = cfg.RdpGatewayUsername ?? "";
            GatewayDomainBox.Text = cfg.RdpGatewayDomain ?? "";
            GatewayPasswordBox.Password = cfg.RdpGatewayPassword ?? "";
            if (!string.IsNullOrWhiteSpace(cfg.RdpGatewayHostname))
                GatewayExpander.IsExpanded = true;
        }
        AuthCombo_SelectionChanged(null!, null!);
        GatewayUsageCombo_SelectionChanged(null!, null!);
        GatewayCredCombo_SelectionChanged(null!, null!);

        _initialName = NameBox.Text ?? "";
        _initialHost = HostBox.Text ?? "";
        _initialPort = PortBox.Text ?? "";
        _initialUsername = UsernameBox.Text ?? "";
        _initialPassword = PasswordBox.Password ?? "";
        _initialDomain = DomainBox.Text ?? "";
        _initialAuthIndex = AuthCombo.SelectedIndex;
        _initialCredentialId = CredentialCombo.SelectedValue as string;
        _initialUseConsole = UseConsoleCheck.IsChecked == true;
        _initialRedirectClipboard = RedirectClipboardCheck.IsChecked == true;
        _initialSmartSizing = SmartSizingCheck.IsChecked == true;
        _initialGatewayHost = GatewayHostBox.Text ?? "";
        _initialGatewayUsage = GatewayUsageCombo.SelectedIndex;
        _initialGatewayCred = GatewayCredCombo.SelectedIndex;
        _initialGatewayUser = GatewayUserBox.Text ?? "";
        _initialGatewayDomain = GatewayDomainBox.Text ?? "";
        _initialGatewayPassword = GatewayPasswordBox.Password ?? "";
        RegisterClosing();
    }

    protected override bool IsDirty()
    {
        if ((NameBox.Text ?? "") != _initialName) return true;
        if ((HostBox.Text ?? "") != _initialHost) return true;
        if ((PortBox.Text ?? "") != _initialPort) return true;
        if ((UsernameBox.Text ?? "") != _initialUsername) return true;
        if (PasswordBox.Password != _initialPassword) return true;
        if ((DomainBox.Text ?? "") != _initialDomain) return true;
        if (AuthCombo.SelectedIndex != _initialAuthIndex) return true;
        var credNow = CredentialCombo.SelectedValue as string;
        if (!string.Equals(credNow, _initialCredentialId, StringComparison.Ordinal)) return true;
        if (UseConsoleCheck.IsChecked == true != _initialUseConsole) return true;
        if (RedirectClipboardCheck.IsChecked == true != _initialRedirectClipboard) return true;
        if (SmartSizingCheck.IsChecked == true != _initialSmartSizing) return true;
        if ((GatewayHostBox.Text ?? "") != _initialGatewayHost) return true;
        if (GatewayUsageCombo.SelectedIndex != _initialGatewayUsage) return true;
        if (GatewayCredCombo.SelectedIndex != _initialGatewayCred) return true;
        if ((GatewayUserBox.Text ?? "") != _initialGatewayUser) return true;
        if ((GatewayDomainBox.Text ?? "") != _initialGatewayDomain) return true;
        if (GatewayPasswordBox.Password != _initialGatewayPassword) return true;
        return false;
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
        _node.Config.Domain = string.IsNullOrWhiteSpace(DomainBox.Text) ? null : DomainBox.Text?.Trim();

        _node.Config.RdpUseConsoleSession = UseConsoleCheck.IsChecked == true;
        _node.Config.RdpRedirectClipboard = RedirectClipboardCheck.IsChecked == true;
        _node.Config.RdpSmartSizing = SmartSizingCheck.IsChecked == true;

        _node.Config.RdpGatewayHostname = string.IsNullOrWhiteSpace(GatewayHostBox.Text) ? null : GatewayHostBox.Text?.Trim();
        _node.Config.RdpGatewayUsageMethod = GatewayUsageCombo.SelectedIndex;
        _node.Config.RdpGatewayUseConnectionCredentials = GatewayCredCombo.SelectedIndex == 0 ? 1 : 0;
        _node.Config.RdpGatewayUsername = GatewayCredCombo.SelectedIndex == 1 ? (GatewayUserBox.Text?.Trim()) : null;
        _node.Config.RdpGatewayDomain = GatewayCredCombo.SelectedIndex == 1 ? (GatewayDomainBox.Text?.Trim()) : null;
        _node.Config.RdpGatewayPassword = GatewayCredCombo.SelectedIndex == 1 ? (string.IsNullOrEmpty(GatewayPasswordBox.Password) ? null : GatewayPasswordBox.Password) : null;
        return true;
    }

    private void AuthCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var idx = AuthCombo.SelectedIndex;
        CredentialRow.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        PasswordRow.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
        UsernameRow.Visibility = (idx == 0 || idx == 1) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void GatewayUsageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 无逻辑依赖，仅占位便于扩展
    }

    private void GatewayCredCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        GatewayCredPanel.Visibility = GatewayCredCombo.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
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
