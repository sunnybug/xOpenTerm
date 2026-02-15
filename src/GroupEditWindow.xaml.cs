using System.Linq;
using System.Windows;
using System.Windows.Controls;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>分组（父节点）编辑：默认认证凭证与默认跳板，仅保存于本节点；子节点在自身设置中选「同父节点」时生效。</summary>
public partial class GroupEditWindow : Window
{
    private readonly Node _groupNode;
    private readonly IList<Node> _nodes;
    private readonly IList<Credential> _credentials;
    private readonly IList<Tunnel> _tunnels;
    private readonly INodeEditContext _context;
    private readonly int _initialSshAuthIndex;
    private readonly int _initialRdpAuthIndex;
    private readonly string? _initialSshCredId;
    private readonly string? _initialRdpCredId;
    private readonly bool _initialTunnelUseParent;
    private readonly List<string> _initialTunnelIds;
    private readonly string _initialTencentSecretId;
    private readonly string _initialTencentSecretKey;
    private readonly string _initialAliAccessKeyId;
    private readonly string _initialAliAccessKeySecret;
    private readonly string _initialKsyunAccessKeyId;
    private readonly string _initialKsyunAccessKeySecret;
    private bool _closingConfirmed;

    public GroupEditWindow(Node groupNode, INodeEditContext context)
    {
        InitializeComponent();
        _groupNode = groupNode;
        _context = context;
        _nodes = context.Nodes;
        _credentials = context.Credentials;
        _tunnels = context.Tunnels;

        Title = $"分组默认设置 - {groupNode.Name}";
        var credList = _credentials.OrderBy(c => c.AuthType).ThenBy(c => c.Name).ToList();

        SshAuthCombo.Items.Add("同父节点");
        SshAuthCombo.Items.Add("登录凭证");
        RdpAuthCombo.Items.Add("同父节点");
        RdpAuthCombo.Items.Add("登录凭证");

        foreach (var combo in new[] { SshCredentialCombo, RdpCredentialCombo })
        {
            combo.DisplayMemberPath = "Name";
            combo.SelectedValuePath = "Id";
            combo.ItemsSource = credList;
        }

        if (_groupNode.Config != null)
        {
            var sshCredId = _groupNode.Config.SshCredentialId ?? _groupNode.Config.CredentialId;
            if (!string.IsNullOrEmpty(sshCredId))
            {
                SshAuthCombo.SelectedIndex = 1;
                SshCredentialCombo.SelectedValue = sshCredId;
                SshCredentialRow.Visibility = Visibility.Visible;
            }
            else
            {
                SshAuthCombo.SelectedIndex = 0;
                SshCredentialRow.Visibility = Visibility.Collapsed;
            }
            var rdpCredId = _groupNode.Config.RdpCredentialId ?? _groupNode.Config.CredentialId;
            if (!string.IsNullOrEmpty(rdpCredId))
            {
                RdpAuthCombo.SelectedIndex = 1;
                RdpCredentialCombo.SelectedValue = rdpCredId;
                RdpCredentialRow.Visibility = Visibility.Visible;
            }
            else
            {
                RdpAuthCombo.SelectedIndex = 0;
                RdpCredentialRow.Visibility = Visibility.Collapsed;
            }
            TunnelUseParentCheckBox.Visibility = string.IsNullOrEmpty(_groupNode.ParentId) ? Visibility.Collapsed : Visibility.Visible;
            var useParentTunnel = _groupNode.Config.TunnelSource == AuthSource.parent;
            TunnelUseParentCheckBox.IsChecked = useParentTunnel;
            RefreshTunnelList(useParentTunnel ? null : _groupNode.Config.TunnelIds);
        }
        else
        {
            SshAuthCombo.SelectedIndex = 0;
            RdpAuthCombo.SelectedIndex = 0;
            SshCredentialRow.Visibility = Visibility.Collapsed;
            RdpCredentialRow.Visibility = Visibility.Collapsed;
            TunnelUseParentCheckBox.Visibility = string.IsNullOrEmpty(_groupNode.ParentId) ? Visibility.Collapsed : Visibility.Visible;
            TunnelUseParentCheckBox.IsChecked = false;
            RefreshTunnelList(null);
        }
        UpdateTunnelListEnabled();

        // 腾讯云/阿里云父节点：显示云 API 密钥区域并加载已保存的密钥
        if (_groupNode.Type == NodeType.tencentCloudGroup)
        {
            CloudKeysPanel.Visibility = Visibility.Visible;
            TencentKeysPanel.Visibility = Visibility.Visible;
            AliKeysPanel.Visibility = Visibility.Collapsed;
            KingsoftKeysPanel.Visibility = Visibility.Collapsed;
            TencentSecretIdBox.Text = _groupNode.Config?.TencentSecretId ?? "";
            TencentSecretKeyBox.Password = _groupNode.Config?.TencentSecretKey ?? "";
            Height = 440;
        }
        else if (_groupNode.Type == NodeType.aliCloudGroup)
        {
            CloudKeysPanel.Visibility = Visibility.Visible;
            TencentKeysPanel.Visibility = Visibility.Collapsed;
            AliKeysPanel.Visibility = Visibility.Visible;
            KingsoftKeysPanel.Visibility = Visibility.Collapsed;
            AliAccessKeyIdBox.Text = _groupNode.Config?.AliAccessKeyId ?? "";
            AliAccessKeySecretBox.Password = _groupNode.Config?.AliAccessKeySecret ?? "";
            Height = 440;
        }
        else if (_groupNode.Type == NodeType.kingsoftCloudGroup)
        {
            CloudKeysPanel.Visibility = Visibility.Visible;
            TencentKeysPanel.Visibility = Visibility.Collapsed;
            AliKeysPanel.Visibility = Visibility.Collapsed;
            KingsoftKeysPanel.Visibility = Visibility.Visible;
            KsyunAccessKeyIdBox.Text = _groupNode.Config?.KsyunAccessKeyId ?? "";
            KsyunAccessKeySecretBox.Password = _groupNode.Config?.KsyunAccessKeySecret ?? "";
            Height = 440;
        }

        _initialSshAuthIndex = SshAuthCombo.SelectedIndex;
        _initialRdpAuthIndex = RdpAuthCombo.SelectedIndex;
        _initialSshCredId = SshCredentialCombo.SelectedValue as string;
        _initialRdpCredId = RdpCredentialCombo.SelectedValue as string;
        _initialTunnelUseParent = TunnelUseParentCheckBox.IsChecked == true;
        _initialTunnelIds = TunnelListBox.SelectedItems.Cast<Tunnel>().Select(t => t.Id).ToList();
        _initialTencentSecretId = TencentSecretIdBox.Text ?? "";
        _initialTencentSecretKey = TencentSecretKeyBox.Password ?? "";
        _initialAliAccessKeyId = AliAccessKeyIdBox.Text ?? "";
        _initialAliAccessKeySecret = AliAccessKeySecretBox.Password ?? "";
        _initialKsyunAccessKeyId = KsyunAccessKeyIdBox.Text ?? "";
        _initialKsyunAccessKeySecret = KsyunAccessKeySecretBox.Password ?? "";
        Closing += GroupEditWindow_Closing;
    }

