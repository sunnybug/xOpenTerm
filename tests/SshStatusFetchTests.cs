using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using xOpenTerm.Services;

namespace xOpenTerm.Tests;

/// <summary>SSH 状态数据获取单元测试。与 WPF/UI 解耦，仅测试 Services 层：连接 + 执行统计命令 + 解析。</summary>
public class SshStatusFetchTests
{
    private const string TestHost = "192.168.1.192";
    private const string TestHostCentos65 = "120.92.35.205";
    private const string TestHostCentos7 = "120.92.82.22";
    private const ushort TestPort = 22;
    private const string TestUser = "root";
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan OverallTimeout = TimeSpan.FromSeconds(10);

    /// <summary>通过 SSH Agent 连接 root@192.168.1.192，执行统计命令并解析输出。连接超时 3s，无交互全自动，测试结束即退出。</summary>
    [Test]
    [Ignore("当前不测试 192.168.1.192")]
    public async Task FetchSshStatusViaAgent_ConnectsAndParses()
    {
        await RunSshStatusTestAsync(TestHost, "192.168.1.192").ConfigureAwait(false);
    }

    /// <summary>通过 SSH Agent 连接 CentOS 6.5 root@120.92.35.205，执行统计命令并解析输出。</summary>
    [Test]
    public async Task FetchSshStatusViaAgent_Centos65_ConnectsAndParses()
    {
        await RunSshStatusTestAsync(TestHostCentos65, "120.92.35.205 (CentOS 6.5)").ConfigureAwait(false);
    }

    /// <summary>通过 SSH Agent 连接 CentOS 7 root@120.92.82.22，执行统计命令并解析输出。需本机可达该主机且 Agent 已添加对应私钥。</summary>
    [Test]
    [Ignore("需本机可达 120.92.82.22 且 SSH Agent 已添加对应私钥；满足条件时移除本 Ignore 再跑")]
    public async Task FetchSshStatusViaAgent_Centos7_ConnectsAndParses()
    {
        await RunSshStatusTestAsync(TestHostCentos7, "120.92.82.22 (CentOS 7)").ConfigureAwait(false);
    }

    /// <summary>执行单次 SSH 状态测试：写日志、整体超时 10s 内未返回则直接失败（避免两用例合计卡 40s）。</summary>
    private static async Task RunSshStatusTestAsync(string host, string logLabel)
    {
        using var cts = new CancellationTokenSource(OverallTimeout);
        var sw = Stopwatch.StartNew();
        ExceptionLog.WriteInfo($"[SshStatusFetchTests] 开始 host={host} ({logLabel}) 连接超时={ConnectionTimeout.TotalSeconds}s 整体超时={OverallTimeout.TotalSeconds}s");

        var task = SessionManager.RunSshCommandAsync(
            host, TestPort, TestUser,
            password: null, keyPath: null, keyPassphrase: null,
            jumpChain: null, useAgent: true,
            command: SshStatsHelper.StatsCommand,
            cancellationToken: cts.Token,
            connectionTimeout: ConnectionTimeout);

        var completed = await Task.WhenAny(task, Task.Delay(OverallTimeout, cts.Token)).ConfigureAwait(false);
        sw.Stop();
        if (completed != task)
        {
            ExceptionLog.WriteInfo($"[SshStatusFetchTests] host={host} 整体超时 {OverallTimeout.TotalSeconds}s 已到，未返回 耗时={sw.ElapsedMilliseconds}ms");
            Assert.Fail($"SSH 在 {OverallTimeout.TotalSeconds}s 内未返回（host={host}）。请查看 .run/log：无「RunCommand 完成」即卡在命令执行，无「Connect 完成」即卡在连接");
        }

        var output = await task.ConfigureAwait(false);
        ExceptionLog.WriteInfo($"[SshStatusFetchTests] host={host} 返回 耗时={sw.ElapsedMilliseconds}ms 有输出={output != null && output.Length > 0}");

        Assert.That(output, Is.Not.Null.And.Not.Empty, "SSH 连接或命令执行失败（需 SSH Agent 已启动并添加私钥，且目标可达，连接超时 3s）");

        var (cpu, mem, rxBps, txBps, tcp, udp) = SshStatsHelper.ParseStatsOutput(output);
        Assert.That(cpu.HasValue || mem.HasValue || rxBps.HasValue || txBps.HasValue || tcp.HasValue || udp.HasValue,
            "解析未得到任何有效数据，请确认目标为 Linux 且 /proc 可用");
    }
}
