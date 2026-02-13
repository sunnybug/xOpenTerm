using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using xOpenTerm.Controls;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace xOpenTerm.Services;

/// <summary>编辑框输入历史：按字段键持久化到 config/input_history.yaml，支持获取与追加（去重、条数上限）。</summary>
public class InputHistoryService
{
    private const string HistoryFileName = "input_history.yaml";
    private const int MaxEntriesPerKey = 50;

    private readonly string _filePath;
    private readonly object _lock = new();
    private Dictionary<string, List<string>> _data = new();
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    public InputHistoryService()
    {
        var configDir = StorageService.GetConfigDir();
        Directory.CreateDirectory(configDir);
        _filePath = Path.Combine(configDir, HistoryFileName);
        _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        Load();
    }

    /// <summary>返回该字段键的历史列表（只读），不存在则返回空列表。</summary>
    public IReadOnlyList<string> GetHistory(string fieldKey)
    {
        if (string.IsNullOrWhiteSpace(fieldKey)) return Array.Empty<string>();
        lock (_lock)
        {
            if (_data.TryGetValue(fieldKey, out var list) && list != null)
                return new ReadOnlyCollection<string>(list.ToList());
            return Array.Empty<string>();
        }
    }

    /// <summary>将非空值追加到该键的历史（去重、插到首位、超过 N 条截断），并写回文件。</summary>
    public void AddToHistory(string fieldKey, string value)
    {
        var trimmed = value?.Trim() ?? "";
        if (string.IsNullOrEmpty(trimmed) || string.IsNullOrWhiteSpace(fieldKey)) return;
        lock (_lock)
        {
            if (!_data.TryGetValue(fieldKey, out var list) || list == null)
                _data[fieldKey] = list = new List<string>();
            list.RemoveAll(s => string.Equals(s, trimmed, StringComparison.Ordinal));
            list.Insert(0, trimmed);
            if (list.Count > MaxEntriesPerKey)
                list.RemoveRange(MaxEntriesPerKey, list.Count - MaxEntriesPerKey);
            Save();
        }
    }

    /// <summary>从当前窗口收集所有带 InputHistory.Key 的 TextBox，将其当前 Text 写入历史。供保存成功后调用。</summary>
    public static void RecordFromWindow(Window window)
    {
        if (window == null) return;
        var service = GetInstance();
        foreach (var (key, text) in InputHistoryBehavior.CollectKeysAndTexts(window))
            service.AddToHistory(key, text);
    }

    private void Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_filePath))
            {
                _data = new Dictionary<string, List<string>>();
                return;
            }
            try
            {
                var yaml = File.ReadAllText(_filePath);
                var wrapper = _deserializer.Deserialize<InputHistoryFile>(yaml);
                _data = wrapper?.Items ?? new Dictionary<string, List<string>>();
                if (_data == null) _data = new Dictionary<string, List<string>>();
            }
            catch (Exception ex)
            {
                ExceptionLog.Write(ex, "加载 input_history.yaml 失败");
                _data = new Dictionary<string, List<string>>();
            }
        }
    }

    private void Save()
    {
        try
        {
            var wrapper = new InputHistoryFile { Items = _data };
            var yaml = _serializer.Serialize(wrapper);
            File.WriteAllText(_filePath, yaml);
        }
        catch (Exception ex)
        {
            ExceptionLog.Write(ex, "保存 input_history.yaml 失败");
        }
    }

    private static InputHistoryService? _instance;

    /// <summary>获取单例（在未接入 DI 时由行为/窗口调用）。</summary>
    public static InputHistoryService GetInstance()
    {
        if (_instance == null)
            _instance = new InputHistoryService();
        return _instance;
    }

    /// <summary>YAML 根结构：字段键 -> 历史字符串列表。</summary>
    private class InputHistoryFile
    {
        public Dictionary<string, List<string>> Items { get; set; } = new();
    }
}
