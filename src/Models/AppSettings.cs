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

    /// <summary>上次关闭时服务器树中展开的分组节点 ID 列表，用于下次启动恢复</summary>
    [YamlMember(Alias = "serverTreeExpandedIds")]
    public List<string>? ServerTreeExpandedIds { get; set; }

    /// <summary>上次关闭时服务器树选中的节点 ID 列表（多选），用于下次启动恢复</summary>
    [YamlMember(Alias = "serverTreeSelectedIds")]
    public List<string>? ServerTreeSelectedIds { get; set; }

    /// <summary>是否已询问过并设置主密码（启动时若为 false 则弹出设置主密码；为 true 则弹出输入主密码）</summary>
    [YamlMember(Alias = "masterPasswordAsked")]
    public bool MasterPasswordAsked { get; set; }

    /// <summary>主密码派生密钥时使用的盐（Base64），仅当 MasterPasswordAsked 为 true 时有效</summary>
    [YamlMember(Alias = "masterPasswordSalt")]
    public string? MasterPasswordSalt { get; set; }

    /// <summary>主密码验证码（密钥的 SHA256 的 Base64），用于启动时校验主密码是否正确</summary>
    [YamlMember(Alias = "masterPasswordVerifier")]
    public string? MasterPasswordVerifier { get; set; }

    /// <summary>用户是否选择「不再提醒」主密码：为 true 时启动不再弹出主密码设置/输入框，使用原有固定密钥加解密。</summary>
    [YamlMember(Alias = "masterPasswordSkipped")]
    public bool MasterPasswordSkipped { get; set; }
}
