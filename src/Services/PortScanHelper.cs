using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using xOpenTerm.Models;

namespace xOpenTerm.Services;

/// <summary>端口扫描辅助类：提供端口输入解析、服务指纹识别、扫描命令生成、结果解析等功能</summary>
public static class PortScanHelper
{
    /// <summary>常用服务端口映射（端口号 → 服务名）</summary>
    private static readonly ConcurrentDictionary<int, string> CommonServices = new(
        new Dictionary<int, string>
        {
            { 21, "FTP" }, { 22, "SSH" }, { 23, "Telnet" }, { 25, "SMTP" },
            { 53, "DNS" }, { 80, "HTTP" }, { 110, "POP3" }, { 143, "IMAP" },
            { 443, "HTTPS" }, { 445, "SMB" }, { 3306, "MySQL" }, { 3389, "RDP" },
            { 5432, "PostgreSQL" }, { 6379, "Redis" }, { 7001, "WebLogic" },
            { 8080, "HTTP-Proxy" }, { 8443, "HTTPS-Alt" }, { 9200, "Elasticsearch" },
            { 27017, "MongoDB" }
        });

    /// <summary>Top 20 常用端口（用于默认预设初始化）</summary>
    public static readonly string[] Top20Ports = new[]
    {
        "22", "80", "443", "3306", "3389", "21", "23", "25", "53", "110",
        "143", "445", "5432", "6379", "8080", "8443", "9200", "27017", "7001", "1521"
    };

    /// <summary>Web 服务端口（用于默认预设初始化）</summary>
    public static readonly string[] WebServicePorts = new[]
    {
        "80", "443", "8080", "8443", "8000", "8001", "8888", "9000", "9090", "3000"
    };

    /// <summary>数据库端口（用于默认预设初始化）</summary>
    public static readonly string[] DatabasePorts = new[]
    {
        "3306", "5432", "6379", "27017", "1521", "1433", "9042", "9300", "11211", "28017"
    };

    /// <summary>解析端口输入，返回去重并排序后的端口列表</summary>
    /// <param name="input">端口输入字符串，支持格式：22, 22,80,443, 1-1024, 22,80,443,8000-9000</param>
    /// <returns>解析出的端口列表（升序排列，无重复）</returns>
    /// <exception cref="ArgumentException">输入格式无效时抛出</exception>
    public static List<int> ParsePortInput(string input)
    {
        var ports = new HashSet<int>();
        input = input.Trim();

        if (string.IsNullOrEmpty(input))
            return new List<int>();

        // 按逗号分割各部分
        var parts = input.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            // 检查是否为端口范围（如 1-1024）
            if (trimmed.Contains('-'))
            {
                var rangeParts = trimmed.Split('-');
                if (rangeParts.Length != 2)
                    throw new ArgumentException($"无效的端口范围格式：{part}");

                if (!int.TryParse(rangeParts[0].Trim(), out var start) ||
                    !int.TryParse(rangeParts[1].Trim(), out var end))
                    throw new ArgumentException($"无效的端口号：{part}");

                if (start < 1 || start > 65535)
                    throw new ArgumentException($"端口超出有效范围（1-65535）：{start}");
                if (end < 1 || end > 65535)
                    throw new ArgumentException($"端口超出有效范围（1-65535）：{end}");
                if (start > end)
                    throw new ArgumentException($"端口范围起始值不能大于结束值：{part}");

                // 添加范围内的所有端口
                for (var p = start; p <= end; p++)
                    ports.Add(p);
            }
            else
            {
                // 单个端口
                if (!int.TryParse(trimmed, out var port))
                    throw new ArgumentException($"无效的端口号：{part}");

                if (port < 1 || port > 65535)
                    throw new ArgumentException($"端口超出有效范围（1-65535）：{port}");

                ports.Add(port);
            }
        }

