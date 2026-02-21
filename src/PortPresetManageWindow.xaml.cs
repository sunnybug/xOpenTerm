using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>端口预设管理窗口：允许用户添加、编辑、删除端口预设方案</summary>
public partial class PortPresetManageWindow : Window
{
    private readonly AppSettings _settings;
    private readonly IStorageService _storage;
    private readonly Action _refreshCallback;
    private PortPreset? _selectedPreset;

    public PortPresetManageWindow(AppSettings settings, IStorageService storage, Action refreshCallback)
    {
        InitializeComponent();
        _settings = settings;
        _storage = storage;
        _refreshCallback = refreshCallback;
        LoadPresets();

        // 监听回车键保存
        NameBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) SaveBtn_Click(s, e); };
        PortsBox.KeyDown += (s, e) => { if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) SaveBtn_Click(s, e); };
    }

    /// <summary>加载预设到 DataGrid</summary>
    private void LoadPresets()
    {
        PresetDataGrid.ItemsSource = _settings.PortScanSettings.PortPresets;
    }

    /// <summary>添加新预设</summary>
    private void AddBtn_Click(object sender, RoutedEventArgs e)
    {
        ClearEditForm();
        NameBox.Focus();
    }

    /// <summary>删除选中预设</summary>
    private void DeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPreset == null)
        {
            MessageBox.Show("请先选择要删除的预设。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            $"确定删除预设 \"{_selectedPreset.Name}\"？",
            "xOpenTerm",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        _settings.PortScanSettings.PortPresets.Remove(_selectedPreset);
        _storage.SaveAppSettings(_settings);
        LoadPresets();
        ClearEditForm();

        // 通知主窗口刷新
        _refreshCallback?.Invoke();
    }

    /// <summary>保存预设</summary>
    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim() ?? "";
        var ports = PortsBox.Text?.Trim() ?? "";

        // 验证输入
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("请输入预设名称。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
            NameBox.Focus();
            return;
        }

        if (name.Length > 50)
        {
            MessageBox.Show("预设名称不能超过 50 个字符。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
            NameBox.Focus();
            return;
        }

        if (string.IsNullOrEmpty(ports))
        {
            MessageBox.Show("请输入端口列表。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
            PortsBox.Focus();
            return;
        }

        // 验证端口格式
        try
        {
            PortScanHelper.ParsePortInput(ports);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"端口格式错误：{ex.Message}", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
            PortsBox.Focus();
            return;
        }

        // 检查名称重复（新增时）
        if (_selectedPreset == null)
        {
            var exists = _settings.PortScanSettings.PortPresets
                .Any(p => p.Name == name);
            if (exists)
            {
                MessageBox.Show($"预设名称 \"{name}\" 已存在，请使用其他名称。", "xOpenTerm",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                NameBox.Focus();
                return;
            }

            // 添加新预设
            var newPreset = new PortPreset { Name = name, Ports = ports };
            _settings.PortScanSettings.PortPresets.Add(newPreset);
        }
        else
        {
            // 更新现有预设
            // 检查名称是否与其他预设重复（排除自己）
            var exists = _settings.PortScanSettings.PortPresets
                .Any(p => p.Name == name && p != _selectedPreset);
            if (exists)
            {
                MessageBox.Show($"预设名称 \"{name}\" 已存在，请使用其他名称。", "xOpenTerm",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                NameBox.Focus();
                return;
            }

            _selectedPreset.Name = name;
            _selectedPreset.Ports = ports;
        }

        // 保存并刷新
        _storage.SaveAppSettings(_settings);
        LoadPresets();
        ClearEditForm();

        // 通知主窗口刷新
        _refreshCallback?.Invoke();
    }

    /// <summary>选中预设变更</summary>
    private void PresetDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedPreset = PresetDataGrid.SelectedItem as PortPreset;
        if (_selectedPreset != null)
        {
            NameBox.Text = _selectedPreset.Name;
            PortsBox.Text = _selectedPreset.Ports;
        }
    }

    /// <summary>清空编辑表单</summary>
    private void ClearEditForm()
    {
        _selectedPreset = null;
        NameBox.Text = "";
        PortsBox.Text = "";
        PresetDataGrid.SelectedItem = null;
    }

    /// <summary>关闭窗口</summary>
    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
