using System.Globalization;
using System.Text.RegularExpressions;

namespace xOpenTerm.Services;

/// <summary>SSH 状态栏：远程执行统计命令与解析输出（Linux /proc、ss）。</summary>
public static class SshStatsHelper
{
    /// <summary>在 Linux 上采集 CPU/内存/网络/TCP/UDP 的单条命令（约 1 秒，含 sleep 1）。</summary>
    public const string StatsCommand = "grep '^cpu ' /proc/stat; echo '---'; grep 'MemTotal' /proc/meminfo; echo '---'; grep 'MemFree' /proc/meminfo; echo '---'; awk 'NR>2{rx+=$2;tx+=$10}END{print rx+0,tx+0}' /proc/net/dev; echo '---'; sleep 1; grep '^cpu ' /proc/stat; echo '---'; awk 'NR>2{rx+=$2;tx+=$10}END{print rx+0,tx+0}' /proc/net/dev; echo '---'; netstat -t -a 2>/dev/null | wc -l; echo '---'; netstat -u -a 2>/dev/null | wc -l;";
    
    /// <summary>用于调试的简化命令，不包含 sleep，用于快速测试。</summary>
    public const string DebugCommand = "echo \"CPU:$(grep '^cpu ' /proc/stat)\"; echo \"MEM:$(grep -E 'MemTotal|MemFree' /proc/meminfo)\"; echo \"NET:$(awk 'NR>2{rx+=$2;tx+=$10}END{print rx+0,tx+0}' /proc/net/dev)\"; echo \"TCP:$(netstat -t -a 2>/dev/null | wc -l)\"; echo \"UDP:$(netstat -u -a 2>/dev/null | wc -l)\"; echo \"VERSION:$(cat /etc/centos-release 2>/dev/null || cat /etc/redhat-release 2>/dev/null || echo 'Unknown')\"";

    /// <summary>解析远程命令输出，返回 CPU%、内存%、下行/上行字节每秒、TCP 数、UDP 数。解析失败则对应项为 null。</summary>
    public static (double? CpuPercent, double? MemPercent, double? RxBps, double? TxBps, int? TcpCount, int? UdpCount) ParseStatsOutput(string? output)
    {
        ExceptionLog.WriteInfo($"[SshStatsHelper] 开始解析输出，长度: {(output?.Length ?? 0)}");
        if (!string.IsNullOrEmpty(output))
        {
            ExceptionLog.WriteInfo($"[SshStatsHelper] 输出内容: {output}");
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            ExceptionLog.WriteInfo("[SshStatsHelper] 输出为空，返回 null");
            return (null, null, null, null, null, null);
        }

        double? cpu = null;
        double? mem = null;
        double? rxBps = null;
        double? txBps = null;
        int? tcp = null;
        int? udp = null;

        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        string? cpu1 = null, cpu2 = null;
        string? memTotal = null, memFree = null;
        string? net1 = null, net2 = null;

        ExceptionLog.WriteInfo($"[SshStatsHelper] 解析到 {lines.Length} 行数据");

        // 使用分隔符 --- 来识别不同的数据段
        int sectionIndex = 0;
        foreach (var line in lines)
        {
            ExceptionLog.WriteInfo($"[SshStatsHelper] 处理行: {line}, 段索引: {sectionIndex}");
            if (line == "---")
            {
                sectionIndex++;
                continue;
            }

            switch (sectionIndex)
            {
                case 0: // CPU1
                    if (!string.IsNullOrEmpty(line))
                        cpu1 = line;
                    break;
                case 1: // MemTotal
                    if (!string.IsNullOrEmpty(line))
                        memTotal = line;
                    break;
                case 2: // MemFree
                    if (!string.IsNullOrEmpty(line))
                        memFree = line;
                    break;
                case 3: // NET1
                    if (!string.IsNullOrEmpty(line))
                        net1 = line;
                    break;
                case 4: // CPU2
                    if (!string.IsNullOrEmpty(line))
                        cpu2 = line;
                    break;
                case 5: // NET2
                    if (!string.IsNullOrEmpty(line))
                        net2 = line;
                    break;
                case 6: // TCP
                    if (!string.IsNullOrEmpty(line))
                    {
                        if (int.TryParse(line, out var t))
                            tcp = t;
                        else if (int.TryParse(System.Text.RegularExpressions.Regex.Match(line, "\\d+").Value, out t))
                            tcp = t;
                    }
                    break;
                case 7: // UDP
                    if (!string.IsNullOrEmpty(line))
                    {
                        if (int.TryParse(line, out var u))
                            udp = u;
                        else if (int.TryParse(System.Text.RegularExpressions.Regex.Match(line, "\\d+").Value, out u))
                            udp = u;
                    }
                    break;
            }
        }

        // CPU: 两行 cpu 行，格式 "cpu  user nice sys idle iowait ..."
        ExceptionLog.WriteInfo($"[SshStatsHelper] CPU1: {cpu1}, CPU2: {cpu2}");
        if (!string.IsNullOrEmpty(cpu1) && !string.IsNullOrEmpty(cpu2))
        {
            if (ParseCpuLine(cpu1, out var u1, out var t1) && ParseCpuLine(cpu2, out var u2, out var t2))
            {
                var totalDelta = (t2 - t1);
                if (totalDelta > 0)
                    cpu = 100.0 * (u2 - u1) / totalDelta;
            }
        }

        // MEM: 分别解析 MemTotal 和 MemFree
        ExceptionLog.WriteInfo($"[SshStatsHelper] 内存总量行: {memTotal}, 空闲行: {memFree}");
        var totalKb = MatchNumberAfter(memTotal ?? "", "MemTotal");
        var freeKb = MatchNumberAfter(memFree ?? "", "MemFree");
        ExceptionLog.WriteInfo($"[SshStatsHelper] 内存总量: {totalKb}, 空闲: {freeKb}");

        if (totalKb.HasValue && freeKb.HasValue && totalKb.Value > 0)
        {
            mem = 100.0 * (1.0 - (double)freeKb.Value / totalKb.Value);
        }

        // NET: "rx_total tx_total" 两行，间隔 1 秒，差值为 bytes/s
        ExceptionLog.WriteInfo($"[SshStatsHelper] NET1: {net1}, NET2: {net2}");
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

        ExceptionLog.WriteInfo($"[SshStatsHelper] 解析结果 - CPU: {cpu}, 内存: {mem}, 下行: {rxBps}, 上行: {txBps}, TCP: {tcp}, UDP: {udp}");
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
