using System.Windows;
using System.Windows.Controls;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

public partial class CredentialEditWindow : Window
{
    private readonly Credential _cred;
    private readonly IList<Credential> _all;
    private readonly INodeEditContext _context;
    private readonly string _initialName;
    private readonly string _initialUsername;
    private readonly int _initialAuthIndex;
    private readonly string _initialPassword;
    private readonly string _initialKeyPath;
    private bool _closingConfirmed;

    public CredentialEditWindow(Credential cred, IList<Credential> all, INodeEditContext context, bool isNew)
    {
        InitializeComponent();
        _cred = cred;
        _all = all;
        _context = context;
        Title = isNew ? "新增凭证" : "编辑凭证";
        AuthCombo.Items.Add("密码");
        AuthCombo.Items.Add("私钥");
        AuthCombo.Items.Add("SSH Agent");
        AuthCombo.SelectedIndex = _cred.AuthType switch { AuthType.key => 1, AuthType.agent => 2, _ => 0 };
        NameBox.Text = _cred.Name;
        UsernameBox.Text = _cred.Username;
        PasswordBox.Password = _cred.Password ?? "";
        KeyPathBox.Text = _cred.KeyPath ?? "";
        _initialName = NameBox.Text ?? "";
        _initialUsername = UsernameBox.Text ?? "";
        _initialAuthIndex = AuthCombo.SelectedIndex;
        _initialPassword = PasswordBox.Password ?? "";
        _initialKeyPath = KeyPathBox.Text ?? "";
        AuthCombo.SelectionChanged += (_, _) => UpdateAuthVisibility();
        UpdateAuthVisibility();
        Closing += CredentialEditWindow_Closing;
    }

    private bool IsDirty()
    {
        return (NameBox.Text ?? "") != _initialName
            || (UsernameBox.Text ?? "") != _initialUsername
            || AuthCombo.SelectedIndex != _initialAuthIndex
            || PasswordBox.Password != _initialPassword
            || (KeyPathBox.Text ?? "") != _initialKeyPath;
    }

    private void CredentialEditWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closingConfirmed) return;
        if (IsDirty() && MessageBox.Show("是否放弃修改？", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            e.Cancel = true;
    }

    private void UpdateAuthVisibility()
    {
        var idx = AuthCombo.SelectedIndex;
        var isAgent = idx == 2;
        var isKey = idx == 1;
        PasswordRow.Visibility = (isKey || isAgent) ? Visibility.Collapsed : Visibility.Visible;
        KeyRow.Visibility = isKey ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        _cred.Name = NameBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(_cred.Name)) { MessageBox.Show("请输入名称。", "xOpenTerm"); return; }
        _cred.Username = UsernameBox.Text?.Trim() ?? "";
        var authIdx = AuthCombo.SelectedIndex;
        _cred.AuthType = authIdx switch { 1 => AuthType.key, 2 => AuthType.agent, _ => AuthType.password };
        _cred.Password = authIdx == 0 ? PasswordBox.Password : null;
        _cred.KeyPath = authIdx == 1 ? KeyPathBox.Text?.Trim() : null;
        var idx = -1;
        for (var i = 0; i < _all.Count; i++)
        {
            if (_all[i].Id == _cred.Id) { idx = i; break; }
        }
        if (idx >= 0) _all[idx] = _cred;
        else _all.Add(_cred);
        _context.SaveCredentials();
        _closingConfirmed = true;
        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        if (IsDirty() && MessageBox.Show("是否放弃修改？", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _closingConfirmed = true;
        DialogResult = false;
        Close();
    }
}
