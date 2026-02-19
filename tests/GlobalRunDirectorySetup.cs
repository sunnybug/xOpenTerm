using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace xOpenTerm.Tests;

/// <summary>单元测试全局设置：将进程工作目录设为仓库根下的 .run，与 run.ps1 一致。</summary>
[SetUpFixture]
public class GlobalRunDirectorySetup
{
    [OneTimeSetUp]
    public void SetWorkingDirectoryToRun()
    {
        Environment.SetEnvironmentVariable("XOPENTERM_UNIT_TEST", "1");
        var runDir = FindRunDirectory();
        if (string.IsNullOrEmpty(runDir))
            return;

        Directory.CreateDirectory(runDir);
        Directory.CreateDirectory(Path.Combine(runDir, "config"));
        Environment.CurrentDirectory = runDir;
    }

    /// <summary>查找仓库根下的 .run 目录（仓库根 = 同时包含 src 与 tests 的目录）。</summary>
    private static string? FindRunDirectory()
    {
        var repoRoot = FindRepoRoot();
        if (string.IsNullOrEmpty(repoRoot))
            return null;

        var runDir = Path.Combine(repoRoot, ".run");
        return runDir;
    }

    private static string? FindRepoRoot()
    {
        // 优先：当前目录已是仓库根或其 .run
        var current = Environment.CurrentDirectory;
        if (Directory.Exists(Path.Combine(current, "src")) && Directory.Exists(Path.Combine(current, "tests")))
            return current;

        // 从测试程序集所在目录向上查找
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, "src")) && Directory.Exists(Path.Combine(dir, "tests")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }
}
