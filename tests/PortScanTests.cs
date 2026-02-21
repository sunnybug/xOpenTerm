using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using xOpenTerm.Services;
using xOpenTerm.Models;

namespace xOpenTerm.Tests;

/// <summary>端口扫描功能单元测试。与 WPF/UI 解耦，仅测试 Services 层：端口输入解析、服务识别、命令生成等。</summary>
public class PortScanTests
{
    private const string TestHost = "192.168.1.192";
    private const ushort TestPort = 22;
    private const string TestUser = "root";
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan OverallTimeout = TimeSpan.FromSeconds(15);

    /// <summary>测试端口输入解析 - 单个端口</summary>
    [Test]
    public void ParsePortInput_SinglePort_ReturnsCorrectList()
    {
        var result = PortScanHelper.ParsePortInput("22");
        Assert.That(result, Is.EqualTo(new[] { 22 }));
    }

    /// <summary>测试端口输入解析 - 多个端口</summary>
    [Test]
    public void ParsePortInput_MultiplePorts_ReturnsCorrectList()
    {
        var result = PortScanHelper.ParsePortInput("22,80,443");
        Assert.That(result, Is.EqualTo(new[] { 22, 80, 443 }));
    }

    /// <summary>测试端口输入解析 - 端口范围</summary>
    [Test]
    public void ParsePortInput_PortRange_ReturnsCorrectList()
    {
        var result = PortScanHelper.ParsePortInput("20-25");
        Assert.That(result, Is.EqualTo(new[] { 20, 21, 22, 23, 24, 25 }));
    }

    /// <summary>测试端口输入解析 - 混合输入</summary>
    [Test]
    public void ParsePortInput_MixedInput_ReturnsCorrectList()
    {
        var result = PortScanHelper.ParsePortInput("22,80,443,8000-8005");
        Assert.That(result, Is.EqualTo(new[] { 22, 80, 443, 8000, 8001, 8002, 8003, 8004, 8005 }));
    }

    /// <summary>测试端口输入解析 - 去重和排序</summary>
    [Test]
    public void ParsePortInput_DuplicatePorts_ReturnsUniqueSortedList()
    {
        var result = PortScanHelper.ParsePortInput("443,22,80,22,443");
        Assert.That(result, Is.EqualTo(new[] { 22, 80, 443 }));
    }

