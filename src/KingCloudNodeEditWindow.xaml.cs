using System.Windows;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>金山云组节点编辑窗口：组名称与 AccessKey。</summary>
public partial class KingCloudNodeEditWindow : NodeEditWindowBase
{
    private readonly string _initialName;
    private readonly string _initialAccessKeyId;
    private readonly string _initialAccessKeySecret;

    public KingCloudNodeEditWindow(Node node, List<Node> nodes, List<Credential> credentials, List<Tunnel> tunnels, StorageService storage, bool isExistingNode = true)
        : base(node, nodes, credentials, tunnels, storage, isExistingNode)
    {
        InitializeComponent();
        NameBox.Text = node.Name ?? "金山云";
        var cfg = node.Config;
        AccessKeyIdBox.Text = cfg?.KingAccessKeyId ?? "";
        AccessKeySecretBox.Password = cfg?.KingAccessKeySecret ?? "";
        _initialName = NameBox.Text ?? "";
        _initialAccessKeyId = AccessKeyIdBox.Text ?? "";
        _initialAccessKeySecret = AccessKeySecretBox.Password ?? "";
        RegisterClosing();
    }

    protected override bool IsDirty() =>
        (NameBox.Text ?? "") != _initialName ||
        (AccessKeyIdBox.Text ?? "") != _initialAccessKeyId ||
        AccessKeySecretBox.Password != _initialAccessKeySecret;

    protected override bool SaveToNode()
    {
        var name = NameBox.Text?.Trim() ?? "金山云";
        var accessKeyId = AccessKeyIdBox.Text?.Trim() ?? "";
        var accessKeySecret = AccessKeySecretBox.Password ?? "";
        if (string.IsNullOrWhiteSpace(accessKeyId))
        {
            MessageBox.Show(this, "请输入 AccessKeyId。", "xOpenTerm");
            AccessKeyIdBox.Focus();
            return false;
        }
        if (string.IsNullOrWhiteSpace(accessKeySecret))
        {
            MessageBox.Show(this, "请输入 AccessKeySecret。", "xOpenTerm");
            AccessKeySecretBox.Focus();
            return false;
        }
        _node.Name = name;
        _node.Config ??= new ConnectionConfig();
        _node.Config.KingAccessKeyId = accessKeyId;
        _node.Config.KingAccessKeySecret = accessKeySecret;
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
