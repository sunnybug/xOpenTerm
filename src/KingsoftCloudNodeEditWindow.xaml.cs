using System.Windows;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>金山云组节点编辑窗口：仅编辑组名称，密钥在「设置」中配置。</summary>
public partial class KingsoftCloudNodeEditWindow : NodeEditWindowBase
{
    private readonly string _initialName;

    public KingsoftCloudNodeEditWindow(Node node, List<Node> nodes, List<Credential> credentials, List<Tunnel> tunnels, StorageService storage, bool isExistingNode = true)
        : base(node, nodes, credentials, tunnels, storage, isExistingNode)
    {
        InitializeComponent();
        NameBox.Text = node.Name ?? "金山云";
        _initialName = NameBox.Text ?? "";
        RegisterClosing();
    }

    protected override bool IsDirty() => (NameBox.Text ?? "") != _initialName;

    protected override bool SaveToNode()
    {
        var name = NameBox.Text?.Trim() ?? "金山云";
        _node.Name = name;
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
