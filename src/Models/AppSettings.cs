using YamlDotNet.Serialization;

namespace xOpenTerm.Models;

/// <summary>应用界面字体、窗口与布局等设置</summary>
public class AppSettings
{
    [YamlMember(Alias = "interfaceFontFamily")]
    public string InterfaceFontFamily { get; set; } = "Microsoft YaHei UI";

    [YamlMember(Alias = "interfaceFontSize")]
    public double InterfaceFontSize { get; set; } = 14;

    [YamlMember(Alias = "windowWidth")]
    public double WindowWidth { get; set; } = 1000;

    [YamlMember(Alias = "windowHeight")]
    public double WindowHeight { get; set; } = 600;

    [YamlMember(Alias = "windowLeft")]
    public double? WindowLeft { get; set; }

    [YamlMember(Alias = "windowTop")]
    public double? WindowTop { get; set; }

    [YamlMember(Alias = "windowState")]
    public int WindowState { get; set; } // 0 Normal, 1 Minimized, 2 Maximized

    [YamlMember(Alias = "leftPanelWidth")]
    public double LeftPanelWidth { get; set; } = 260;
}