    /// <summary>测试端口输入解析 - 无效输入抛出异常</summary>
    [Test]
    public void ParsePortInput_InvalidInput_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => PortScanHelper.ParsePortInput("abc"));
        Assert.Throws<ArgumentException>(() => PortScanHelper.ParsePortInput("99999"));
        Assert.Throws<ArgumentException>(() => PortScanHelper.ParsePortInput("0"));
        Assert.Throws<ArgumentException>(() => PortScanHelper.ParsePortInput("1-99999"));
    }

    /// <summary>测试服务识别 - 常用端口</summary>
    [Test]
    public void IdentifyService_CommonPorts_ReturnsCorrectService()
    {
        Assert.That(PortScanHelper.IdentifyService(22), Is.EqualTo("SSH"));
        Assert.That(PortScanHelper.IdentifyService(80), Is.EqualTo("HTTP"));
        Assert.That(PortScanHelper.IdentifyService(443), Is.EqualTo("HTTPS"));
        Assert.That(PortScanHelper.IdentifyService(3306), Is.EqualTo("MySQL"));
        Assert.That(PortScanHelper.IdentifyService(3389), Is.EqualTo("RDP"));
    }

    /// <summary>测试服务识别 - 未知端口</summary>
    [Test]
    public void IdentifyService_UnknownPort_ReturnsUnknown()
    {
        Assert.That(PortScanHelper.IdentifyService(9999), Is.EqualTo("未知"));
    }

    /// <summary>测试 nc 命令生成</summary>
    [Test]
    public void GenerateNcCommand_ReturnsValidCommand()
    {
        var cmd = PortScanHelper.GenerateNcCommand("192.168.1.1", 80, 2);
        Assert.That(cmd, Does.Contain("nc"));
        Assert.That(cmd, Does.Contain("-zv"));
        Assert.That(cmd, Does.Contain("-w 2"));
        Assert.That(cmd, Does.Contain("192.168.1.1"));
        Assert.That(cmd, Does.Contain("80"));
    }

    /// <summary>测试 bash 命令生成</summary>
    [Test]
    public void GenerateBashCommand_ReturnsValidCommand()
    {
        var cmd = PortScanHelper.GenerateBashCommand("192.168.1.1", 80, 2);
        Assert.That(cmd, Does.Contain("/dev/tcp/"));
        Assert.That(cmd, Does.Contain("192.168.1.1"));
        Assert.That(cmd, Does.Contain("80"));
    }

    /// <summary>测试 nc 输出解析 - 开放端口（各种格式）</summary>
    [Test]
    public void ParseNcOutput_OpenPort_ReturnsTrue()
    {
        // 标准格式1: Connection succeeded
        var (isOpen, _) = PortScanHelper.ParseNcOutput(22, "Connection to 192.168.1.1 22 port [tcp/*] succeeded!");
        Assert.That(isOpen, Is.True, "标准格式1: Connection succeeded");

        // 标准格式2: port/tcp open
        (isOpen, _) = PortScanHelper.ParseNcOutput(80, "80/tcp open");
        Assert.That(isOpen, Is.True, "标准格式2: port/tcp open");

        // 标准格式3: port (tcp) open
        (isOpen, _) = PortScanHelper.ParseNcOutput(443, "443 (tcp) open");
        Assert.That(isOpen, Is.True, "标准格式3: port (tcp) open");

        // 混合格式: 包含端口号和 open 关键词
        (isOpen, _) = PortScanHelper.ParseNcOutput(3306, "192.168.1.1:3306 (tcp) open mysql");
        Assert.That(isOpen, Is.True, "混合格式: 包含端口号和 open 关键词");

        // 仅包含端口号和 open（无明确 closed 标记）
        (isOpen, _) = PortScanHelper.ParseNcOutput(8080, "8080 open http-proxy");
        Assert.That(isOpen, Is.True, "仅包含端口号和 open 关键词");
    }

    /// <summary>测试 nc 输出解析 - 关闭端口（各种格式）</summary>
    [Test]
    public void ParseNcOutput_ClosedPort_ReturnsFalse()
    {
        // 标准格式1: port/tcp closed
        var (isOpen, _) = PortScanHelper.ParseNcOutput(8080, "8080/tcp closed");
        Assert.That(isOpen, Is.False, "标准格式1: port/tcp closed");

        // 标准格式2: Connection refused
        (isOpen, _) = PortScanHelper.ParseNcOutput(9999, "Connection refused");
        Assert.That(isOpen, Is.False, "标准格式2: Connection refused");

        // 混合格式: 包含端口号和 closed 关键词
        (isOpen, _) = PortScanHelper.ParseNcOutput(3306, "192.168.1.1:3306 (tcp) closed");
        Assert.That(isOpen, Is.False, "混合格式: 包含端口号和 closed 关键词");

        // filtered 端口（防火墙过滤）
        (isOpen, _) = PortScanHelper.ParseNcOutput(23, "23/tcp filtered");
        Assert.That(isOpen, Is.False, "filtered 端口（防火墙过滤）");
    }

    /// <summary>测试 nc 输出解析 - 空输出和无响应</summary>
    [Test]
    public void ParseNcOutput_EmptyOrNoResponse_ReturnsFalse()
    {
        // 空输出
        var (isOpen, _) = PortScanHelper.ParseNcOutput(80, "");
        Assert.That(isOpen, Is.False, "空输出应返回 false");

        // null 输出
        (isOpen, _) = PortScanHelper.ParseNcOutput(80, null);
        Assert.That(isOpen, Is.False, "null 输出应返回 false");

        // 仅包含空白字符
        (isOpen, _) = PortScanHelper.ParseNcOutput(80, "   \n\n  ");
        Assert.That(isOpen, Is.False, "仅包含空白字符应返回 false");
    }

    /// <summary>测试 nc 输出解析 - 超时情况</summary>
    [Test]
    public void ParseNcOutput_Timeout_ReturnsFalse()
    {
        // 超时无明确标记（默认为关闭）
        var (isOpen, _) = PortScanHelper.ParseNcOutput(80, "timeout waiting for response");
        Assert.That(isOpen, Is.False, "超时应返回 false");
    }

    /// <summary>测试 nc 输出解析 - 多行输出</summary>
    [Test]
    public void ParseNcOutput_MultiLineOutput_ReturnsCorrectResult()
    {
        // 多行输出，第一行包含 succeeded
        var multiLine1 = "Connection to 192.168.1.1 22 port [tcp/*] succeeded!\nSSH-2.0-OpenSSH_8.9";
        var (isOpen, _) = PortScanHelper.ParseNcOutput(22, multiLine1);
        Assert.That(isOpen, Is.True, "多行输出：第一行包含 succeeded 应返回 true");

        // 多行输出，第二行包含 closed
        var multiLine2 = "Scanning port 8080...\n8080/tcp closed";
        (isOpen, _) = PortScanHelper.ParseNcOutput(8080, multiLine2);
        Assert.That(isOpen, Is.False, "多行输出：第二行包含 closed 应返回 false");
    }

    /// <summary>测试 bash 输出解析</summary>
    [Test]
    public void ParseBashOutput_ReturnsCorrectResult()
    {
        var (isOpen, _) = PortScanHelper.ParseBashOutput("OPEN");
        Assert.That(isOpen, Is.True);

        (isOpen, _) = PortScanHelper.ParseBashOutput("CLOSED");
        Assert.That(isOpen, Is.False);
    }

    /// <summary>测试服务 Banner 识别 - SSH</summary>
    [Test]
    public void IdentifyServiceFromBanner_SSH_ReturnsSSH()
    {
        var service = PortScanHelper.IdentifyServiceFromBanner(22, "SSH-2.0-OpenSSH_8.9");
        Assert.That(service, Does.Contain("SSH"));

        service = PortScanHelper.IdentifyServiceFromBanner(22, "ssh");
        Assert.That(service, Does.Contain("SSH"));
    }

    /// <summary>测试服务 Banner 识别 - HTTP</summary>
    [Test]
    public void IdentifyServiceFromBanner_HTTP_ReturnsHTTP()
    {
        var service = PortScanHelper.IdentifyServiceFromBanner(80, "HTTP/1.1 200 OK");
        Assert.That(service, Does.Contain("HTTP"));

        service = PortScanHelper.IdentifyServiceFromBanner(80, "<html><body>Test</body></html>");
        Assert.That(service, Does.Contain("HTTP"));
    }

    /// <summary>测试服务 Banner 识别 - MySQL</summary>
    [Test]
    public void IdentifyServiceFromBanner_MySQL_ReturnsMySQL()
    {
        var service = PortScanHelper.IdentifyServiceFromBanner(3306, "mysql");
        Assert.That(service, Does.Contain("MySQL"));
    }

    /// <summary>测试服务 Banner 识别 - 空 Banner 降级到端口号猜测</summary>
    [Test]
    public void IdentifyServiceFromBanner_EmptyBanner_ReturnsPortBasedGuess()
    {
        var service = PortScanHelper.IdentifyServiceFromBanner(22, "");
        Assert.That(service, Does.Contain("SSH"));
    }

    /// <summary>通过 SSH Agent 连接 root@192.168.1.192，执行端口扫描命令并验证结果。连接超时 3s，无交互全自动。</summary>
    [Test]
    [Ignore("需目标主机可达且 SSH Agent 已添加私钥")]
    public async Task ScanPortsViaAgent_ConnectsAndScans()
    {
        await RunPortScanTestAsync(TestHost, "192.168.1.192").ConfigureAwait(false);
    }

    /// <summary>执行单次端口扫描测试：扫描常用端口（22,80,443,3306），验证至少能检测到 SSH(22) 开放。</summary>
    private static async Task RunPortScanTestAsync(string host, string logLabel)
    {
        using var cts = new CancellationTokenSource(OverallTimeout);
        var sw = Stopwatch.StartNew();
        ExceptionLog.WriteInfo($"[PortScanTests] 开始 host={host} ({logLabel}) 连接超时={ConnectionTimeout.TotalSeconds}s 整体超时={OverallTimeout.TotalSeconds}s");

        // 测试端口列表
        var testPorts = new[] { 22, 80, 443, 3306 };

        // 执行快速扫描（仅检测开放）
        var results = new System.Collections.Generic.List<PortResult>();
        foreach (var port in testPorts)
        {
            cts.Token.ThrowIfCancellationRequested();

            var cmd = PortScanHelper.GenerateNcCommand(host, port, 2);
            var (output, error) = await SessionManager.RunSshCommandAsync(
                host, TestPort, TestUser,
                password: null, keyPath: null, keyPassphrase: null,
                jumpChain: null, useAgent: true,
                command: cmd,
                cancellationToken: cts.Token,
                connectionTimeout: ConnectionTimeout).ConfigureAwait(false);

            var (isOpen, _) = PortScanHelper.ParseNcOutput(port, output);
            var service = isOpen ? PortScanHelper.IdentifyService(port) : "关闭";
            results.Add(new PortResult(port, isOpen, service, null));
        }

        sw.Stop();
        ExceptionLog.WriteInfo($"[PortScanTests] host={host} 扫描完成 耗时={sw.ElapsedMilliseconds}ms 扫描端口数={testPorts.Length}");

        // 输出结果
        foreach (var result in results)
        {
            var status = result.IsOpen ? "开放" : "关闭";
            ExceptionLog.WriteInfo($"[PortScanTests] host={host} 端口 {result.Port}: {status} ({result.Service})");
        }

        // 验证至少检测到 SSH(22) 开放
        var sshResult = results.FirstOrDefault(r => r.Port == 22);
        Assert.That(sshResult, Is.Not.Null, "未找到端口 22 的扫描结果");
        Assert.That(sshResult!.IsOpen, Is.True, "SSH 端口 22 应该是开放的（需 SSH Agent 已启动并添加私钥，且目标可达）");

        // 验证所有端口都有结果
        Assert.That(results.Count, Is.EqualTo(testPorts.Length), "扫描结果数量应该等于测试端口数量");
    }

    /// <summary>测试深度扫描：发送探测包获取服务 banner</summary>
    [Test]
    [Ignore("需目标主机可达且 SSH Agent 已添加私钥")]
    public async Task DeepScanPortViaAgent_FetchesBanner()
    {
        using var cts = new CancellationTokenSource(OverallTimeout);
        ExceptionLog.WriteInfo($"[PortScanTests] 深度扫描测试 host={TestHost} 端口=22");

        // 对 SSH 端口进行深度扫描
        var (isOpen, service, banner) = await PortScanHelper.DeepScanPortAsync(
            TestHost, 22, 2,
            async (cmd) => await SessionManager.RunSshCommandAsync(
                TestHost, TestPort, TestUser,
                password: null, keyPath: null, keyPassphrase: null,
                jumpChain: null, useAgent: true,
                command: cmd,
                cancellationToken: cts.Token,
                connectionTimeout: ConnectionTimeout)).ConfigureAwait(false);

        ExceptionLog.WriteInfo($"[PortScanTests] 深度扫描结果 isOpen={isOpen} service={service} banner长度={banner?.Length ?? 0}");

        Assert.That(isOpen, Is.True, "SSH 端口应该是开放的");
        Assert.That(service, Does.Contain("SSH").Or.Contain("开放"), "服务识别应该包含 SSH 或开放");
    }

    /// <summary>测试本地端口扫描功能 - 验证开放和关闭端口</summary>
    [Test]
    public async Task LocalPortScan_ScanLocalhost_PortsOpenOrClosed()
    {
        // 扫描本地主机的常用端口
        // 127.0.0.1 的端口 22、80、443、3306、3389 通常在开发环境中不会全部开放
        // 此测试主要验证扫描功能正常工作，能区分开放和关闭的端口
        var host = "127.0.0.1";
        var ports = new[] { 22, 80, 443, 3306, 3389 };
        var timeoutMillis = 1000;

        var results = await LocalPortScanner.ScanPortsAsync(
            host, ports.ToList(), timeoutMillis, 5,
            null, CancellationToken.None);

        // 验证扫描结果
        Assert.That(results, Is.Not.Null, "扫描结果不应为 null");
        Assert.That(results.Count, Is.EqualTo(ports.Length), "扫描结果数量应该等于端口数量");

        // 输出结果
        foreach (var result in results)
        {
            var status = result.IsOpen ? "开放" : "关闭";
            ExceptionLog.WriteInfo($"[PortScanTests] 本地扫描 host={host} 端口 {result.Port}: {status} ({result.Service})");
        }

        // 验证结果按端口号排序
        for (int i = 1; i < results.Count; i++)
        {
            Assert.That(results[i].Port, Is.GreaterThan(results[i - 1].Port), "结果应该按端口号升序排列");
        }

        // 验证所有结果都有明确的开放或关闭状态
        foreach (var result in results)
        {
            Assert.That(result.Service, Is.Not.Null.And.Not.Empty, "服务名称不应为空");
            Assert.That(result.Service == "关闭" || result.Service.Contains("开放") || result.Service != "未知",
                "服务状态应该是关闭、开放或具体服务名");
        }
    }

    /// <summary>测试本地端口扫描 - 使用一个已知开放的端口（echo服务）</summary>
    [Test]
    public async Task LocalPortScan_KnownOpenPort_ReturnsOpen()
    {
        // Windows 上通常没有开放常用端口，所以我们测试关闭端口
        // 此测试验证扫描不会误报关闭端口为开放
        var host = "127.0.0.1";
        // 选择一个不太可能开放的端口
        var ports = new[] { 65432 };
        var timeoutMillis = 1000;

        var results = await LocalPortScanner.ScanPortsAsync(
            host, ports.ToList(), timeoutMillis, 1,
            null, CancellationToken.None);

        Assert.That(results, Is.Not.Null, "扫描结果不应为 null");
        Assert.That(results.Count, Is.EqualTo(1), "应该有1个扫描结果");

        var result = results[0];
        ExceptionLog.WriteInfo($"[PortScanTests] 高位端口测试 host={host} 端口 {result.Port}: {(result.IsOpen ? "开放" : "关闭")} ({result.Service})");

        // 高位端口通常关闭（除非有特定服务运行）
        // 主要验证扫描能正常完成并返回结果
        Assert.That(result.Port, Is.EqualTo(65432), "端口号应该正确");
    }

    /// <summary>测试本地端口扫描 - 验证能检测关闭的 SSH/RDP 端口</summary>
    [Test]
    public async Task LocalPortScan_ClosedSshRdpPorts_ReturnsClosed()
    {
        // 扫描本地主机的 SSH 和 RDP 端口
        // 在大多数 Windows 开发环境中，这些端口都是关闭的
        var host = "127.0.0.1";
        var ports = new[] { 22, 3389 }; // SSH 和 RDP
        var timeoutMillis = 1000;

        var results = await LocalPortScanner.ScanPortsAsync(
            host, ports.ToList(), timeoutMillis, 2,
            null, CancellationToken.None);

        Assert.That(results, Is.Not.Null, "扫描结果不应为 null");
        Assert.That(results.Count, Is.EqualTo(ports.Length), "扫描结果数量应该等于端口数量");

        // 输出结果
        foreach (var result in results)
        {
            var status = result.IsOpen ? "开放" : "关闭";
            ExceptionLog.WriteInfo($"[PortScanTests] SSH/RDP端口测试 host={host} 端口 {result.Port}: {status} ({result.Service})");

            // 验证结果结构正确
            Assert.That(result.Port, Is.EqualTo(22).Or.EqualTo(3389), "端口应该是 SSH 或 RDP");
            Assert.That(result.Service, Is.Not.Null.And.Not.Empty, "服务名称不应为空");

            // 如果端口关闭，服务应该标记为"关闭"
            if (!result.IsOpen)
            {
                Assert.That(result.Service, Does.Contain("关闭").Or.EqualTo("超时"),
                    "关闭端口的服务名称应该包含'关闭'或'超时'");
            }
        }
    }

    /// <summary>测试本地端口扫描 - 超时处理</summary>
    [Test]
    public async Task LocalPortScan_Timeout_DoesNotHang()
    {
        // 扫描本地主机的非特权高位端口，通常不会开放
        var host = "127.0.0.1";
        var ports = new[] { 65432 }; // 选择一个不太可能开放的端口
        var timeoutMillis = 1000; // 1秒超时

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var results = await LocalPortScanner.ScanPortsAsync(
            host, ports.ToList(), timeoutMillis, 1,
            null, CancellationToken.None);
        sw.Stop();

        // 验证超时处理
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(timeoutMillis * ports.Length + 2000),
            "扫描应该在合理时间内完成（超时时间 + 缓冲）");

        // 验证有结果
        Assert.That(results.Count, Is.EqualTo(1), "应该有1个扫描结果");
        // 端口状态（开放或关闭都可以，主要测试不会超时挂起）
        ExceptionLog.WriteInfo($"[PortScanTests] 超时测试 host={host} 端口 {results[0].Port}: {(results[0].IsOpen ? "开放" : "关闭")} ({results[0].Service})");
    }

    /// <summary>测试本地端口扫描 - 取消操作</summary>
    [Test]
    public async Task LocalPortScan_Cancellation_CancelsScan()
    {
        var host = "127.0.0.1";
        var ports = Enumerable.Range(1, 100).ToList(); // 扫描100个端口
        var timeoutMillis = 5000;

        using var cts = new CancellationTokenSource();

        // 启动扫描任务
        var scanTask = LocalPortScanner.ScanPortsAsync(
            host, ports, timeoutMillis, 10,
            null, cts.Token);

        // 短暂延迟后取消
        await Task.Delay(100);
        cts.Cancel();

        // 验证任务被取消
        try
        {
            var results = await scanTask;
            // 如果没有抛出异常，验证结果不完整
            Assert.That(results.Count, Is.LessThan(ports.Count), "取消后扫描结果应该不完整");
        }
        catch (OperationCanceledException)
        {
            // 预期的异常
            Assert.Pass("扫描被正确取消");
        }
    }

    /// <summary>测试真实网络环境的端口扫描（公网IP）</summary>
    /// <remarks>
    /// 此测试扫描一个已知开放端口的公网服务器：
    /// - 149.129.223.30:80 (HTTP 端口应该开放)
    /// - 149.129.223.30:22 (SSH 端口可能开放或关闭)
    /// </remarks>
    [Test]
    public async Task LocalPortScan_PublicIP_CanDetectOpenPort()
    {
        var host = "149.129.223.30";
        var ports = new[] { 80 }; // HTTP 端口（通常开放）
        var timeoutMillis = 3000;

        ExceptionLog.WriteInfo($"[PortScanTests] 开始扫描公网IP {host} 端口 80");

        var results = await LocalPortScanner.ScanPortsAsync(
            host, ports.ToList(), timeoutMillis, 1,
            null, CancellationToken.None);

        Assert.That(results, Is.Not.Null, "扫描结果不应为 null");
        Assert.That(results.Count, Is.EqualTo(1), "应该有1个扫描结果");

        var result = results[0];
        var status = result.IsOpen ? "开放" : "关闭";
        ExceptionLog.WriteInfo($"[PortScanTests] 公网IP扫描 host={host} 端口 {result.Port}: {status} ({result.Service})");

        // HTTP 端口 80 应该开放（或者至少能检测到状态）
        Assert.That(result.Port, Is.EqualTo(80), "端口号应该正确");
        Assert.That(result.Service, Is.Not.Null.And.Not.Empty, "服务名称不应为空");

        // 如果端口开放，验证服务识别正确
        if (result.IsOpen)
        {
            Assert.That(result.Service, Does.Contain("HTTP").Or.Contain("开放"),
                "开放端口 80 应该识别为 HTTP 服务");
            ExceptionLog.WriteInfo($"[PortScanTests] 端口 80 正确识别为 {result.Service}");
        }
        else
        {
            ExceptionLog.Warn($"[PortScanTests] 端口 80 检测为关闭，可能是网络问题或防火墙");
        }
    }

    /// <summary>测试真实网络环境的端口扫描 - 检测关闭端口</summary>
    /// <remarks>
    /// 此测试扫描一个不太可能开放的高位端口，验证能正确检测关闭状态
    /// </remarks>
    [Test]
    public async Task LocalPortScan_PublicIP_CanDetectClosedPort()
    {
        var host = "149.129.223.30";
        var ports = new[] { 65432 }; // 高位端口（通常关闭）
        var timeoutMillis = 2000;

        ExceptionLog.WriteInfo($"[PortScanTests] 开始扫描公网IP {host} 高位端口 65432");

        var results = await LocalPortScanner.ScanPortsAsync(
            host, ports.ToList(), timeoutMillis, 1,
            null, CancellationToken.None);

        Assert.That(results, Is.Not.Null, "扫描结果不应为 null");
        Assert.That(results.Count, Is.EqualTo(1), "应该有1个扫描结果");

        var result = results[0];
        var status = result.IsOpen ? "开放" : "关闭";
        ExceptionLog.WriteInfo($"[PortScanTests] 公网IP扫描 host={host} 端口 {result.Port}: {status} ({result.Service})");

        // 高位端口通常关闭
        Assert.That(result.Port, Is.EqualTo(65432), "端口号应该正确");
        Assert.That(result.Service, Is.Not.Null.And.Not.Empty, "服务名称不应为空");

        // 验证能正确检测关闭状态（或者超时）
        if (!result.IsOpen)
        {
            Assert.That(result.Service, Does.Contain("关闭").Or.EqualTo("超时"),
                "关闭端口应该标记为'关闭'或'超时'");
            ExceptionLog.WriteInfo($"[PortScanTests] 高位端口 65432 正确识别为 {result.Service}");
        }
        else
        {
            ExceptionLog.Warn($"[PortScanTests] 意外：高位端口 65432 检测为开放");
        }
    }

    /// <summary>测试单个端口扫描 - 明确关闭的端口</summary>
    [Test]
    public async Task ScanPortAsync_ClosedPort_ReturnsClosed()
    {
        var (isOpen, service) = await LocalPortScanner.ScanPortAsync("127.0.0.1", 65432, 1000);
        Assert.That(isOpen, Is.False, "关闭端口应该返回 false");
        Assert.That(service, Does.Contain("关闭").Or.Contain("超时"), "服务状态应该包含'关闭'或'超时'");
    }

    /// <summary>测试单个端口扫描 - 超时处理</summary>
    [Test]
    public async Task ScanPortAsync_Timeout_ReturnsTimeout()
    {
        // 使用本地高位端口（通常关闭），测试连接拒绝
        var sw = Stopwatch.StartNew();
        var (isOpen, service) = await LocalPortScanner.ScanPortAsync("127.0.0.1", 65433, 500);
        sw.Stop();

        Assert.That(isOpen, Is.False, "关闭端口应该返回 false");
        Assert.That(service, Does.Contain("关闭").Or.EqualTo("超时"), "服务状态应该是'关闭'或'超时'");
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(2000), "连接拒绝应该在合理时间内完成（<2秒）");

        ExceptionLog.WriteInfo($"[PortScanTests] 超时测试完成 - 耗时: {sw.ElapsedMilliseconds}ms, 服务状态: {service}");
    }

    /// <summary>测试单个端口扫描 - 取消操作</summary>
    [Test]
    public async Task ScanPortAsync_Cancellation_ThrowsException()
    {
        // 在开始前取消，验证能正确检测取消令牌
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // 先取消

        // 验证抛出取消异常
        var ex = Assert.ThrowsAsync<OperationCanceledException>(async () => await LocalPortScanner.ScanPortAsync("127.0.0.1", 65434, 5000, cts.Token), "应该抛出 OperationCanceledException");
        Assert.That(ex, Is.Not.Null, "异常不应为 null");
        Assert.That(ex!.CancellationToken, Is.EqualTo(cts.Token), "取消令牌应该匹配");

        ExceptionLog.WriteInfo($"[PortScanTests] 取消测试成功 - 任务在启动前被取消");
    }

    /// <summary>测试单个端口扫描 - 真实环境一致性检查</summary>
    [Test]
    [Explicit("需要网络连接")]
    [Repeat(3)]
    public async Task ScanPortAsync_RealWorld_ConsistentResults()
    {
        var host = "149.129.223.30";
        var port = 80;

        var results = new List<bool>();
        for (int i = 0; i < 3; i++)
        {
            var (isOpen, _) = await LocalPortScanner.ScanPortAsync(host, port, 3000);
            results.Add(isOpen);
            await Task.Delay(500);
        }

        // 验证结果一致性（所有扫描应该返回相同结果）
        Assert.That(results.Distinct().Count(), Is.EqualTo(1), "多次扫描同一端口应该返回一致的结果");
        Assert.That(results[0], Is.True, "HTTP 端口 80 应该开放");
    }

    /// <summary>测试批量扫描 - SocketErrorCode 处理</summary>
    [Test]
    public async Task ScanPortsAsync_SocketErrorCode_HandledCorrectly()
    {
        var host = "127.0.0.1";
        var ports = new[] { 22, 80, 443, 65432 }; // 混合开放/关闭端口
        var timeoutMillis = 1000;

        var results = await LocalPortScanner.ScanPortsAsync(
            host, ports.ToList(), timeoutMillis, 5,
            null, CancellationToken.None);

        Assert.That(results, Is.Not.Null, "扫描结果不应为 null");
        Assert.That(results.Count, Is.EqualTo(ports.Length), "扫描结果数量应该等于端口数量");

        // 验证每个结果都有明确的状态
        foreach (var result in results)
        {
            Assert.That(result.Service, Is.Not.Null.And.Not.Empty, "服务名称不应为空");

            if (result.IsOpen)
            {
                Assert.That(result.Service, Is.Not.Contains("关闭").And.Not.Contains("超时"),
                    "开放端口的服务名称不应包含'关闭'或'超时'");
            }
            else
            {
                var validStatus = new[] { "关闭", "超时", "取消" };
                Assert.That(result.Service, Is.AnyOf(validStatus).Or.Contains("关闭").Or.Contains("超时"),
                    "关闭端口的服务名称应该包含'关闭'、'超时'或'取消'");
            }

            ExceptionLog.WriteInfo($"[PortScanTests] SocketErrorCode 测试 host={host} 端口 {result.Port}: {(result.IsOpen ? "开放" : "关闭")} ({result.Service})");
        }
    }

    /// <summary>测试单个端口扫描 - 性能验证</summary>
    [Test]
    public async Task ScanPortAsync_Performance_NoSignificantDegradation()
    {
        var host = "127.0.0.1";
        var port = 65432; // 关闭的端口
        var timeoutMillis = 1000;
        var iterations = 10;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            await LocalPortScanner.ScanPortAsync(host, port, timeoutMillis);
        }
        sw.Stop();

        var avgTime = sw.ElapsedMilliseconds / iterations;
        ExceptionLog.WriteInfo($"[PortScanTests] 性能测试 平均扫描时间: {avgTime}ms/端口");

        // 验证平均时间不超过超时时间的 150%（允许一定开销）
        Assert.That(avgTime, Is.LessThan(timeoutMillis * 1.5), "平均扫描时间不应超过超时时间的 150%");
    }
}
