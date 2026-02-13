using System.Text;
using NUnit.Framework;
using xOpenTerm.Services;

namespace xOpenTerm.Tests;

/// <summary>测试 MobaXterm.ini 解析（含 GBK 编码）。</summary>
public class MobaXtermIniParserTests
{
    private const string TestIniPath = @"d:\xsw\Dropbox\tool\net\MobaXterm\MobaXterm.ini";

    [Test]
    public void Parse_MobaXtermIni_ReadsSessions_WithGbkEncoding()
    {
        if (!File.Exists(TestIniPath))
        {
            Assert.Ignore($"测试用 INI 不存在，跳过: {TestIniPath}");
            return;
        }

        var sessions = MobaXtermIniParser.Parse(TestIniPath);
        Assert.That(sessions, Is.Not.Null, "解析不应返回 null");

        var folderTree = MobaXtermIniParser.BuildFolderTree(sessions);
        Assert.That(folderTree, Is.Not.Null);
        var totalFromTree = folderTree.Sum(f => f.TotalSessionCount);
        Assert.That(totalFromTree, Is.EqualTo(sessions.Count), "目录树下会话总数应与扁平列表一致");

        foreach (var s in sessions)
        {
            Assert.That(s.Host, Is.Not.Empty, "会话应有 Host");
            Assert.That(s.Port, Is.GreaterThan(0), "端口应大于 0");
        }

        if (sessions.Count > 0)
            Assert.That(folderTree.Count, Is.GreaterThan(0), "有会话时目录树应至少有一项（根目录或子目录）");

        // 断言解码无乱码：不应出现替换符 U+FFFD（错误解码时会产生）
        if (sessions.Count > 0)
        {
            AssertNoReplacementChar(folderTree, sessions);
        }

        // 输出：导入的目录总数、每个目录下包含的服务器节点数（控制台 UTF-8 时中文正常显示）
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* 忽略 */ }
        var dirCount = CountFoldersRecursive(folderTree);
        TestContext.WriteLine($"导入目录数: {dirCount}");
        TestContext.WriteLine($"服务器节点总数: {sessions.Count}");
        WriteFolderStats(folderTree, indent: "");
    }

    private static int CountFoldersRecursive(List<MobaFolderNode> roots)
    {
        var n = roots.Count;
        foreach (var r in roots)
            n += CountFoldersRecursive(r.SubFolders);
        return n;
    }

    private static void WriteFolderStats(List<MobaFolderNode> roots, string indent)
    {
        foreach (var f in roots)
        {
            var displayName = string.IsNullOrEmpty(f.FullPath) ? "(根目录)" : f.Name;
            TestContext.WriteLine($"{indent}{displayName}: 本目录 {f.Sessions.Count} 个节点, 含子目录共 {f.TotalSessionCount} 个节点");
            WriteFolderStats(f.SubFolders, indent + "  ");
        }
    }

    private static void AssertNoReplacementChar(List<MobaFolderNode> roots, List<MobaXtermSessionItem> sessions)
    {
        foreach (var f in roots)
        {
            Assert.That(f.Name, Does.Not.Contain('\uFFFD'), "目录名不应含解码替换符（乱码）");
            Assert.That(f.FullPath, Does.Not.Contain('\uFFFD'), "目录路径不应含解码替换符（乱码）");
            AssertNoReplacementChar(f.SubFolders, []);
        }
        foreach (var s in sessions)
        {
            Assert.That(s.SessionName ?? "", Does.Not.Contain('\uFFFD'), "会话名不应含解码替换符（乱码）");
            Assert.That(s.FolderPath ?? "", Does.Not.Contain('\uFFFD'), "文件夹路径不应含解码替换符（乱码）");
        }
    }

    [Test]
    public void Parse_NonExistentPath_ReturnsEmptyList()
    {
        var sessions = MobaXtermIniParser.Parse(@"C:\NonExistent\MobaXterm.ini");
        Assert.That(sessions, Is.Not.Null);
        Assert.That(sessions, Is.Empty);
    }

    [Test]
    public void BuildFolderTree_EmptySessions_ReturnsEmptyRoots()
    {
        var tree = MobaXtermIniParser.BuildFolderTree(new List<MobaXtermSessionItem>());
        Assert.That(tree, Is.Not.Null);
        Assert.That(tree, Is.Empty);
    }

    [Test]
    public void ParsePasswordFile_ValidFormat_ParsesKeyUsernamePassword()
    {
        var path = Path.Combine(Path.GetTempPath(), "xot_moba_test_" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(path, "MySession(root) = secret123\nProd(admin) = pwd456\n", Encoding.UTF8);
            var dict = MobaXtermIniParser.ParsePasswordFile(path);
            Assert.That(dict, Is.Not.Null);
            Assert.That(dict.Count, Is.EqualTo(2));
            Assert.That(dict["MySession"].Username, Is.EqualTo("root"));
            Assert.That(dict["MySession"].Password, Is.EqualTo("secret123"));
            Assert.That(dict["Prod"].Username, Is.EqualTo("admin"));
            Assert.That(dict["Prod"].Password, Is.EqualTo("pwd456"));
        }
        finally { try { File.Delete(path); } catch { /* 忽略 */ } }
    }

    [Test]
    public void ParsePasswordFile_KeyLookupIsCaseInsensitive()
    {
        var path = Path.Combine(Path.GetTempPath(), "xot_moba_test_" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(path, "MySession(root) = secret\n", Encoding.UTF8);
            var dict = MobaXtermIniParser.ParsePasswordFile(path);
            Assert.That(dict["mysession"].Username, Is.EqualTo("root"));
            Assert.That(dict["mysession"].Password, Is.EqualTo("secret"));
        }
        finally { try { File.Delete(path); } catch { /* 忽略 */ } }
    }

    [Test]
    public void ParsePasswordFile_NonExistent_ReturnsEmpty()
    {
        var dict = MobaXtermIniParser.ParsePasswordFile(@"C:\NonExistent\pass.txt");
        Assert.That(dict, Is.Not.Null);
        Assert.That(dict, Is.Empty);
    }

    [Test]
    public void ParsePasswordFile_EmptyFile_ReturnsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), "xot_moba_test_" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(path, "", Encoding.UTF8);
            var dict = MobaXtermIniParser.ParsePasswordFile(path);
            Assert.That(dict, Is.Not.Null);
            Assert.That(dict, Is.Empty);
        }
        finally { try { File.Delete(path); } catch { /* 忽略 */ } }
    }
}
