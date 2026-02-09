using System.Windows;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>腾讯云组节点编辑窗口：组名称与 API 密钥。</summary>
public partial class TencentCloudNodeEditWindow : NodeEditWindowBase
{
    private readonly string _initialName;
    private readonly string _initialSecretId;
    private readonly string _initialSecretKey;

    public TencentCloudNodeEditWindow(Node node, List<Node> nodes, List<Credential> credentials, List<Tunnel> tunnels, StorageService storage, bool isExistingNode = true)
        : base(node, nodes, credentials, tunnels, storage, isExistingNode)
    {
        InitializeComponent();
        NameBox.Text = node.Name ?? "腾讯云";
        var cfg = node.Config;
        SecretIdBox.Text = cfg?.TencentSecretId ?? "";
        SecretKeyBox.Password = cfg?.TencentSecretKey ?? "";
        _initialName = NameBox.Text ?? "";
        _initialSecretId = SecretIdBox.Text ?? "";
        _initialSecretKey = SecretKeyBox.Password ?? "";
        RegisterClosing();
    }

    protected override bool IsDirty() =>
        (NameBox.Text ?? "") != _initialName ||
        (SecretIdBox.Text ?? "") != _initialSecretId ||
        SecretKeyBox.Password != _initialSecretKey;

    protected override bool SaveToNode()
    {
        var name = NameBox.Text?.Trim() ?? "腾讯云";
        var secretId = SecretIdBox.Text?.Trim() ?? "";
        var secretKey = SecretKeyBox.Password ?? "";
        if (string.IsNullOrWhiteSpace(secretId))
        {
            MessageBox.Show(this, "请输入 SecretId。", "xOpenTerm");
            SecretIdBox.Focus();
            return false;
        }
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            MessageBox.Show(this, "请输入 SecretKey。", "xOpenTerm");
            SecretKeyBox.Focus();
            return false;
        }
        _node.Name = name;
        _node.Config ??= new ConnectionConfig();
        _node.Config.TencentSecretId = secretId;
        _node.Config.TencentSecretKey = secretKey;
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
