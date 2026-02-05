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
        CredentialCombo.DisplayMemberPath = "Name";
        CredentialCombo.SelectedValuePath = "Id";
        CredentialCombo.ItemsSource = _credentials.OrderBy(c => c.AuthType).ThenBy(c => c.Name).ToList();

        if (_groupNode.Config != null)
        {
            CredentialCombo.SelectedValue = _groupNode.Config.CredentialId;
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
        var credId = CredentialCombo.SelectedValue as string;
        var tunnelIds = TunnelListBox.SelectedItems.Cast<Tunnel>().OrderBy(t => t.AuthType).ThenBy(t => t.Name).Select(t => t.Id).ToList();

        if (string.IsNullOrEmpty(credId) && (tunnelIds == null || tunnelIds.Count == 0))
        {
            _groupNode.Config = null;
        }
        else
        {
            _groupNode.Config ??= new ConnectionConfig();
            _groupNode.Config.AuthSource = string.IsNullOrEmpty(credId) ? null : AuthSource.credential;
            _groupNode.Config.CredentialId = string.IsNullOrEmpty(credId) ? null : credId;
            _groupNode.Config.TunnelIds = tunnelIds.Count == 0 ? null : tunnelIds;
            _groupNode.Config.TunnelSource = null;
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
