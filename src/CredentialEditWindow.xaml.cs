using System.Windows;
using System.Windows.Controls;
using xOpenTerm2.Models;
using xOpenTerm2.Services;

namespace xOpenTerm2;

public partial class CredentialEditWindow : Window
{
    private readonly Credential _cred;
    private readonly List<Credential> _all;
    private readonly StorageService _storage;

    public CredentialEditWindow(Credential cred, List<Credential> all, StorageService storage, bool isNew)
    {
        InitializeComponent();
        _cred = cred;
        _all = all;
        _storage = storage;
        Title = isNew ? "新增凭证" : "编辑凭证";
        AuthCombo.Items.Add("密码");
        AuthCombo.Items.Add("私钥");
        AuthCombo.Items.Add("SSH Agent");
        AuthCombo.SelectedIndex = _cred.AuthType switch { AuthType.key => 1, AuthType.agent => 2, _ => 0 };
        NameBox.Text = _cred.Name;
        UsernameBox.Text = _cred.Username;
        PasswordBox.Password = _cred.Password ?? "";
        KeyPathBox.Text = _cred.KeyPath ?? "";
        AuthCombo.SelectionChanged += (_, _) => UpdateAuthVisibility();
        UpdateAuthVisibility();
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
        if (string.IsNullOrEmpty(_cred.Name)) { MessageBox.Show("请输入名称。", "xOpenTerm2"); return; }
        _cred.Username = UsernameBox.Text?.Trim() ?? "";
        var authIdx = AuthCombo.SelectedIndex;
        _cred.AuthType = authIdx switch { 1 => AuthType.key, 2 => AuthType.agent, _ => AuthType.password };
        _cred.Password = authIdx == 0 ? PasswordBox.Password : null;
        _cred.KeyPath = authIdx == 1 ? KeyPathBox.Text?.Trim() : null;
        var list = new List<Credential>(_all);
        var idx = list.FindIndex(c => c.Id == _cred.Id);
        if (idx >= 0) list[idx] = _cred;
        else list.Add(_cred);
        _storage.SaveCredentials(list);
        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
