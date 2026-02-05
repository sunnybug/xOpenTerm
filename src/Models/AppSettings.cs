using YamlDotNet.Serialization;

namespace xOpenTerm2.Models;

/// <summary>应用界面字体等设置</summary>
public class AppSettings
{
    [YamlMember(Alias = "interfaceFontFamily")]
    public string InterfaceFontFamily { get; set; } = "Microsoft YaHei UI";

    [YamlMember(Alias = "interfaceFontSize")]
    public double InterfaceFontSize { get; set; } = 14;
}
