using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using xOpenTerm.Services;

namespace xOpenTerm.Tests;

/// <summary>SSH 状态数据获取单元测试。与 WPF/UI 解耦，仅测试 Services 层：连接 + 执行统计命令 + 解析。</summary>
public class SshStatusFetchTests
{
    private const string TestHost = "192.168.1.192";
    private const ushort TestPort = 22;
    private const string TestUser = "root";
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan OverallTimeout = TimeSpan.FromSeconds(10);

    /// <summary>通过 SSH Agent 连接 root@192.168.1.192，执行统计命令并解析输出。连接超时 3s，无交互全自动，测试结束即退出。</summary>
    [Test]
    public async Task FetchSshStatusViaAgent_ConnectsAndParses()
    {
        using var cts = new CancellationTokenSource(OverallTimeout);
        var output = await SessionManager.RunSshCommandAsync(
            TestHost, TestPort, TestUser,
            password: null, keyPath: null, keyPassphrase: null,
            jumpChain: null, useAgent: true,
            command: SshStatsHelper.StatsCommand,
            cancellationToken: cts.Token,
            connectionTimeout: ConnectionTimeout).ConfigureAwait(false);

        Assert.That(output, Is.Not.Null.And.Not.Empty, "SSH 连接或命令执行失败（需 SSH Agent 已启动并添加私钥，且 192.168.1.192 可达，连接超时 3s）");

        var (cpu, mem, rxBps, txBps, tcp, udp) = SshStatsHelper.ParseStatsOutput(output);
        Assert.That(cpu.HasValue || mem.HasValue || rxBps.HasValue || txBps.HasValue || tcp.HasValue || udp.HasValue,
            "解析未得到任何有效数据，请确认目标为 Linux 且 /proc 可用");
    }
}