        return ports.OrderBy(p => p).ToList();
    }

    /// <summary>根据端口号识别服务（基于常用端口映射）</summary>
    /// <param name="port">端口号</param>
    /// <returns>服务名称（如 SSH、HTTP 等），未知端口返回 "未知"</returns>
    public static string IdentifyService(int port)
    {
        return CommonServices.TryGetValue(port, out var serviceName) ? serviceName : "未知";
    }

    /// <summary>生成 nc（netcat）端口扫描命令</summary>
    /// <param name="host">目标主机</param>
    /// <param name="port">目标端口</param>
    /// <param name="timeoutSeconds">超时时间（秒）</param>
    /// <returns>nc 命令字符串</returns>
    public static string GenerateNcCommand(string host, int port, int timeoutSeconds)
    {
        // nc 命令格式：timeout 3 nc -zv -w 2 host port 2>&1
        // -z: 扫描模式（不发送数据）
        // -v: 显示详细信息
        // -w: 连接超时
        return $"timeout {timeoutSeconds + 1} nc -zv -w {timeoutSeconds} {host} {port} 2>&1";
    }

    /// <summary>生成 bash /dev/tcp 端口扫描命令（nc 不可用时的备选方案）</summary>
    /// <param name="host">目标主机</param>
    /// <param name="port">目标端口</param>
    /// <param name="timeoutSeconds">超时时间（秒）</param>
    /// <returns>bash 命令字符串</returns>
    public static string GenerateBashCommand(string host, int port, int timeoutSeconds)
    {
        // bash /dev/tcp 检测命令
        return $"timeout {timeoutSeconds} bash -c 'echo >/dev/tcp/{host}/{port}' 2>&1 && echo 'OPEN' || echo 'CLOSED'";
    }

    /// <summary>生成服务探测命令（深度扫描时用于获取服务 banner）</summary>
    /// <param name="host">目标主机</param>
    /// <param name="port">目标端口</param>
    /// <param name="timeoutSeconds">超时时间（秒）</param>
    /// <returns>探测命令字符串</returns>
    public static string GenerateProbeCommand(string host, int port, int timeoutSeconds)
    {
        // 优先使用 nc 发送空探测包获取 banner
        return $"timeout {timeoutSeconds} bash -c 'echo -e \"\\r\\n\" | nc {host} {port} 2>&1' | head -c 512";
    }

    /// <summary>解析 nc 命令输出，判断端口是否开放</summary>
    /// <param name="port">目标端口</param>
    /// <param name="output">nc 命令输出</param>
    /// <returns>(是否开放, 输出信息)</returns>
    public static (bool IsOpen, string Message) ParseNcOutput(int port, string? output)
    {
        if (string.IsNullOrEmpty(output))
            return (false, "命令无输出");

        var lower = output.ToLower();
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        // 逐行分析 nc 输出
        // nc (netcat) 的典型输出格式：
        // 开放端口: "Connection to 192.168.1.1 80 port [tcp/*] succeeded!"
        // 开放端口: "192.168.1.1:80 (tcp) open"
        // 开放端口: "80/tcp open"
        // 关闭端口: "192.168.1.1:80 (tcp) closed"
        // 关闭端口: "80/tcp closed"
        // 超时/防火墙: "timed out" 或无输出

        foreach (var line in lines)
        {
            var lineLower = line.ToLower().Trim();

            // 明确的开放端口特征
            if (lineLower.Contains("succeeded") ||
                lineLower.Contains($"port {port}/tcp") && lineLower.Contains("open") ||
                lineLower.Contains($"[tcp/*/tcp] succeeded") ||
                (lineLower.Contains("open") && !lineLower.Contains("closed") && line.Contains(port.ToString())))
            {
                return (true, "开放");
            }

            // 明确的关闭端口特征
            if (lineLower.Contains("closed") ||
                lineLower.Contains("refused") ||
                lineLower.Contains("filtered"))
            {
                return (false, "关闭");
            }
        }

        // 检查是否包含端口号（用于处理特殊格式）
        if (output.Contains(port.ToString()))
        {
            // 如果包含端口号但没有明确的关闭信息，检查是否有开放关键词
            if (lower.Contains("open") || lower.Contains("succeeded") || lower.Contains("connected"))
                return (true, "开放");
        }

        // 默认认为关闭
        return (false, "关闭");
    }

    /// <summary>解析 bash /dev/tcp 命令输出，判断端口是否开放</summary>
    /// <param name="output">bash 命令输出</param>
    /// <returns>(是否开放, 输出信息)</returns>
    public static (bool IsOpen, string Message) ParseBashOutput(string? output)
    {
        if (string.IsNullOrEmpty(output))
            return (false, "命令无输出");

        if (output.Contains("OPEN"))
            return (true, "开放");

        return (false, "关闭");
    }

    /// <summary>根据服务 banner 识别服务类型（比仅根据端口号更准确）</summary>
    /// <param name="port">端口号（用于降级猜测）</param>
    /// <param name="banner">服务响应 banner</param>
    /// <returns>服务名称</returns>
    public static string IdentifyServiceFromBanner(int port, string banner)
    {
        if (string.IsNullOrWhiteSpace(banner))
            return $"开放（{IdentifyService(port)}）"; // 无 banner，返回端口号猜测

        var lower = banner.ToLower();

        // SSH 检测
        if (lower.Contains("ssh") || lower.Contains("openssh"))
            return "SSH";

        // HTTP/HTTPS 检测
        if (lower.StartsWith("http/") || lower.Contains("server:") || banner.Contains("<html"))
            return lower.Contains("http/2") ? "HTTP/2" : "HTTP";

        // 数据库检测
        if (lower.Contains("mysql") || lower.Contains("mariadb"))
            return "MySQL";
        if (lower.Contains("redis"))
            return "Redis";
        if (lower.Contains("postgresql") || lower.Contains("postgres"))
            return "PostgreSQL";
        if (lower.Contains("mongodb") || lower.Contains("mongo"))
            return "MongoDB";

        // FTP 检测
        if (banner.StartsWith("220") && lower.Contains("ftp"))
            return "FTP";

        // SMTP 检测
        if (banner.StartsWith("220") && (lower.Contains("smtp") || lower.Contains("mail")))
            return "SMTP";

        // Elasticsearch
        if (lower.Contains("elasticsearch"))
            return "Elasticsearch";

        // 无法识别
        return $"开放（未知服务）";
    }

    /// <summary>执行单个端口的深度扫描（开放检测 + 服务识别）</summary>
    /// <param name="host">目标主机</param>
    /// <param name="port">目标端口</param>
    /// <param name="timeoutSeconds">超时时间（秒）</param>
    /// <param name="executeCommand">SSH 命令执行委托</param>
    /// <returns>(是否开放, 服务名称, Banner)</returns>
    public static async Task<(bool IsOpen, string Service, string Banner)> DeepScanPortAsync(
        string host, int port, int timeoutSeconds,
        Func<string, Task<(string? Output, string? Error)>> executeCommand)
    {
        // 1. 先检测端口是否开放
        var checkCmd = GenerateNcCommand(host, port, Math.Min(2, timeoutSeconds));
        var (checkOutput, _) = await executeCommand(checkCmd);

        if (checkOutput == null)
            return (false, "关闭", "");

        var (isOpen, _) = ParseNcOutput(port, checkOutput);
        if (!isOpen)
            return (false, "关闭", "");

        // 2. 发送探测包获取服务 banner
        var probeCmd = GenerateProbeCommand(host, port, timeoutSeconds);
        var (banner, _) = await executeCommand(probeCmd);

        // 3. 解析 banner 识别服务
        var service = IdentifyServiceFromBanner(port, banner ?? "");

        return (true, service, banner ?? "");
    }

    /// <summary>格式化端口扫描结果为可读字符串</summary>
    /// <param name="port">端口号</param>
    /// <param name="isOpen">是否开放</param>
    /// <param name="service">服务名称</param>
    /// <param name="banner">服务 banner（可选）</param>
    /// <returns>格式化的结果字符串</returns>
    public static string FormatPortResult(int port, bool isOpen, string service, string? banner = null)
    {
        if (!isOpen)
            return $"端口 {port}: 关闭";

        var result = $"端口 {port}: 开放 ({service})";
        if (!string.IsNullOrEmpty(banner))
        {
            // 截取 banner 前 100 个字符
            var preview = banner.Length > 100 ? banner.Substring(0, 100) + "..." : banner;
            result += $"\n  Banner: {preview.Replace("\r", "").Replace("\n", " ")}";
        }
        return result;
    }
}

