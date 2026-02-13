namespace xOpenTerm.Models;

/// <summary>RDP 连接显示与重定向选项（参考 mRemoteNG），用于内嵌 RDP 控件与 .rdp 生成。</summary>
public record RdpConnectionOptions
{
    /// <summary>使用控制台会话（/admin）。</summary>
    public bool UseConsoleSession { get; init; }

    /// <summary>重定向剪贴板。</summary>
    public bool RedirectClipboard { get; init; }

    /// <summary>智能缩放（SmartSizing）。</summary>
    public bool SmartSizing { get; init; }
}
