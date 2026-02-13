using System.IO;
using NUnit.Framework;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm.Tests;

/// <summary>IStorageService / StorageService 的加载与保存测试（使用临时目录）。</summary>
public class StorageServiceTests
{
    private string _tempDir = null!;
    private string _configDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "xOpenTermTests_" + Guid.NewGuid().ToString("N")[..8]);
        _configDir = Path.Combine(_tempDir, "config");
        Directory.CreateDirectory(_configDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { /* 忽略清理失败 */ }
    }

    [Test]
    public void StorageService_GetConfigDir_UnderCurrentDirectory_ReturnsConfigPath()
    {
        var prevDir = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = _tempDir;
            IStorageService storage = new StorageService();
            var dir = storage.GetConfigDir();
            Assert.That(dir, Is.EqualTo(_configDir).IgnoreCase);
        }
        finally
        {
            Environment.CurrentDirectory = prevDir;
        }
    }

    [Test]
    public void StorageService_LoadNodes_WhenFileMissing_ReturnsEmptyList()
    {
        var prevDir = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = _tempDir;
            IStorageService storage = new StorageService();
            var nodes = storage.LoadNodes();
            Assert.That(nodes, Is.Not.Null);
            Assert.That(nodes.Count, Is.EqualTo(0));
        }
        finally
        {
            Environment.CurrentDirectory = prevDir;
        }
    }

    [Test]
    public void StorageService_SaveNodes_ThenLoadNodes_RoundTrips()
    {
        var prevDir = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = _tempDir;
            IStorageService storage = new StorageService();
            var nodes = new List<Node>
            {
                new Node { Id = "n1", ParentId = "", Type = NodeType.group, Name = "Test Group", Config = null }
            };
            storage.SaveNodes(nodes);
            var loaded = storage.LoadNodes();
            Assert.That(loaded.Count, Is.EqualTo(1));
            Assert.That(loaded[0].Id, Is.EqualTo("n1"));
            Assert.That(loaded[0].Name, Is.EqualTo("Test Group"));
        }
        finally
        {
            Environment.CurrentDirectory = prevDir;
        }
    }
}
