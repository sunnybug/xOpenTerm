using YamlDotNet.Serialization;

namespace xOpenTerm2.Models;

public class Node
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = "";

    [YamlMember(Alias = "parentId")]
    public string? ParentId { get; set; }

    [YamlMember(Alias = "type")]
    public NodeType Type { get; set; }

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "config")]
    public ConnectionConfig? Config { get; set; }

    [YamlMember(Alias = "children")]
    public List<string>? Children { get; set; }
}