/// <summary>端口扫描结果数据结构</summary>
public record PortResult(int Port, bool IsOpen, string Service, string? Banner);

/// <summary>节点扫描结果数据结构</summary>
public record NodeScanResult(Node Node, List<PortResult> Ports, TimeSpan Duration);

/// <summary>本地端口扫描：从本地机器发起 TCP 连接检测</summary>
public static class LocalPortScanner
{
    /// <summary>扫描单个端口（本地发起）</summary>
    /// <param name="host">目标主机</param>
    /// <param name="port">目标端口</param>
    /// <param name="timeoutMillis">超时时间（毫秒）</param>
    /// <param name="token">取消令牌</param>
    /// <returns>(是否开放, 服务名称)</returns>
    public static async Task<(bool IsOpen, string Service)> ScanPortAsync(string host, int port, int timeoutMillis, CancellationToken token = default)
    {
        // 在开始连接前检查取消
        token.ThrowIfCancellationRequested();

        ExceptionLog.WriteInfo($"LocalPortScanner.ScanPortAsync: 开始扫描 {host}:{port}，超时 {timeoutMillis}ms");

        using var client = new TcpClient();
        var sw = Stopwatch.StartNew();

        try
        {
            // 创建超时控制
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeoutMillis);

            // 直接等待连接
            ExceptionLog.WriteInfo($"LocalPortScanner.ScanPortAsync: 正在连接 {host}:{port}...");
            await client.ConnectAsync(host, port, cts.Token);

            // 连接成功
            sw.Stop();
            client.Close();
            var service = PortScanHelper.IdentifyService(port);
            ExceptionLog.WriteInfo($"LocalPortScanner.ScanPortAsync: 端口 {host}:{port} 开放 ({service}) - 耗时 {sw.ElapsedMilliseconds}ms");
            return (true, service);
        }
        catch (OperationCanceledException)
        {
            // 超时或被取消
            sw.Stop();
            var status = token.IsCancellationRequested ? "取消" : "超时";
            ExceptionLog.WriteInfo($"LocalPortScanner.ScanPortAsync: 端口 {host}:{port} {status} - 耗时 {sw.ElapsedMilliseconds}ms");
            return (false, status);
        }
        catch (SocketException ex) when (
            ex.SocketErrorCode == SocketError.ConnectionRefused ||
            ex.SocketErrorCode == SocketError.HostUnreachable ||
            ex.SocketErrorCode == SocketError.NetworkUnreachable)
        {
            // 明确的连接拒绝
            sw.Stop();
            ExceptionLog.WriteInfo($"LocalPortScanner.ScanPortAsync: 端口 {host}:{port} 关闭 - {ex.SocketErrorCode} - 耗时 {sw.ElapsedMilliseconds}ms");
            return (false, "关闭");
        }
        catch (SocketException ex)
        {
            // 其他 Socket 错误
            sw.Stop();
            var status = ex.SocketErrorCode == SocketError.TimedOut ? "超时" : "关闭";
            ExceptionLog.WriteInfo($"LocalPortScanner.ScanPortAsync: 端口 {host}:{port} {status} - {ex.SocketErrorCode}: {ex.Message} - 耗时 {sw.ElapsedMilliseconds}ms");
            return (false, status);
        }
        catch (Exception ex)
        {
            // 其他异常
            sw.Stop();
            ExceptionLog.WriteInfo($"LocalPortScanner.ScanPortAsync: 端口 {host}:{port} 异常 - {ex.GetType().Name}: {ex.Message} - 耗时 {sw.ElapsedMilliseconds}ms");
            return (false, "关闭");
        }
    }

    /// <summary>批量扫描端口（本地发起）</summary>
    /// <param name="host">目标主机</param>
    /// <param name="ports">端口列表</param>
    /// <param name="timeoutMillis">超时时间（毫秒）</param>
    /// <param name="concurrency">并发数</param>
    /// <param name="progress">进度回调</param>
    /// <param name="token">取消令牌</param>
    /// <returns>端口扫描结果列表</returns>
    public static async Task<List<PortResult>> ScanPortsAsync(
        string host,
        List<int> ports,
        int timeoutMillis,
        int concurrency,
        IProgress<(int Port, PortResult Result)>? progress = null,
        CancellationToken token = default)
    {
        var results = new ConcurrentBag<PortResult>();

        // 分批并发扫描
        for (var i = 0; i < ports.Count; i += concurrency)
        {
            token.ThrowIfCancellationRequested();

            var batch = ports.Skip(i).Take(concurrency).ToList();
            var tasks = batch.Select(async port =>
            {
                // 每个端口扫描都传入取消令牌
                var (isOpen, service) = await ScanPortAsync(host, port, timeoutMillis, token);
                var result = new PortResult(port, isOpen, service, null);
                results.Add(result);
                progress?.Report((port, result));
                return result;
            }).ToArray();

            await Task.WhenAll(tasks);
        }

        return results.OrderBy(r => r.Port).ToList();
    }
}

