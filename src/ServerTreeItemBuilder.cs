using System.Windows.Media;
using xOpenTerm.Models;

namespace xOpenTerm;

/// <summary>服务器树节点图标与颜色等 UI 辅助。</summary>
public static class ServerTreeItemBuilder
{
    public static string NodeIcon(Node n, bool isGroupExpanded = true)
    {
        return n.Type switch
        {
            NodeType.group => isGroupExpanded ? "📂" : "📁",
            NodeType.tencentCloudGroup => isGroupExpanded ? "☁️" : "☁️",
            NodeType.aliCloudGroup => isGroupExpanded ? "☁️" : "☁️",
            NodeType.kingsoftCloudGroup => isGroupExpanded ? "☁️" : "☁️",
            NodeType.ssh => "\u276F",  // ❯ 命令行提示符风格（不用 MDL2）
            NodeType.rdp => "🖥️",
            _ => "⌨"
        };
    }

    public static Brush NodeColor(Node n)
    {
        return n.Type switch
        {
            NodeType.group => Brushes.Gold,
            NodeType.tencentCloudGroup => new SolidColorBrush(Color.FromRgb(0x00, 0x96, 0xff)),
            NodeType.aliCloudGroup => new SolidColorBrush(Color.FromRgb(0xff, 0x6a, 0x00)),  // 阿里橙
            NodeType.kingsoftCloudGroup => new SolidColorBrush(Color.FromRgb(0x00, 0xbf, 0x9a)),  // 金山云绿
            NodeType.ssh => new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)),  // 深灰，偏黑白
            NodeType.rdp => new SolidColorBrush(Color.FromRgb(0xc0, 0x84, 0xfc)),
            _ => Brushes.LightGreen
        };
    }
}
