using System.Reflection;
using NUnit.Framework;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm.Tests;

/// <summary>金山云同步单元测试：使用 .run/config 中的金山云密钥调用 ListInstances，用于复现和验证同步问题。</summary>
public class KingsoftCloudSyncTests
{
    private static string GetFullExceptionMessage(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e != null; e = e.InnerException)
            parts.Add($"{e.GetType().Name}: {e.Message}");
        return string.Join(" => ", parts);
    }

    private static string? FindRepoRoot()
    {
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        while (!string.IsNullOrEmpty(dir))
        {
            var runConfig = Path.Combine(dir, ".run", "config");
            if (Directory.Exists(runConfig))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    [Test]
    public void ListInstances_WithConfigKeys_DoesNotThrow()
    {
        var repoRoot = FindRepoRoot();
        if (string.IsNullOrEmpty(repoRoot))
        {
            Assert.Ignore("未找到包含 .run/config 的仓库根目录，跳过金山云同步测试。请在仓库根目录执行 dotnet test。");
            return;
        }

        var prevDir = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = repoRoot;
            var storage = new StorageService();
            var nodes = storage.LoadNodes();
            var kingsoftGroup = nodes.FirstOrDefault(n =>
                n.Type == NodeType.kingsoftCloudGroup
                && !string.IsNullOrWhiteSpace(n.Config?.KsyunAccessKeyId)
                && !string.IsNullOrWhiteSpace(n.Config?.KsyunAccessKeySecret));

            if (kingsoftGroup == null)
            {
                Assert.Ignore(".run/config 中未找到已配置密钥的金山云组节点，跳过测试。");
                return;
            }

            var accessKeyId = kingsoftGroup.Config!.KsyunAccessKeyId!.Trim();
            var accessKeySecret = kingsoftGroup.Config.KsyunAccessKeySecret?.Trim() ?? "";

            var progressReports = new List<(string message, int current, int total)>();
            var progress = new Progress<(string message, int current, int total)>(p => progressReports.Add(p));

            List<KsyunKecInstance>? instances = null;
            Exception? caught = null;
            try
            {
                instances = KingsoftCloudService.ListInstances(accessKeyId, accessKeySecret, progress, default);
            }
            catch (Exception ex)
            {
                caught = ex;
                TestContext.WriteLine($"金山云 ListInstances 异常: {ex.Message}");
                TestContext.WriteLine(ex.StackTrace);
                for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
                    TestContext.WriteLine($"Inner: {inner.GetType().Name}: {inner.Message}");
            }

            if (caught != null)
            {
                var fullMsg = GetFullExceptionMessage(caught);
                Assert.Fail($"金山云同步应不抛异常，实际: {fullMsg}. {Environment.NewLine}{caught.StackTrace}");
                return;
            }

            Assert.That(instances, Is.Not.Null, "ListInstances 应返回非 null 列表");
            TestContext.WriteLine($"拉取到 KEC 实例数: {instances!.Count}");
            foreach (var p in progressReports)
                TestContext.WriteLine($"  进度: {p.message} ({p.current}/{p.total})");

            foreach (var inst in instances)
            {
                Assert.That(inst.InstanceId, Is.Not.Empty, "实例应有 InstanceId");
                Assert.That(inst.RegionId, Is.Not.Empty, "实例应有 RegionId");
            }
        }
        finally
        {
            Environment.CurrentDirectory = prevDir;
        }
    }
}
