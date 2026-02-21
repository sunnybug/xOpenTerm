using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using xOpenTerm.Services;
using static xOpenTerm.Services.ExceptionLog;

namespace xOpenTerm.Tests;

/// <summary>端口扫描手动测试：用于诊断实际的端口扫描问题</summary>
[TestFixture]
public class PortScanManualTests
{
    /// <summary>测试本地端口扫描 - 详细的连接测试</summary>
    [Test]
    [Explicit("需要手动运行，用于诊断问题")]
    public async Task ManualTest_LocalPortScan_WithDetails()
    {
        Console.WriteLine("=== 本地端口扫描详细测试 ===\n");

        // 测试本地主机（应该有一些端口开放）
        var testCases = new[]
        {
            ("127.0.0.1", 3389, "RDP (通常开放)"),
            ("127.0.0.1", 22, "SSH (通常关闭)"),
            ("127.0.0.1", 80, "HTTP (通常关闭)"),
            ("127.0.0.1", 9999, "高位端口 (通常关闭)")
        };

        foreach (var (host, port, desc) in testCases)
        {
            Console.WriteLine($"\n测试: {desc} ({host}:{port})");

            try
            {
                var (isOpen, service) = await LocalPortScanner.ScanPortAsync(
                    host, port, 2000, CancellationToken.None);

                var status = isOpen ? "✓ 开放" : "✗ 关闭";
                Console.WriteLine($"  结果: {status}");
                Console.WriteLine($"  服务: {service}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  异常: {ex.Message}");
            }
        }

        Console.WriteLine("\n=== 测试完成 ===");
    }

    /// <summary>测试 nc 输出解析 - 各种可能的输出格式</summary>
    [Test]
    [Explicit("需要手动运行，用于诊断问题")]
    public void ManualTest_ParseNcOutput_VariousFormats()
    {
        Console.WriteLine("=== nc 输出解析测试 ===\n");

        var testCases = new[]
        {
            (22, "Connection to 192.168.1.1 22 port [tcp/*] succeeded!", true, "开放 - succeeded"),
            (80, "192.168.1.1:80 (tcp) open", true, "开放 - open"),
            (443, "443/tcp open", true, "开放 - 简短格式"),
            (3306, "192.168.1.1:3306 (tcp) closed", false, "关闭 - closed"),
            (22, "22/tcp closed", false, "关闭 - 简短格式"),
            (8080, "Connection refused", false, "关闭 - refused"),
            (9090, "nc: connect to 192.168.1.1 port 9090 (tcp) failed: Connection refused", false, "关闭 - 完整错误"),
            (3389, "", false, "无输出"),
            (53, "nc: timeout", false, "超时")
        };

        var passed = 0;
        var failed = 0;

        foreach (var (port, output, expectedOpen, description) in testCases)
        {
            var (isOpen, message) = PortScanHelper.ParseNcOutput(port, output);
            var status = isOpen ? "开放" : "关闭";
            var expectedStatus = expectedOpen ? "开放" : "关闭";
            var result = isOpen == expectedOpen ? "✓" : "✗";

            Console.WriteLine($"{result} {description}");
            Console.WriteLine($"   输入: \"{output}\"");
            Console.WriteLine($"   结果: {status} ({message})");
            Console.WriteLine($"   预期: {expectedStatus}");

            if (isOpen == expectedOpen)
                passed++;
            else
                failed++;
        }

        Console.WriteLine($"\n=== 测试统计 ===");
        Console.WriteLine($"通过: {passed}/{testCases.Length}");
        Console.WriteLine($"失败: {failed}/{testCases.Length}");

        if (failed > 0)
        {
            Console.WriteLine("\n⚠️ 有测试失败，请检查 ParseNcOutput 的解析逻辑");
        }
    }

    /// <summary>测试深度扫描功能</summary>
    [Test]
    [Explicit("需要手动运行，用于诊断问题")]
    [Ignore("需要实际的 SSH 连接")]
    public async Task ManualTest_DeepScan_WithMockCommand()
    {
        Console.WriteLine("=== 深度扫描测试（模拟） ===\n");

        // 模拟 nc 命令输出
        var mockOutputs = new[]
        {
            (22, "Connection to 192.168.1.1 22 port [tcp/*] succeeded!", true, "SSH 开放"),
            (80, "Connection to 192.168.1.1 80 port [tcp/*] succeeded!", true, "HTTP 开放"),
            (3306, "nc: connect to 192.168.1.1 port 3306 (tcp) failed: Connection refused", false, "MySQL 关闭")
        };

        foreach (var (port, ncOutput, expectedOpen, description) in mockOutputs)
        {
            Console.WriteLine($"\n测试: {description} (端口 {port})");

            // 模拟深度扫描
            var (isOpen, service, banner) = await PortScanHelper.DeepScanPortAsync(
                "192.168.1.1", port, 2,
                async (cmd) =>
                {
                    Console.WriteLine($"  命令: {cmd}");
                    // 返回模拟输出
                    await Task.Delay(10);
                    return (ncOutput, null);
                });

            var status = isOpen ? "开放" : "关闭";
            var result = isOpen == expectedOpen ? "✓" : "✗";
            Console.WriteLine($"  {result} 结果: {status} ({service})");
            if (!string.IsNullOrEmpty(banner))
            {
                Console.WriteLine($"  Banner: {banner.Substring(0, Math.Min(50, banner.Length))}...");
            }
        }

        Console.WriteLine("\n=== 测试完成 ===");
    }

    /// <summary>真实网络环境测试 - 扫描公网服务器</summary>
    [Test]
    [Explicit("需要手动运行，需要网络连接")]
    public async Task ManualTest_RealNetwork_ScanPublicServer()
    {
        Console.WriteLine("=== 真实网络测试 ===\n");

        // 扫描一个公网的 DNS 服务器（应该开放 53 端口）
        var host = "8.8.8.8"; // Google DNS
        var ports = new[] { 53, 80, 443 };

        Console.WriteLine($"目标: {host} (Google DNS)");
        Console.WriteLine("预期: 53 端口开放，其他端口可能关闭\n");

        foreach (var port in ports)
        {
            Console.Write($"扫描端口 {port}... ");

            try
            {
                var (isOpen, service) = await LocalPortScanner.ScanPortAsync(
                    host, port, 3000, CancellationToken.None);

                var status = isOpen ? "✓ 开放" : "✗ 关闭";
                Console.WriteLine($"{status} ({service})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"异常: {ex.Message}");
            }
        }

        Console.WriteLine("\n=== 测试完成 ===");
    }
}
