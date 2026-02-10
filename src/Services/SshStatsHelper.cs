using System.Globalization;
using System.Text.RegularExpressions;

namespace xOpenTerm.Services;

/// <summary>SSH 状态栏：远程执行统计命令与解析输出（Linux /proc、ss）。</summary>
public static class SshStatsHelper
{
    /// <summary>在 Linux 上采集 CPU/内存/网络/TCP/UDP 的单条命令（约 1 秒，含 sleep 1）。</summary>
    public const string StatsCommand = "S1=$(grep '^cpu ' /proc/stat); echo \"CPU1:$S1\"; echo \"MEM:$(grep -E 'MemTotal|MemAvailable' /proc/meminfo)\"; N1=$(awk 'NR>2{rx+=$2;tx+=$10}END{print rx+0,tx+0}' /proc/net/dev); echo \"NET1:$N1\"; sleep 1; S2=$(grep '^cpu ' /proc/stat); echo \"CPU2:$S2\"; N2=$(awk 'NR>2{rx+=$2;tx+=$10}END{print rx+0,tx+0}' /proc/net/dev); echo \"NET2:$N2\"; echo \"TCP:$(ss -t -a 2>/dev/null | tail -n +2 | wc -l)\"; echo \"UDP:$(ss -u -a 2>/dev/null | tail -n +2 | wc -l)\"";

    /// <summary>解析远程命令输出，返回 CPU%、内存%、下行/上行字节每秒、TCP 数、UDP 数。解析失败则对应项为 null。</summary>
    public static (double? CpuPercent, double? MemPercent, double? RxBps, double? TxBps, int? TcpCount, int? UdpCount) ParseStatsOutput(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return (null, null, null, null, null, null);

        double? cpu = null;
        double? mem = null;
        double? rxBps = null;
        double? txBps = null;
        int? tcp = null;
        int? udp = null;

        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        string? cpu1 = null, cpu2 = null;
        string? memLine = null;
        string? net1 = null, net2 = null;

        foreach (var line in lines)
        {
            if (line.StartsWith("CPU1:", StringComparison.Ordinal))
                cpu1 = line.Substring(5).Trim();
            else if (line.StartsWith("CPU2:", StringComparison.Ordinal))
                cpu2 = line.Substring(5).Trim();
            else if (line.StartsWith("MEM:", StringComparison.Ordinal))
                memLine = line.Substring(4).Trim();
            else if (line.StartsWith("NET1:", StringComparison.Ordinal))
                net1 = line.Substring(5).Trim();
            else if (line.StartsWith("NET2:", StringComparison.Ordinal))
                net2 = line.Substring(5).Trim();
            else if (line.StartsWith("TCP:", StringComparison.Ordinal) && int.TryParse(line.Substring(4).Trim(), out var t))
                tcp = t;
            else if (line.StartsWith("UDP:", StringComparison.Ordinal) && int.TryParse(line.Substring(4).Trim(), out var u))
                udp = u;
        }

        // CPU: 两行 cpu 行，格式 "cpu  user nice sys idle iowait ..."
        if (!string.IsNullOrEmpty(cpu1) && !string.IsNullOrEmpty(cpu2))
        {
            if (ParseCpuLine(cpu1, out var u1, out var t1) && ParseCpuLine(cpu2, out var u2, out var t2))
            {
                var totalDelta = (t2 - t1);
                if (totalDelta > 0)
                    cpu = 100.0 * (u2 - u1) / totalDelta;
            }
        }

        // MEM: "MemTotal: 123456 kB MemAvailable: 65432 kB" 或多行
        if (!string.IsNullOrEmpty(memLine))
        {
            var totalKb = MatchNumberAfter(memLine, "MemTotal");
            var availKb = MatchNumberAfter(memLine, "MemAvailable");
            if (totalKb.HasValue && availKb.HasValue && totalKb.Value > 0)
                mem = 100.0 * (1.0 - (double)availKb.Value / totalKb.Value);
        }

        // NET: "rx_total tx_total" 两行，间隔 1 秒，差值为 bytes/s
        if (!string.IsNullOrEmpty(net1) && !string.IsNullOrEmpty(net2))
        {
            var parts1 = net1.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var parts2 = net2.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts1.Length >= 2 && parts2.Length >= 2 &&
                long.TryParse(parts1[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r1) &&
                long.TryParse(parts1[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var t1) &&
                long.TryParse(parts2[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r2) &&
                long.TryParse(parts2[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var t2))
            {
                rxBps = (double)(r2 - r1);
                txBps = (double)(t2 - t1);
                if (rxBps < 0) rxBps = 0;
                if (txBps < 0) txBps = 0;
            }
        }

        return (cpu, mem, rxBps, txBps, tcp, udp);
    }

    private static bool ParseCpuLine(string line, out double used, out double total)
    {
        used = 0;
        total = 0;
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        // cpu user nice sys idle iowait irq softirq steal guest guest_nice
        if (parts.Length < 5) return false;
        var user = ParseLong(parts[1]);
        var nice = ParseLong(parts[2]);
        var sys = ParseLong(parts[3]);
        var idle = ParseLong(parts[4]);
        var iowait = parts.Length > 5 ? ParseLong(parts[5]) : 0;
        var irq = parts.Length > 6 ? ParseLong(parts[6]) : 0;
        var softirq = parts.Length > 7 ? ParseLong(parts[7]) : 0;
        var steal = parts.Length > 8 ? ParseLong(parts[8]) : 0;
        used = user + nice + sys + irq + softirq + steal;
        total = used + idle + iowait;
        return true;
    }

    private static long ParseLong(string s)
    {
        return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static long? MatchNumberAfter(string text, string key)
    {
        var m = Regex.Match(text, key + @"\s*:\s*(\d+)", RegexOptions.IgnoreCase);
        return m.Success && long.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
    }
}
