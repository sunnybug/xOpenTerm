using System.Globalization;
using System.Text.RegularExpressions;

namespace xOpenTerm.Services;

/// <summary>RDP 状态栏：远程执行统计命令与解析输出（Windows wmic、typeperf）。</summary>
public static class RdpStatsHelper
{
    /// <summary>在 Windows 上采集 CPU/内存/网络/TCP/UDP 的命令（约 1 秒）。</summary>
    public const string StatsCommand = "wmic cpu get LoadPercentage /value && wmic OS get FreePhysicalMemory,TotalVisibleMemorySize /value && netstat -an | find /c \"LISTENING\" && netstat -an | find /c \"UDP\"";

    /// <summary>在 Windows 上采集各盘符占用率的命令（DriveType=3 为本地磁盘）。输出格式依赖 wmic 列顺序。</summary>
    public const string DiskStatsCommand = "wmic logicaldisk where DriveType=3 get DeviceID,Size,FreeSpace /format:csv";

    /// <summary>解析 wmic logicaldisk /format:csv 输出，返回 (盘符, 占用率%) 列表。解析失败返回空列表。</summary>
    public static IReadOnlyList<(string DiskName, double UsePercent)> ParseDiskStatsOutput(string? output)
    {
        var list = new List<(string DiskName, double UsePercent)>();
        if (string.IsNullOrWhiteSpace(output)) return list;
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        // csv 格式首行为 Node,DeviceID,FreeSpace,Size
        if (lines.Length < 2) return list;
        var header = lines[0].Trim();
        var deviceIdIdx = -1;
        var sizeIdx = -1;
        var freeIdx = -1;
        var parts = header.Split(',');
        for (var i = 0; i < parts.Length; i++)
        {
            var p = parts[i].Trim();
            if (string.Equals(p, "DeviceID", StringComparison.OrdinalIgnoreCase)) deviceIdIdx = i;
            else if (string.Equals(p, "Size", StringComparison.OrdinalIgnoreCase)) sizeIdx = i;
            else if (string.Equals(p, "FreeSpace", StringComparison.OrdinalIgnoreCase)) freeIdx = i;
        }
        if (deviceIdIdx < 0 || sizeIdx < 0 || freeIdx < 0) return list;
        for (var i = 1; i < lines.Length; i++)
        {
            var cols = lines[i].Split(',');
            if (cols.Length <= Math.Max(deviceIdIdx, Math.Max(sizeIdx, freeIdx))) continue;
            if (!ulong.TryParse(cols[sizeIdx].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) || size == 0)
                continue;
            if (!ulong.TryParse(cols[freeIdx].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var free))
                continue;
            var deviceId = cols[deviceIdIdx].Trim();
            if (string.IsNullOrEmpty(deviceId)) continue;
            var used = size - free;
            var percent = 100.0 * (double)used / size;
            list.Add((deviceId, Math.Round(percent, 1)));
        }
        return list;
    }

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