    private void SshAuthCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var useCred = SshAuthCombo.SelectedIndex == 1;
        SshCredentialRow.Visibility = useCred ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RdpAuthCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var useCred = RdpAuthCombo.SelectedIndex == 1;
        RdpCredentialRow.Visibility = useCred ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool IsDirty()
    {
        if (SshAuthCombo.SelectedIndex != _initialSshAuthIndex || RdpAuthCombo.SelectedIndex != _initialRdpAuthIndex)
            return true;
        var sshNow = SshAuthCombo.SelectedIndex == 1 ? (SshCredentialCombo.SelectedValue as string) : null;
        var rdpNow = RdpAuthCombo.SelectedIndex == 1 ? (RdpCredentialCombo.SelectedValue as string) : null;
        if (!string.Equals(sshNow, _initialSshCredId, StringComparison.Ordinal) || !string.Equals(rdpNow, _initialRdpCredId, StringComparison.Ordinal))
            return true;
        if ((TunnelUseParentCheckBox.IsChecked == true) != _initialTunnelUseParent)
            return true;
        var tunnelIdsNow = TunnelListBox.SelectedItems.Cast<Tunnel>().Select(t => t.Id).ToList();
        if (tunnelIdsNow.Count != _initialTunnelIds.Count || tunnelIdsNow.Except(_initialTunnelIds).Any())
            return true;
        if (_groupNode.Type == NodeType.tencentCloudGroup)
            return TencentSecretIdBox.Text != _initialTencentSecretId || TencentSecretKeyBox.Password != _initialTencentSecretKey;
        if (_groupNode.Type == NodeType.aliCloudGroup)
            return AliAccessKeyIdBox.Text != _initialAliAccessKeyId || AliAccessKeySecretBox.Password != _initialAliAccessKeySecret;
        if (_groupNode.Type == NodeType.kingsoftCloudGroup)
            return KsyunAccessKeyIdBox.Text != _initialKsyunAccessKeyId || KsyunAccessKeySecretBox.Password != _initialKsyunAccessKeySecret;
        return false;
    }

