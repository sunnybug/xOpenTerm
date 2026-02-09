using System.Windows;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>本地终端节点编辑窗口：名称与协议（PowerShell/CMD）。</summary>
public partial class LocalNodeEditWindow : NodeEditWindowBase
{
    private readonly string _initialName;
    private readonly int _initialProtocolIndex;

    public LocalNodeEditWindow(Node node, List<Node> nodes, List<Credential> credentials, List<Tunnel> tunnels, StorageService storage, bool isExistingNode = true)
        : base(node, nodes, credentials, tunnels, storage, isExistingNode)
    {
        InitializeComponent();
        NameBox.Text = node.Name ?? "";
        ProtocolCombo.Items.Add("PowerShell");
        ProtocolCombo.Items.Add("CMD");
        var proto = node.Config?.Protocol ?? Protocol.powershell;
        ProtocolCombo.SelectedIndex = proto == Protocol.cmd ? 1 : 0;
        _initialName = NameBox.Text ?? "";
        _initialProtocolIndex = ProtocolCombo.SelectedIndex;
        RegisterClosing();
    }

    protected override bool IsDirty() =>
        (NameBox.Text ?? "") != _initialName ||
        ProtocolCombo.SelectedIndex != _initialProtocolIndex;

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
        _node.Config.Protocol = ProtocolCombo.SelectedIndex == 1 ? Protocol.cmd : Protocol.powershell;
        return true;
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
