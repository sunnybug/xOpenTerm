using System.Globalization;
using System.Text.RegularExpressions;

namespace xOpenTerm.Services;

/// <summary>RDP 状态栏：远程执行统计命令与解析输出（Windows wmic、typeperf）。</summary>
public static class RdpStatsHelper
{
    /// <summary>在 Windows 上采集 CPU/内存/网络/TCP/UDP 的命令（约 1 秒）。</summary>
    public const string StatsCommand = "wmic cpu get LoadPercentage /value && wmic OS get FreePhysicalMemory,TotalVisibleMemorySize /value && netstat -an | find /c \"LISTENING\" && netstat -an | find /c \"UDP\"";

    /// <summary>解析远程命令输出，返回 CPU%、内存%、TCP 数、UDP 数。解析失败则对应项为 null。</summary>
    public static (double? CpuPercent, double? MemPercent, int? TcpCount, int? UdpCount) ParseStatsOutput(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return (null, null, null, null);

        double? cpu = null;
        double? mem = null;
        int? tcp = null;
        int? udp = null;

        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("LoadPercentage=", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(trimmed.Substring(14).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var load))
                    cpu = load;
            }
            else if (trimmed.StartsWith("FreePhysicalMemory=", StringComparison.OrdinalIgnoreCase))
            {
                if (ulong.TryParse(trimmed.Substring(21).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var free))
                {
                    // 查找 TotalVisibleMemorySize
                    foreach (var l in lines)
                    {
                        var tTrimmed = l.Trim();
                        if (tTrimmed.StartsWith("TotalVisibleMemorySize=", StringComparison.OrdinalIgnoreCase))
                        {
                            if (ulong.TryParse(tTrimmed.Substring(24).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var total))
                            {
                                if (total > 0)
                                    mem = 100.0 * (1.0 - (double)free / total);
                            }
                            break;
                        }
                    }
                }
            }
            else if (int.TryParse(trimmed, out var count))
            {
                if (tcp == null)
                    tcp = count;
                else if (udp == null)
                    udp = count;
            }
        }

        return (cpu, mem, tcp, udp);
    }
}