    private void GroupEditWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closingConfirmed) return;
        if (IsDirty() && MessageBox.Show(this, "是否放弃修改？", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            e.Cancel = true;
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

    private void UpdateTunnelListEnabled()
    {
        var useParent = TunnelUseParentCheckBox.IsChecked == true;
        TunnelListBox.IsEnabled = !useParent;
        TunnelManageBtn.IsEnabled = !useParent;
    }

    private void TunnelUseParentCheckBox_Changed(object sender, RoutedEventArgs e) => UpdateTunnelListEnabled();

    private void TunnelManageBtn_Click(object sender, RoutedEventArgs e)
    {
        var currentSel = TunnelListBox.SelectedItems.Cast<Tunnel>().Select(t => t.Id).ToList();
        var win = new TunnelManagerWindow(this);
        win.ShowDialog();
        _context.ReloadTunnels();
        RefreshTunnelList(currentSel);
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        var sshCredId = SshAuthCombo.SelectedIndex == 1 ? (SshCredentialCombo.SelectedValue as string) : null;
        var rdpCredId = RdpAuthCombo.SelectedIndex == 1 ? (RdpCredentialCombo.SelectedValue as string) : null;
        var useParentTunnel = TunnelUseParentCheckBox.IsChecked == true;
        var tunnelIds = useParentTunnel ? null : TunnelListBox.SelectedItems.Cast<Tunnel>().OrderBy(t => t.AuthType).ThenBy(t => t.Name).Select(t => t.Id).ToList();

        var hasCred = !string.IsNullOrEmpty(sshCredId) || !string.IsNullOrEmpty(rdpCredId);
        var hasTunnel = useParentTunnel || (tunnelIds != null && tunnelIds.Count > 0);
        var isCloudGroup = _groupNode.Type == NodeType.tencentCloudGroup || _groupNode.Type == NodeType.aliCloudGroup || _groupNode.Type == NodeType.kingsoftCloudGroup;
        if (!hasCred && !hasTunnel && !isCloudGroup)
        {
            _groupNode.Config = null;
        }
        else
        {
            _groupNode.Config ??= new ConnectionConfig();
            _groupNode.Config.SshCredentialId = string.IsNullOrEmpty(sshCredId) ? null : sshCredId;
            _groupNode.Config.RdpCredentialId = string.IsNullOrEmpty(rdpCredId) ? null : rdpCredId;
            _groupNode.Config.CredentialId = _groupNode.Config.SshCredentialId ?? _groupNode.Config.RdpCredentialId;
            _groupNode.Config.AuthSource = hasCred ? AuthSource.credential : null;
            _groupNode.Config.TunnelSource = useParentTunnel ? AuthSource.parent : null;
            _groupNode.Config.TunnelIds = (tunnelIds?.Count ?? 0) == 0 ? null : tunnelIds;
            // 云组父节点：保存云 API 密钥（AccessKeyId/Secret 或 SecretId/SecretKey）
            if (_groupNode.Type == NodeType.tencentCloudGroup)
            {
                _groupNode.Config.TencentSecretId = TencentSecretIdBox.Text?.Trim() ?? "";
                _groupNode.Config.TencentSecretKey = TencentSecretKeyBox.Password ?? "";
            }
            else if (_groupNode.Type == NodeType.aliCloudGroup)
            {
                _groupNode.Config.AliAccessKeyId = AliAccessKeyIdBox.Text?.Trim() ?? "";
                _groupNode.Config.AliAccessKeySecret = AliAccessKeySecretBox.Password ?? "";
            }
            else if (_groupNode.Type == NodeType.kingsoftCloudGroup)
            {
                _groupNode.Config.KsyunAccessKeyId = KsyunAccessKeyIdBox.Text?.Trim() ?? "";
                _groupNode.Config.KsyunAccessKeySecret = KsyunAccessKeySecretBox.Password ?? "";
            }
        }

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
