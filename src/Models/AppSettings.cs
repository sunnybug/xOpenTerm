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

    /// <summary>MobaXterm 导入界面上次使用的 MobaXterm.ini 路径</summary>
    [YamlMember(Alias = "lastMobaXtermIniPath")]
    public string? LastMobaXtermIniPath { get; set; }

    /// <summary>MobaXterm 导入界面上次使用的密码文件路径</summary>
    [YamlMember(Alias = "lastMobaXtermPasswordPath")]
    public string? LastMobaXtermPasswordPath { get; set; }

    /// <summary>端口扫描配置</summary>
    [YamlMember(Alias = "portScanSettings")]
    public PortScanSettings PortScanSettings { get; set; } = new();
}

/// <summary>端口扫描设置</summary>
public class PortScanSettings
{
    /// <summary>默认并发节点数（1-1000，默认50）</summary>
    [YamlMember(Alias = "defaultConcurrency")]
    public int DefaultConcurrency { get; set; } = 50;

    /// <summary>默认超时时间（秒，1-10，默认2）</summary>
    [YamlMember(Alias = "defaultTimeoutSeconds")]
    public int DefaultTimeoutSeconds { get; set; } = 2;

    /// <summary>默认扫描模式（true=深度，false=快速）</summary>
    [YamlMember(Alias = "defaultUseDeepScan")]
    public bool DefaultUseDeepScan { get; set; } = true;

    /// <summary>端口范围历史记录（最多保存20条）</summary>
    [YamlMember(Alias = "portHistory")]
    public List<string> PortHistory { get; set; } = new()
    {
        "22,80,443,3306,3389,8080",  // 默认历史
        "21,22,23,25,53,80,110,143,443,445,3306,3389,5432,6379,7001,8080,8443,9200,27017"
    };

    /// <summary>端口预设方案列表</summary>
    [YamlMember(Alias = "portPresets")]
    public List<PortPreset> PortPresets { get; set; } = new();

    /// <summary>上次选择的端口预设名称（用于恢复上次选择）</summary>
    [YamlMember(Alias = "lastSelectedPreset")]
    public string? LastSelectedPreset { get; set; }
}

/// <summary>端口预设方案</summary>
public class PortPreset
{
    /// <summary>预设名称（显示在下拉框中）</summary>
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>端口列表（逗号分隔的字符串）</summary>
    [YamlMember(Alias = "ports")]
    public string Ports { get; set; } = string.Empty;
}
