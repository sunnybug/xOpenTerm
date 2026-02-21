using System;
using xOpenTerm.Services;

namespace xOpenTerm
{
    class TestNcParse
    {
        static void Main()
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
                Console.WriteLine();

                if (isOpen == expectedOpen)
                    passed++;
                else
                    failed++;
            }

            Console.WriteLine($"=== 统计 ===");
            Console.WriteLine($"通过: {passed}/{testCases.Length}");
            Console.WriteLine($"失败: {failed}/{testCases.Length}");
        }
    }
}
