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
}
