using System.Linq;
using System.Windows;
using System.Windows.Input;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>节点编辑窗口抽象基类，封装关闭确认、保存/取消、SavedNode 等公共逻辑。</summary>
public abstract class NodeEditWindowBase : Window
{
    protected readonly Node _node;
    protected readonly List<Node> _nodes;
    protected readonly List<Credential> _credentials;
    protected readonly List<Tunnel> _tunnels;
    protected readonly StorageService _storage;
    protected readonly bool _isExistingNode;
    protected bool _closingConfirmed;

    public Node? SavedNode { get; protected set; }

    protected NodeEditWindowBase(Node node, List<Node> nodes, List<Credential> credentials, List<Tunnel> tunnels, StorageService storage, bool isExistingNode)
    {
        _node = node;
        _nodes = nodes;
        _credentials = credentials;
        _tunnels = tunnels;
        _storage = storage;
        _isExistingNode = isExistingNode;
    }

    /// <summary>子类在 InitializeComponent 及控件绑定完成后调用，注册关闭前未保存提示及 ESC 键行为（无修改直接关，有修改则提示是否放弃）。</summary>
    protected void RegisterClosing()
    {
        Closing += (_, e) =>
        {
            if (_closingConfirmed) return;
            if (IsDirty() && MessageBox.Show(this, "是否放弃修改？", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                e.Cancel = true;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            if (!IsDirty())
            {
                ConfirmCloseAndCancel();
                return;
            }
            if (MessageBox.Show(this, "是否放弃修改？", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            ConfirmCloseAndCancel();
        };
    }

    /// <summary>子类实现：当前表单是否有未保存修改。</summary>
    protected abstract bool IsDirty();

    /// <summary>子类实现：将表单数据写入 _node（可在此做校验，失败时抛或 MessageBox 后 return，由子类不调用 ConfirmCloseAndSave）。</summary>
    protected abstract bool SaveToNode();

    protected void ConfirmCloseAndSave()
    {
        _closingConfirmed = true;
        SavedNode = _node;
        DialogResult = true;
        Close();
    }

    protected void ConfirmCloseAndCancel()
    {
        _closingConfirmed = true;
        DialogResult = false;
        Close();
    }

    /// <summary>供 SSH 等子类绑定登录凭证下拉框。</summary>
    protected void RefreshCredentialCombo(System.Windows.Controls.ComboBox combo)
    {
        combo.ItemsSource = null;
        combo.ItemsSource = _credentials.OrderBy(c => c.AuthType).ThenBy(c => c.Name).ToList();
    }

    /// <summary>供 SSH 子类绑定跳板机列表。</summary>
    protected void RefreshTunnelList(System.Windows.Controls.ListBox listBox, List<string>? initialSelectedIds = null)
    {
        var sel = initialSelectedIds ?? listBox.SelectedItems.Cast<Tunnel>().Select(t => t.Id).ToList();
        var sorted = _tunnels.OrderBy(t => t.AuthType).ThenBy(t => t.Name).ToList();
        listBox.ItemsSource = null;
        listBox.ItemsSource = sorted;
        listBox.DisplayMemberPath = "Name";
        foreach (var t in sorted.Where(t => sel.Contains(t.Id)))
            listBox.SelectedItems.Add(t);
    }
}
