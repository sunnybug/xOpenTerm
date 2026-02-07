using System.Linq;
using System.Windows;
using System.Windows.Controls;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>隧道（跳板机）管理窗口</summary>
public partial class TunnelManagerWindow : Window
{
    private readonly StorageService _storage = new();
    private List<Tunnel> _tunnels = new();
    private List<Credential> _credentials = new();

    public TunnelManagerWindow(Window? owner)
    {
        InitializeComponent();
        Owner = owner;
        _credentials = _storage.LoadCredentials();
        LoadTunnels();
    }

    private void LoadTunnels()
    {
        _tunnels = _storage.LoadTunnels().OrderBy(t => t.AuthType).ThenBy(t => t.Name).ToList();
        TunnelList.Items.Clear();
        foreach (var t in _tunnels)
            TunnelList.Items.Add($"{t.Name} — {t.Host}:{t.Port ?? 22} ({t.Username})");
    }

    private void TunnelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasSel = TunnelList.SelectedIndex >= 0 && TunnelList.SelectedIndex < _tunnels.Count;
        EditBtn.IsEnabled = hasSel;
        DelBtn.IsEnabled = hasSel;
    }

    private void AddBtn_Click(object sender, RoutedEventArgs e)
    {
        var t = new Tunnel { Id = Guid.NewGuid().ToString(), Name = "新跳板机", Port = 22, AuthType = AuthType.password };
        var win = new TunnelEditWindow(t, _tunnels, _credentials, _storage, isNew: true) { Owner = this };
        if (win.ShowDialog() == true)
            LoadTunnels();
    }

    private void TunnelList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (TunnelList.SelectedIndex >= 0 && TunnelList.SelectedIndex < _tunnels.Count)
            OpenEdit(_tunnels[TunnelList.SelectedIndex]);
    }

    private void EditBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TunnelList.SelectedIndex >= 0 && TunnelList.SelectedIndex < _tunnels.Count)
            OpenEdit(_tunnels[TunnelList.SelectedIndex]);
    }

    private void OpenEdit(Tunnel t)
    {
        _credentials = _storage.LoadCredentials();
        var win = new TunnelEditWindow(t, _tunnels, _credentials, _storage, isNew: false) { Owner = this };
        if (win.ShowDialog() == true)
            LoadTunnels();
    }

    private void DelBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TunnelList.SelectedIndex < 0 || TunnelList.SelectedIndex >= _tunnels.Count) return;
        var t = _tunnels[TunnelList.SelectedIndex];
        if (MessageBox.Show(this, $"确定删除跳板机「{t.Name}」？", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _tunnels.RemoveAll(x => x.Id == t.Id);
        _storage.SaveTunnels(_tunnels);
        LoadTunnels();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}
