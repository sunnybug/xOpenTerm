using System.Linq;
using System.Windows;
using System.Windows.Controls;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>分组（父节点）默认设置：默认认证凭证与默认跳板，仅保存于本节点；子节点在自身设置中选「同父节点」时生效。</summary>
public partial class GroupSettingsWindow : Window
{
    private readonly Node _groupNode;
    private readonly List<Node> _nodes;
    private readonly List<Credential> _credentials;
    private readonly List<Tunnel> _tunnels;
    private readonly StorageService _storage;

    public GroupSettingsWindow(Node groupNode, List<Node> nodes, List<Credential> credentials, List<Tunnel> tunnels, StorageService storage)
    {
        InitializeComponent();
        _groupNode = groupNode;
        _nodes = nodes;
        _credentials = credentials;
        _tunnels = tunnels;
        _storage = storage;

        Title = $"分组默认设置 - {groupNode.Name}";
        var credList = _credentials.OrderBy(c => c.AuthType).ThenBy(c => c.Name).ToList();
        foreach (var combo in new[] { SshCredentialCombo, RdpCredentialCombo })
        {
            combo.DisplayMemberPath = "Name";
            combo.SelectedValuePath = "Id";
            combo.ItemsSource = credList;
        }

        if (_groupNode.Config != null)
        {
            SshCredentialCombo.SelectedValue = _groupNode.Config.SshCredentialId ?? _groupNode.Config.CredentialId;
            RdpCredentialCombo.SelectedValue = _groupNode.Config.RdpCredentialId ?? _groupNode.Config.CredentialId;
            RefreshTunnelList(_groupNode.Config.TunnelIds);
        }
        else
        {
            RefreshTunnelList(null);
        }
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

    private void TunnelManageBtn_Click(object sender, RoutedEventArgs e)
    {
        var currentSel = TunnelListBox.SelectedItems.Cast<Tunnel>().Select(t => t.Id).ToList();
        var win = new TunnelManagerWindow(this);
        win.ShowDialog();
        _tunnels.Clear();
        _tunnels.AddRange(_storage.LoadTunnels());
        RefreshTunnelList(currentSel);
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        var sshCredId = SshCredentialCombo.SelectedValue as string;
        var rdpCredId = RdpCredentialCombo.SelectedValue as string;
        var tunnelIds = TunnelListBox.SelectedItems.Cast<Tunnel>().OrderBy(t => t.AuthType).ThenBy(t => t.Name).Select(t => t.Id).ToList();

        var hasCred = !string.IsNullOrEmpty(sshCredId) || !string.IsNullOrEmpty(rdpCredId);
        var isTencentGroup = _groupNode.Type == NodeType.tencentCloudGroup;
        if (!hasCred && (tunnelIds == null || tunnelIds.Count == 0) && !isTencentGroup)
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
            _groupNode.Config.TunnelIds = (tunnelIds?.Count ?? 0) == 0 ? null : tunnelIds;
            _groupNode.Config.TunnelSource = null;
            // 腾讯云组不覆盖密钥，由新增时填写
        }

        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
