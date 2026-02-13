using System.Windows;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>分组节点编辑窗口，仅编辑名称。</summary>
public partial class GroupNodeEditWindow : NodeEditWindowBase
{
    private readonly string _initialName;

    public GroupNodeEditWindow(Node node, INodeEditContext context, bool isExistingNode = true)
        : base(node, context, isExistingNode)
    {
        InitializeComponent();
        NameBox.Text = node.Name ?? "";
        _initialName = NameBox.Text ?? "";
        RegisterClosing();
    }

    protected override bool IsDirty() => (NameBox.Text ?? "") != _initialName;

    protected override bool SaveToNode()
    {
        var name = NameBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show(this, "请输入名称。", "xOpenTerm");
            return false;
        }
        _node.Name = name;
        _node.Config = null;
        return true;
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveToNode()) return;
        RecordInputHistory();
        ConfirmCloseAndSave();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        if (IsDirty() && MessageBox.Show(this, "是否放弃修改？", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        ConfirmCloseAndCancel();
    }
}
