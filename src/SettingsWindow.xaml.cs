using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using xOpenTerm2.Controls;
using xOpenTerm2.Models;
using xOpenTerm2.Services;

namespace xOpenTerm2;

public partial class SettingsWindow : Window
{
    private readonly StorageService _storage = new();
    private readonly AppSettings _settings;

    public SettingsWindow(Window owner)
    {
        Owner = owner;
        InitializeComponent();
        _settings = _storage.LoadAppSettings();

        var fontSizes = new[] { 10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 24 };
        foreach (var size in fontSizes)
            InterfaceSizeCombo.Items.Add(size);

        var commonFonts = new[]
        {
            "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "SimSun", "SimHei",
            "Consolas", "Courier New", "Cascadia Code", "Cascadia Mono", "Source Code Pro"
        };
        foreach (var f in commonFonts)
            InterfaceFontCombo.Items.Add(f);
        foreach (var ff in Fonts.SystemFontFamilies.OrderBy(x => x.Source))
        {
            var name = ff.Source;
            if (!InterfaceFontCombo.Items.Cast<object>().Any(x => string.Equals(x?.ToString(), name, StringComparison.OrdinalIgnoreCase)))
                InterfaceFontCombo.Items.Add(name);
        }

        InterfaceFontCombo.Text = _settings.InterfaceFontFamily;
        InterfaceSizeCombo.Text = _settings.InterfaceFontSize.ToString("0");

        InterfaceFontCombo.SelectionChanged += FontCombo_Changed;
        InterfaceFontCombo.LostFocus += FontCombo_Changed;
        InterfaceSizeCombo.SelectionChanged += FontCombo_Changed;

        UpdatePreview();
    }

    private void FontCombo_Changed(object sender, EventArgs e)
    {
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var uiFamily = InterfaceFontCombo.Text?.Trim() ?? "Microsoft YaHei UI";
        var uiSize = 14.0;
        double.TryParse(InterfaceSizeCombo.Text, out uiSize);
        if (uiSize < 8 || uiSize > 36) uiSize = 14;

        try
        {
            InterfacePreview.FontFamily = new FontFamily(uiFamily);
            InterfacePreview.FontSize = uiSize;
        }
        catch
        {
            InterfacePreview.FontFamily = new FontFamily("Microsoft YaHei UI");
            InterfacePreview.FontSize = uiSize;
        }
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(InterfaceSizeCombo.Text, out var uiSize) || uiSize < 8 || uiSize > 36)
            uiSize = 14;

        _settings.InterfaceFontFamily = InterfaceFontCombo.Text?.Trim() ?? "Microsoft YaHei UI";
        _settings.InterfaceFontSize = uiSize;

        _storage.SaveAppSettings(_settings);
        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void PuttyFontBtn_Click(object sender, RoutedEventArgs e)
    {
        var puttyPath = SshPuttyHostControl.DefaultPuttyPath;
        if (string.IsNullOrWhiteSpace(puttyPath) || !File.Exists(puttyPath))
        {
            MessageBox.Show("未找到 PuTTY 程序，请确保已安装 PuTTY 或 PuTTY NG。", "xOpenTerm2");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = puttyPath,
                Arguments = "",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show("无法启动 PuTTY：" + ex.Message, "xOpenTerm2");
        }
    }
}
