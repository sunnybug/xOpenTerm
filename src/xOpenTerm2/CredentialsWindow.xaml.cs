using System.Windows;
using System.Windows.Controls;
using xOpenTerm2.Models;
using xOpenTerm2.Services;

namespace xOpenTerm2;

/// <summary>登录凭证管理窗口：列表、新增、编辑、删除</summary>
public partial class CredentialsWindow : Window
{
    private readonly StorageService _storage = new();
    private List<Credential> _credentials = new();

    public CredentialsWindow(Window? owner)
    {
        InitializeComponent();
        Owner = owner;
        LoadCredentials();
    }

    private void LoadCredentials()
    {
        _credentials = _storage.LoadCredentials();
        CredList.Items.Clear();
        foreach (var c in _credentials)
            CredList.Items.Add($"{c.Name} ({c.Username}, {c.AuthType})");
    }

    private void AddBtn_Click(object sender, RoutedEventArgs e)
    {
        var cred = new Credential { Id = Guid.NewGuid().ToString(), Name = "新凭证", AuthType = AuthType.password };
        var win = new CredentialEditWindow(cred, _credentials, _storage, isNew: true);
        if (win.ShowDialog() == true)
        {
            _credentials = _storage.LoadCredentials();
            LoadCredentials();
        }
    }

    private void CredList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasSel = CredList.SelectedIndex >= 0 && CredList.SelectedIndex < _credentials.Count;
        EditBtn.IsEnabled = hasSel;
        DelBtn.IsEnabled = hasSel;
    }

    private void CredList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (CredList.SelectedIndex >= 0 && CredList.SelectedIndex < _credentials.Count)
            OpenEdit(_credentials[CredList.SelectedIndex]);
    }

    private void EditBtn_Click(object sender, RoutedEventArgs e)
    {
        if (CredList.SelectedIndex >= 0 && CredList.SelectedIndex < _credentials.Count)
            OpenEdit(_credentials[CredList.SelectedIndex]);
    }

    private void OpenEdit(Credential cred)
    {
        var win = new CredentialEditWindow(cred, _credentials, _storage, isNew: false);
        if (win.ShowDialog() == true)
            LoadCredentials();
    }

    private void DelBtn_Click(object sender, RoutedEventArgs e)
    {
        if (CredList.SelectedIndex < 0 || CredList.SelectedIndex >= _credentials.Count) return;
        var cred = _credentials[CredList.SelectedIndex];
        if (MessageBox.Show($"确定删除凭证「{cred.Name}」？", "xOpenTerm2", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _credentials.RemoveAll(c => c.Id == cred.Id);
        _storage.SaveCredentials(_credentials);
        LoadCredentials();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}
