using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using xOpenTerm.Controls;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>主窗口：服务器树、节点 CRUD。实现分布在 ServerTree.Build / ContextMenu / Crud / Selection 等 partial 中。</summary>
public partial class MainWindow
{
    private void AddNode(NodeType type, string? parentId)
    {
        var node = new Node
        {
            Id = Guid.NewGuid().ToString(),
            ParentId = parentId,
            Type = type,
            Name = type == NodeType.group ? "新分组" : "新主机",
            Config = type != NodeType.group ? new ConnectionConfig() : null
        };
        _nodes.Add(node);
        _storage.SaveNodes(_nodes);
        BuildTree();
        if (type != NodeType.group)
            EditNode(node, isExistingNode: false);
    }

    private void ConnectAll(string groupId)
    {
        var leaves = GetLeafNodes(groupId);
        if (leaves.Count == 0)
        {
            MessageBox.Show("该分组下没有可连接的主机。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var groupNode = _nodes.FirstOrDefault(n => n.Id == groupId);
        var groupName = groupNode != null ? (GetNodeDisplayName(groupNode) ?? groupNode.Name ?? "未命名分组") : "分组";
        if (MessageBox.Show($"确定要连接分组「{groupName}」下的全部 {leaves.Count} 台主机吗？", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        foreach (var node in leaves)
            OpenTab(node);
    }

    private List<Node> GetLeafNodes(string parentId)
    {
        var list = new List<Node>();
        foreach (var n in _nodes.Where(n => n.ParentId == parentId))
        {
            if (n.Type == NodeType.group || n.Type == NodeType.tencentCloudGroup || n.Type == NodeType.aliCloudGroup || n.Type == NodeType.kingsoftCloudGroup)
                list.AddRange(GetLeafNodes(n.Id));
            else
                list.Add(n);
        }
        return list;
    }

    private void DeleteNodeRecursive(Node node)
    {
        var name = string.IsNullOrEmpty(node.Name) ? "未命名分组" : (GetNodeDisplayName(node) ?? node.Name);
        if (string.IsNullOrWhiteSpace(name)) name = "未命名分组";
        if (MessageBox.Show($"确定删除分组「{name}」及全部子节点？此操作不可恢复。", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        RemoveNodeRecursive(node.Id);
        _storage.SaveNodes(_nodes);
        BuildTree();
    }

    private void RemoveNodeRecursive(string nodeId)
    {
        foreach (var child in _nodes.Where(n => n.ParentId == nodeId).ToList())
            RemoveNodeRecursive(child.Id);
        _nodes.RemoveAll(n => n.Id == nodeId);
    }

    /// <summary>删除指定节点下的全部子节点（不删除该节点本身），保存并刷新树。</summary>
    private void RemoveChildrenOfNode(Node parentNode)
    {
        foreach (var child in _nodes.Where(n => n.ParentId == parentNode.Id).ToList())
            RemoveNodeRecursive(child.Id);
        _storage.SaveNodes(_nodes);
        BuildTree();
    }

    private void DeleteNode(Node node)
    {
        var name = string.IsNullOrEmpty(node.Name) && node.Config?.Host != null ? node.Config.Host : (GetNodeDisplayName(node) ?? node.Name ?? "未命名节点");
        if (string.IsNullOrWhiteSpace(name)) name = "未命名节点";
        if (MessageBox.Show($"确定删除节点「{name}」？", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _nodes.RemoveAll(n => n.Id == node.Id);
        _storage.SaveNodes(_nodes);
        BuildTree();
    }

    private void DuplicateNode(Node node)
    {
        var copy = new Node
        {
            Id = Guid.NewGuid().ToString(),
            ParentId = node.ParentId,
            Type = node.Type,
            Name = node.Name + " (副本)",
            Config = node.Config != null ? new ConnectionConfig
            {
                Host = node.Config.Host,
                Port = node.Config.Port,
                Username = node.Config.Username,
                AuthType = node.Config.AuthType,
                Password = node.Config.Password,
                KeyPath = node.Config.KeyPath,
                KeyPassphrase = node.Config.KeyPassphrase,
                Protocol = node.Config.Protocol,
                CredentialId = node.Config.CredentialId,
                AuthSource = node.Config.AuthSource,
                TunnelIds = node.Config.TunnelIds != null ? new List<string>(node.Config.TunnelIds) : null,
                TunnelId = node.Config.TunnelId,
                Domain = node.Config.Domain
            } : null
        };
        _nodes.Add(copy);
        _storage.SaveNodes(_nodes);
        BuildTree();
    }

    private void EditNode(Node node, bool isExistingNode = true)
    {
        var context = new NodeEditContext(_nodes, _credentials, _tunnels, _storage);
        NodeEditWindowBase? dlg = node.Type switch
        {
            NodeType.group => new GroupNodeEditWindow(node, context, isExistingNode),
            NodeType.tencentCloudGroup => new TencentCloudNodeEditWindow(node, context, isExistingNode),
            NodeType.aliCloudGroup => new AliCloudNodeEditWindow(node, context, isExistingNode),
            NodeType.kingsoftCloudGroup => new KingsoftCloudNodeEditWindow(node, context, isExistingNode),
            NodeType.ssh => new SshNodeEditWindow(node, context, isExistingNode),
            NodeType.rdp => new RdpNodeEditWindow(node, context, isExistingNode),
            _ => null
        };
        if (dlg == null) return;
        dlg.Owner = this;
        if (dlg.ShowDialog() == true && dlg.SavedNode != null)
        {
            var idx = _nodes.FindIndex(n => n.Id == dlg.SavedNode!.Id);
            if (idx >= 0) _nodes[idx] = dlg.SavedNode;
            else _nodes.Add(dlg.SavedNode);
            _storage.SaveNodes(_nodes);
            // 局部更新节点，避免重刷整个树
            UpdateTreeItem(node);
        }
    }

    /// <summary>局部更新树中指定节点的显示，避免重刷整个树。</summary>
    private void UpdateTreeItem(Node node)
    {
        if (!_nodeIdToTvi.TryGetValue(node.Id, out var tvi))
            tvi = FindTreeViewItemByNodeId(ServerTree, node.Id);
        if (tvi == null) return;

        // 更新 Tag 为最新的节点引用
        tvi.Tag = node;

        // 先清除 Header，强制清理视觉树
        tvi.Header = null;

        // 重新构建 Header
        var expand = tvi.IsExpanded;
        var textPrimary = (Brush)FindResource("TextPrimary");
        var textSecondary = (Brush)FindResource("TextSecondary");
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        var iconBlock = new TextBlock
        {
            Text = ServerTreeItemBuilder.NodeIcon(node, isGroupExpanded: expand),
            Margin = new Thickness(0, 0, 6, 0),
            Foreground = ServerTreeItemBuilder.NodeColor(node)
        };
        header.Children.Add(iconBlock);
        var displayName = GetNodeDisplayName(node);
        header.Children.Add(new TextBlock
        {
            Text = displayName ?? "",
            Foreground = textPrimary,
            VerticalAlignment = VerticalAlignment.Center
        });
        if (node.Type == NodeType.ssh && !string.IsNullOrEmpty(node.Config?.Host))
        {
            header.Children.Add(new TextBlock
            {
                Text = " " + node.Config.Host,
                Foreground = textSecondary,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        tvi.Header = header;

        // 对于分组节点，重新绑定展开/折叠事件以更新图标
        if (node.Type == NodeType.group || node.Type == NodeType.tencentCloudGroup || node.Type == NodeType.aliCloudGroup || node.Type == NodeType.kingsoftCloudGroup)
        {
            void UpdateGroupIcon()
            {
                iconBlock.Text = ServerTreeItemBuilder.NodeIcon(node, tvi.IsExpanded);
            }
            tvi.Expanded += (_, _) => UpdateGroupIcon();
            tvi.Collapsed += (_, _) => UpdateGroupIcon();
        }
    }

    private void OpenGroupEdit(Node groupNode)
    {
        var context = new NodeEditContext(_nodes, _credentials, _tunnels, _storage);
        var dlg = new GroupEditWindow(groupNode, context) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            var idx = _nodes.FindIndex(n => n.Id == groupNode.Id);
            if (idx >= 0) _nodes[idx] = groupNode;
            _storage.SaveNodes(_nodes);
            // 局部更新节点，避免重刷整个树
            UpdateTreeItem(groupNode);
        }
    }

    private void ImportMobaXterm(Node? parentNode)
    {
        var settings = _storage.LoadAppSettings();
        var dlg = new ImportMobaXtermWindow(this, settings.LastMobaXtermIniPath, settings.LastMobaXtermPasswordPath);
        if (dlg.ShowDialog() != true || dlg.SelectedSessions.Count == 0) return;
        settings.LastMobaXtermIniPath = string.IsNullOrWhiteSpace(dlg.LastUsedIniPath) ? null : dlg.LastUsedIniPath;
        settings.LastMobaXtermPasswordPath = string.IsNullOrWhiteSpace(dlg.LastUsedPasswordPath) ? null : dlg.LastUsedPasswordPath;
        _storage.SaveAppSettings(settings);
        var passwordLookup = dlg.PasswordLookup;
        // 按目录结构创建父节点：path -> 已创建的分组 Node；根级别时 parentNode 为 null
        var rootParentId = parentNode?.Id;
        var pathToGroupId = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { [""] = rootParentId };
        foreach (var item in dlg.SelectedSessions.OrderBy(s => s.FolderPath ?? ""))
        {
            var path = item.FolderPath ?? "";
            var parts = string.IsNullOrEmpty(path) ? Array.Empty<string>() : path.Split(new[] { " / " }, StringSplitOptions.None);
            var currentParentId = rootParentId;
            var currentPath = "";
            foreach (var segment in parts)
            {
                var name = segment.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                currentPath = string.IsNullOrEmpty(currentPath) ? name : currentPath + " / " + name;
                if (!pathToGroupId.TryGetValue(currentPath, out var groupId))
                {
                    var group = new Node
                    {
                        Id = Guid.NewGuid().ToString(),
                        ParentId = currentParentId,
                        Type = NodeType.group,
                        Name = name,
                        Config = null
                    };
                    _nodes.Add(group);
                    pathToGroupId[currentPath] = group.Id;
                    groupId = group.Id;
                }
                currentParentId = groupId;
            }
            var sessionNode = item.ToNode(currentParentId);
            // 若提供了密码文件且为密码类型节点且用户名为 [配置名]，从密码文件填充用户名和密码
            if (passwordLookup != null && sessionNode.Config != null)
            {
                var isPasswordType = item.IsRdp || string.IsNullOrEmpty(item.KeyPath);
                var un = item.Username?.Trim() ?? "";
                if (isPasswordType && un.StartsWith("[", StringComparison.Ordinal) && un.Contains("]"))
                {
                    var key = un[1..un.IndexOf(']')].Trim();
                    if (!string.IsNullOrEmpty(key) && passwordLookup.TryGetValue(key, out var cred))
                    {
                        sessionNode.Config.Username = cred.Username;
                        sessionNode.Config.Password = cred.Password;
                    }
                }
            }
            // SSH 节点：有用户名（非 [xxx] 包裹）、无密码、无密钥时，导入后转为 SSH Agent
            if (!item.IsRdp && sessionNode.Config != null)
            {
                var un = sessionNode.Config.Username?.Trim() ?? "";
                var hasRealUsername = !string.IsNullOrEmpty(un) && !(un.StartsWith("[", StringComparison.Ordinal) && un.Contains("]"));
                var noPassword = string.IsNullOrEmpty(sessionNode.Config.Password);
                var noKey = string.IsNullOrEmpty(sessionNode.Config.KeyPath);
                if (hasRealUsername && noPassword && noKey)
                {
                    sessionNode.Config.AuthSource = AuthSource.agent;
                    sessionNode.Config.AuthType = AuthType.agent;
                }
            }
            _nodes.Add(sessionNode);
        }
        _storage.SaveNodes(_nodes);
        BuildTree(expandNodes: false); // 导入后不自动展开节点
    }

    /// <summary>收集节点树中所有被引用的凭证 ID（Config 与 Tunnel 跳转）。</summary>
    private static HashSet<string> CollectReferencedCredentialIds(IEnumerable<Node> nodes)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in nodes)
        {
            var c = n.Config;
            if (c == null) continue;
            if (!string.IsNullOrEmpty(c.CredentialId)) ids.Add(c.CredentialId);
            if (!string.IsNullOrEmpty(c.SshCredentialId)) ids.Add(c.SshCredentialId);
            if (!string.IsNullOrEmpty(c.RdpCredentialId)) ids.Add(c.RdpCredentialId);
            if (c.Tunnel != null)
            {
                foreach (var h in c.Tunnel)
                {
                    if (!string.IsNullOrEmpty(h.CredentialId)) ids.Add(h.CredentialId);
                }
            }
        }
        return ids;
    }

    private void ExportYaml()
    {
        var refIds = CollectReferencedCredentialIds(_nodes);
        var credentials = _credentials.Where(c => refIds.Contains(c.Id)).ToList();
        var data = new ExportYamlRoot
        {
            Version = 1,
            Nodes = _nodes,
            Credentials = credentials
        };
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出 YAML",
            Filter = "YAML 文件 (*.yaml)|*.yaml|所有文件 (*.*)|*.*",
            DefaultExt = "yaml",
            FileName = $"nodes-export-{DateTime.Now:MMyy-HHmm}.yaml"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var yaml = _storage.SerializeExport(data);
            File.WriteAllText(dlg.FileName, yaml);
            MessageBox.Show($"已导出 {_nodes.Count} 个节点、{credentials.Count} 个凭证。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportYaml(Node? parentNode)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入 YAML",
            Filter = "YAML 文件 (*.yaml)|*.yaml|所有文件 (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true || string.IsNullOrEmpty(dlg.FileName)) return;
        try
        {
            var yaml = File.ReadAllText(dlg.FileName);
            var data = _storage.DeserializeExport(yaml);
            if (data?.Nodes == null || data.Nodes.Count == 0)
            {
                MessageBox.Show("文件中没有节点数据。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var importedCreds = data.Credentials ?? new List<Credential>();
            var credIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var imp in importedCreds)
            {
                if (string.IsNullOrEmpty(imp.Id)) continue;
                var existing = _credentials.FirstOrDefault(c => CredentialContentEquals(c, imp));
                if (existing != null)
                {
                    credIdMap[imp.Id] = existing.Id;
                    continue;
                }
                var oldCredId = imp.Id;
                imp.Id = Guid.NewGuid().ToString();
                _credentials.Add(imp);
                credIdMap[oldCredId] = imp.Id;
            }
            string? parentIdForRoots = parentNode?.Id;
            // 云组祖先下不允许导入云组节点：过滤掉腾讯云组/阿里云组，并将其子节点挂到有效祖先下
            var underCloudGroup = parentNode != null && HasAncestorOrSelfCloudGroup(parentNode);
            var nodesToAdd = underCloudGroup
                ? data.Nodes.Where(n => n.Type != NodeType.tencentCloudGroup && n.Type != NodeType.aliCloudGroup && n.Type != NodeType.kingsoftCloudGroup).ToList()
                : data.Nodes;
            var removedCloudGroupIds = underCloudGroup
                ? data.Nodes.Where(n => n.Type == NodeType.tencentCloudGroup || n.Type == NodeType.aliCloudGroup || n.Type == NodeType.kingsoftCloudGroup).Select(n => n.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var importedById = data.Nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);

            var nodeIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in nodesToAdd)
                nodeIdMap[n.Id] = Guid.NewGuid().ToString();

            foreach (var n in nodesToAdd)
            {
                var oldParentId = n.ParentId;
                var effectiveOldParentId = oldParentId;
                while (!string.IsNullOrEmpty(effectiveOldParentId) && removedCloudGroupIds.Contains(effectiveOldParentId)
                    && importedById.TryGetValue(effectiveOldParentId, out var p))
                    effectiveOldParentId = p.ParentId;
                n.Id = nodeIdMap[n.Id];
                n.ParentId = string.IsNullOrEmpty(effectiveOldParentId) ? parentIdForRoots : nodeIdMap[effectiveOldParentId];
                RemapCredentialIdsInNode(n, credIdMap);
                _nodes.Add(n);
            }
            _storage.SaveNodes(_nodes);
            _storage.SaveCredentials(_credentials);
            BuildTree(expandNodes: false);
            var skipMsg = removedCloudGroupIds.Count > 0 ? $"，已跳过 {removedCloudGroupIds.Count} 个云组节点（云组下不允许嵌套云组）" : "";
            MessageBox.Show($"已导入 {nodesToAdd.Count} 个节点{skipMsg}；凭证：相同已忽略，不同已新增。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导入失败：{ex.Message}", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool CredentialContentEquals(Credential a, Credential b)
    {
        if (a.Name != b.Name || a.Username != b.Username || a.AuthType != b.AuthType) return false;
        if ((a.Password ?? "") != (b.Password ?? "")) return false;
        if ((a.KeyPath ?? "") != (b.KeyPath ?? "")) return false;
        if ((a.KeyPassphrase ?? "") != (b.KeyPassphrase ?? "")) return false;
        if ((a.AgentForwarding ?? false) != (b.AgentForwarding ?? false)) return false;
        if ((a.Tunnel == null) != (b.Tunnel == null)) return false;
        if (a.Tunnel != null && b.Tunnel != null)
        {
            if (a.Tunnel.Count != b.Tunnel.Count) return false;
            for (var i = 0; i < a.Tunnel.Count; i++)
            {
                var ha = a.Tunnel[i];
                var hb = b.Tunnel[i];
                if (ha.Host != hb.Host || ha.Username != hb.Username || ha.AuthType != hb.AuthType) return false;
                if ((ha.Password ?? "") != (hb.Password ?? "")) return false;
                if ((ha.KeyPath ?? "") != (hb.KeyPath ?? "")) return false;
                if ((ha.KeyPassphrase ?? "") != (hb.KeyPassphrase ?? "")) return false;
            }
        }
        return true;
    }

    private static void RemapCredentialIdsInNode(Node node, Dictionary<string, string> credIdMap)
    {
        var c = node.Config;
        if (c == null) return;
        if (!string.IsNullOrEmpty(c.CredentialId) && credIdMap.TryGetValue(c.CredentialId, out var id1)) c.CredentialId = id1;
        if (!string.IsNullOrEmpty(c.SshCredentialId) && credIdMap.TryGetValue(c.SshCredentialId, out var id2)) c.SshCredentialId = id2;
        if (!string.IsNullOrEmpty(c.RdpCredentialId) && credIdMap.TryGetValue(c.RdpCredentialId, out var id3)) c.RdpCredentialId = id3;
        if (c.Tunnel != null)
        {
            foreach (var h in c.Tunnel)
            {
                if (!string.IsNullOrEmpty(h.CredentialId) && credIdMap.TryGetValue(h.CredentialId, out var id4)) h.CredentialId = id4;
            }
        }
    }

    private void AddTencentCloudGroup(string? parentId)
    {
        if (!string.IsNullOrEmpty(parentId))
        {
            var parent = _nodes.FirstOrDefault(n => n.Id == parentId);
            if (parent != null && HasAncestorOrSelfCloudGroup(parent))
            {
                MessageBox.Show("云组下不允许再嵌套云组，请在其他分组下新建。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        var dlg = new TencentCloudGroupAddWindow { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var groupNode = new Node
        {
            Id = Guid.NewGuid().ToString(),
            ParentId = parentId,
            Type = NodeType.tencentCloudGroup,
            Name = string.IsNullOrWhiteSpace(dlg.GroupName) ? "腾讯云" : dlg.GroupName.Trim(),
            Config = new ConnectionConfig
            {
                TencentSecretId = dlg.SecretId,
                TencentSecretKey = dlg.SecretKey
            }
        };
        _nodes.Add(groupNode);
        _storage.SaveNodes(_nodes);
        BuildTree();

        // 复用与右键「同步」完全相同的流程，避免新建组流程中的线程/时序差异导致跨线程访问 UI
        SyncTencentCloudGroup(groupNode);
    }

    private void AddAliCloudGroup(string? parentId)
    {
        if (!string.IsNullOrEmpty(parentId))
        {
            var parent = _nodes.FirstOrDefault(n => n.Id == parentId);
            if (parent != null && HasAncestorOrSelfCloudGroup(parent))
            {
                MessageBox.Show("云组下不允许再嵌套云组，请在其他分组下新建。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        var dlg = new AliCloudGroupAddWindow { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var groupNode = new Node
        {
            Id = Guid.NewGuid().ToString(),
            ParentId = parentId,
            Type = NodeType.aliCloudGroup,
            Name = string.IsNullOrWhiteSpace(dlg.GroupName) ? "阿里云" : dlg.GroupName.Trim(),
            Config = new ConnectionConfig
            {
                AliAccessKeyId = dlg.AccessKeyId,
                AliAccessKeySecret = dlg.AccessKeySecret
            }
        };
        _nodes.Add(groupNode);
        _storage.SaveNodes(_nodes);
        BuildTree();
        SyncAliCloudGroup(groupNode);
    }

    private void AddKingsoftCloudGroup(string? parentId)
    {
        if (!string.IsNullOrEmpty(parentId))
        {
            var parent = _nodes.FirstOrDefault(n => n.Id == parentId);
            if (parent != null && HasAncestorOrSelfCloudGroup(parent))
            {
                MessageBox.Show("云组下不允许再嵌套云组，请在其他分组下新建。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        var dlg = new KingsoftCloudGroupAddWindow { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var groupNode = new Node
        {
            Id = Guid.NewGuid().ToString(),
            ParentId = parentId,
            Type = NodeType.kingsoftCloudGroup,
            Name = string.IsNullOrWhiteSpace(dlg.GroupName) ? "金山云" : dlg.GroupName.Trim(),
            Config = new ConnectionConfig
            {
                KsyunAccessKeyId = dlg.AccessKeyId,
                KsyunAccessKeySecret = dlg.AccessKeySecret
            }
        };
        _nodes.Add(groupNode);
        _storage.SaveNodes(_nodes);
        BuildTree();
        SyncKingsoftCloudGroup(groupNode);
    }

    /// <summary>根据阿里云 ECS/轻量 实例列表构建 地域→服务器 节点树。</summary>
    private static List<Node> BuildAliCloudSubtree(string rootId, List<AliEcsInstance> instances)
    {
        var result = new List<Node>();
        var regionIdToNode = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);

        foreach (var ins in instances.OrderBy(i => i.RegionId).ThenBy(i => i.InstanceName))
        {
            if (string.IsNullOrEmpty(ins.InstanceId)) continue;

            if (!regionIdToNode.TryGetValue(ins.RegionId ?? "", out var regionNode))
            {
                regionNode = new Node
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = rootId,
                    Type = NodeType.group,
                    Name = ins.RegionName ?? ins.RegionId ?? "",
                    Config = null
                };
                result.Add(regionNode);
                regionIdToNode[ins.RegionId ?? ""] = regionNode;
            }

            var host = ins.PublicIp ?? ins.PrivateIp ?? "";
            if (string.IsNullOrEmpty(host)) continue;

            var serverNode = new Node
            {
                Id = Guid.NewGuid().ToString(),
                ParentId = regionNode.Id,
                Type = ins.IsWindows ? NodeType.rdp : NodeType.ssh,
                Name = string.IsNullOrEmpty(ins.InstanceName) ? ins.InstanceId : ins.InstanceName,
                Config = new ConnectionConfig
                {
                    Host = host,
                    Port = (ushort)(ins.IsWindows ? 3389 : 22),
                    ResourceId = ins.InstanceId,
                    AuthSource = AuthSource.parent
                }
            };
            result.Add(serverNode);
        }
        return result;
    }

    /// <summary>根据金山云 KEC 实例列表构建 地域→服务器 节点树。操作系统类型由 API 的 Image.Platform / OsName 判定（Windows 为 RDP，否则为 SSH）。</summary>
    private static List<Node> BuildKingsoftCloudSubtree(string rootId, List<KsyunKecInstance> instances)
    {
        var result = new List<Node>();
        var regionIdToNode = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);

        foreach (var ins in instances.OrderBy(i => i.RegionId).ThenBy(i => i.InstanceName))
        {
            if (string.IsNullOrEmpty(ins.InstanceId)) continue;

            if (!regionIdToNode.TryGetValue(ins.RegionId ?? "", out var regionNode))
            {
                regionNode = new Node
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = rootId,
                    Type = NodeType.group,
                    Name = ins.RegionName ?? ins.RegionId ?? "",
                    Config = null
                };
                result.Add(regionNode);
                regionIdToNode[ins.RegionId ?? ""] = regionNode;
            }

            var host = ins.PublicIp ?? ins.PrivateIp ?? "";
            if (string.IsNullOrEmpty(host)) continue;

            bool useRdp = ins.IsWindows;

            var serverNode = new Node
            {
                Id = Guid.NewGuid().ToString(),
                ParentId = regionNode.Id,
                Type = useRdp ? NodeType.rdp : NodeType.ssh,
                Name = string.IsNullOrEmpty(ins.InstanceName) ? ins.InstanceId : ins.InstanceName,
                Config = new ConnectionConfig
                {
                    Host = host,
                    Port = (ushort)(useRdp ? 3389 : 22),
                    ResourceId = ins.InstanceId,
                    AuthSource = AuthSource.parent
                }
            };
            result.Add(serverNode);
        }
        return result;
    }

    private void SyncAliCloudGroup(Node groupNode)
    {
        if (groupNode.Config?.AliAccessKeyId == null || groupNode.Config?.AliAccessKeySecret == null)
        {
            MessageBox.Show("该阿里云组未配置密钥，请先在「设置」中保存 AccessKeyId/AccessKeySecret。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var accessKeyId = groupNode.Config.AliAccessKeyId;
        var accessKeySecret = groupNode.Config.AliAccessKeySecret ?? "";
        var cts = new CancellationTokenSource();
        var syncWin = new CloudSyncProgressWindow(cts.Cancel) { Owner = this, Title = "阿里云同步" };
        syncWin.Show();
        syncWin.ReportProgress("正在拉取实例列表…", 0, 1);

        var progress = new Progress<(string message, int current, int total)>(p =>
        {
            syncWin.Dispatcher.Invoke(() => syncWin.ReportProgress(p.message, p.current, p.total));
        });

        List<AliEcsInstance>? instances = null;
        Exception? runEx = null;
        var t = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                return AliCloudService.ListAllInstances(accessKeyId, accessKeySecret, progress, cts.Token);
            }
            catch (Exception ex)
            {
                runEx = ex;
                return null;
            }
        });

        while (!t.IsCompleted && syncWin.IsLoaded && syncWin.IsVisible)
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                System.Windows.Threading.DispatcherPriority.Background,
                () => { });
            Thread.Sleep(50);
        }

        if (runEx != null || t.Result == null)
        {
            if (runEx != null)
                ExceptionLog.Write(runEx, "阿里云同步失败", toCrashLog: false);
            var errMsg = runEx != null ? GetFullExceptionMessage(runEx) : "拉取失败或已取消";
            syncWin.ReportResult(errMsg, false);
            return;
        }
        instances = t.Result;
        var cloudInstanceIds = instances.Where(i => !string.IsNullOrEmpty(i.InstanceId)).Select(i => i.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var serverNodesUnderGroup = new List<Node>();
        void CollectServerNodes(string parentId)
        {
            foreach (var n in _nodes.Where(x => x.ParentId == parentId))
            {
                if (n.Type == NodeType.ssh || n.Type == NodeType.rdp)
                {
                    if (!string.IsNullOrEmpty(n.Config?.ResourceId))
                        serverNodesUnderGroup.Add(n);
                }
                else if (n.Type == NodeType.group || n.Type == NodeType.tencentCloudGroup || n.Type == NodeType.aliCloudGroup || n.Type == NodeType.kingsoftCloudGroup)
                    CollectServerNodes(n.Id);
            }
        }
        CollectServerNodes(groupNode.Id);

        var toRemove = serverNodesUnderGroup.Where(n => !cloudInstanceIds.Contains(n.Config!.ResourceId!)).ToList();
        var removedDetail = new List<string>();
        if (toRemove.Count > 0)
        {
            if (MessageBox.Show($"云上已不存在以下 {toRemove.Count} 个实例，是否从本地树中删除？\n\n此操作不可恢复。", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                foreach (var n in toRemove)
                {
                    removedDetail.Add(n.Name ?? n.Config?.ResourceId ?? n.Id);
                    _nodes.RemoveAll(x => x.Id == n.Id);
                }
            }
            else
                toRemove = new List<Node>();
        }

        var instanceMap = instances.Where(i => !string.IsNullOrEmpty(i.InstanceId)).ToDictionary(i => i.InstanceId!, StringComparer.OrdinalIgnoreCase);
        var updatedDetail = new List<string>();
        foreach (var n in serverNodesUnderGroup.Where(n => _nodes.Any(x => x.Id == n.Id)))
        {
            var rid = n.Config?.ResourceId;
            if (string.IsNullOrEmpty(rid)) continue;
            if (instanceMap.TryGetValue(rid, out var aliIns))
            {
                n.Config!.CloudRegionId = aliIns.RegionId;
                n.Config.CloudIsLightweight = aliIns.IsLightweight;
                var newHost = aliIns.PublicIp ?? aliIns.PrivateIp ?? "";
                if (!string.IsNullOrEmpty(newHost) && n.Config.Host != newHost)
                {
                    var oldHost = n.Config.Host ?? "";
                    n.Config.Host = newHost;
                    updatedDetail.Add($"{n.Name ?? rid}: {oldHost} → {newHost}");
                }
            }
        }

        var existingRegionNodes = _nodes.Where(x => x.ParentId == groupNode.Id && x.Type == NodeType.group).ToList();
        var regionByKey = existingRegionNodes.ToDictionary(n => n.Name ?? "", StringComparer.OrdinalIgnoreCase);
        var existingIds = CollectResourceIdsUnderGroup(groupNode.Id);
        var addedCount = 0;
        var addedDetail = new List<string>();

        foreach (var ins in instances)
        {
            if (string.IsNullOrEmpty(ins.InstanceId) || existingIds.Contains(ins.InstanceId)) continue;
            existingIds.Add(ins.InstanceId);
            addedCount++;
            addedDetail.Add(string.IsNullOrEmpty(ins.InstanceName) ? ins.InstanceId! : $"{ins.InstanceName} ({ins.InstanceId})");

            var regionName = ins.RegionName ?? ins.RegionId ?? "";
            if (!regionByKey.TryGetValue(regionName, out var regionNode))
            {
                regionNode = new Node
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = groupNode.Id,
                    Type = NodeType.group,
                    Name = regionName,
                    Config = null
                };
                _nodes.Add(regionNode);
                regionByKey[regionName] = regionNode;
            }

            var host = ins.PublicIp ?? ins.PrivateIp ?? "";
            if (string.IsNullOrEmpty(host)) continue;

            var serverNode = new Node
            {
                Id = Guid.NewGuid().ToString(),
                ParentId = regionNode.Id,
                Type = ins.IsWindows ? NodeType.rdp : NodeType.ssh,
                Name = string.IsNullOrEmpty(ins.InstanceName) ? ins.InstanceId : ins.InstanceName,
                Config = new ConnectionConfig
                {
                    Host = host,
                    Port = (ushort)(ins.IsWindows ? 3389 : 22),
                    ResourceId = ins.InstanceId,
                    AuthSource = AuthSource.parent,
                    CloudRegionId = ins.RegionId,
                    CloudIsLightweight = ins.IsLightweight
                }
            };
            _nodes.Add(serverNode);
        }

        _storage.SaveNodes(_nodes);
        var detailLines = new List<string>();
        foreach (var s in removedDetail)
            detailLines.Add("[删除] " + s);
        foreach (var s in updatedDetail)
            detailLines.Add("[更新IP] " + s);
        foreach (var s in addedDetail)
            detailLines.Add("[新增] " + s);
        var summary = $"已删除 {removedDetail.Count} 个本地节点，更新 {updatedDetail.Count} 台 IP，新增 {addedCount} 台实例（共 {instances.Count} 台）。";
        syncWin.ReportResult(summary, true, detailLines.Count > 0 ? detailLines : null);
        _selectedNodeIds.Clear();
        _selectedNodeIds.Add(groupNode.Id);
        BuildTree(expandNodes: false);
    }

    private void SyncKingsoftCloudGroup(Node groupNode)
    {
        if (groupNode.Config?.KsyunAccessKeyId == null || groupNode.Config?.KsyunAccessKeySecret == null)
        {
            MessageBox.Show("该金山云组未配置密钥，请先在「设置」中保存 AccessKeyId/AccessKeySecret。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var accessKeyId = groupNode.Config.KsyunAccessKeyId;
        var accessKeySecret = groupNode.Config.KsyunAccessKeySecret ?? "";
        var cts = new CancellationTokenSource();
        var syncWin = new CloudSyncProgressWindow(cts.Cancel) { Owner = this, Title = "金山云同步" };
        syncWin.Show();
        syncWin.ReportProgress("正在拉取实例列表…", 0, 1);

        var progress = new Progress<(string message, int current, int total)>(p =>
        {
            syncWin.Dispatcher.Invoke(() => syncWin.ReportProgress(p.message, p.current, p.total));
        });

        List<KsyunKecInstance>? instances = null;
        Exception? runEx = null;
        var t = Task.Run(() =>
        {
            try
            {
                return KingsoftCloudService.ListInstances(accessKeyId, accessKeySecret, progress, cts.Token);
            }
            catch (Exception ex)
            {
                runEx = ex;
                return null;
            }
        });

        while (!t.IsCompleted && syncWin.IsLoaded && syncWin.IsVisible)
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                System.Windows.Threading.DispatcherPriority.Background,
                () => { });
            Thread.Sleep(50);
        }

        if (runEx != null || t.Result == null)
        {
            if (runEx != null)
                ExceptionLog.Write(runEx, "金山云同步失败", toCrashLog: false);
            var errMsg = runEx != null ? GetFullExceptionMessage(runEx) : "拉取失败或已取消";
            syncWin.ReportResult(errMsg, false);
            return;
        }
        instances = t.Result;
        var cloudInstanceIds = instances.Where(i => !string.IsNullOrEmpty(i.InstanceId)).Select(i => i.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var serverNodesUnderGroup = new List<Node>();
        void CollectServerNodes(string parentId)
        {
            foreach (var n in _nodes.Where(x => x.ParentId == parentId))
            {
                if (n.Type == NodeType.ssh || n.Type == NodeType.rdp)
                {
                    if (!string.IsNullOrEmpty(n.Config?.ResourceId))
                        serverNodesUnderGroup.Add(n);
                }
                else if (n.Type == NodeType.group || n.Type == NodeType.tencentCloudGroup || n.Type == NodeType.aliCloudGroup || n.Type == NodeType.kingsoftCloudGroup)
                    CollectServerNodes(n.Id);
            }
        }
        CollectServerNodes(groupNode.Id);

        var toRemove = serverNodesUnderGroup.Where(n => !cloudInstanceIds.Contains(n.Config!.ResourceId!)).ToList();
        var removedDetail = new List<string>();
        if (toRemove.Count > 0)
        {
            if (MessageBox.Show($"云上已不存在以下 {toRemove.Count} 个实例，是否从本地树中删除？\n\n此操作不可恢复。", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                foreach (var n in toRemove)
                {
                    removedDetail.Add(n.Name ?? n.Config?.ResourceId ?? n.Id);
                    _nodes.RemoveAll(x => x.Id == n.Id);
                }
            }
            else
                toRemove = new List<Node>();
        }

        var instanceMap = instances.Where(i => !string.IsNullOrEmpty(i.InstanceId)).ToDictionary(i => i.InstanceId!, StringComparer.OrdinalIgnoreCase);
        var updatedDetail = new List<string>();
        foreach (var n in serverNodesUnderGroup.Where(n => _nodes.Any(x => x.Id == n.Id)))
        {
            var rid = n.Config?.ResourceId;
            if (string.IsNullOrEmpty(rid)) continue;
            if (instanceMap.TryGetValue(rid, out var ksIns))
            {
                n.Config!.CloudRegionId = ksIns.RegionId;
                n.Config.CloudIsLightweight = false;
                var newHost = ksIns.PublicIp ?? ksIns.PrivateIp ?? "";
                if (!string.IsNullOrEmpty(newHost) && n.Config.Host != newHost)
                {
                    var oldHost = n.Config.Host ?? "";
                    n.Config.Host = newHost;
                    updatedDetail.Add($"{n.Name ?? rid}: {oldHost} → {newHost}");
                }
            }
        }

        var existingRegionNodes = _nodes.Where(x => x.ParentId == groupNode.Id && x.Type == NodeType.group).ToList();
        var regionByKey = existingRegionNodes.ToDictionary(n => n.Name ?? "", StringComparer.OrdinalIgnoreCase);
        var existingIds = CollectResourceIdsUnderGroup(groupNode.Id);
        var addedCount = 0;
        var addedDetail = new List<string>();

        foreach (var ins in instances)
        {
            if (string.IsNullOrEmpty(ins.InstanceId) || existingIds.Contains(ins.InstanceId)) continue;
            existingIds.Add(ins.InstanceId);
            addedCount++;
            addedDetail.Add(string.IsNullOrEmpty(ins.InstanceName) ? ins.InstanceId! : $"{ins.InstanceName} ({ins.InstanceId})");

            var regionName = ins.RegionName ?? ins.RegionId ?? "";
            if (!regionByKey.TryGetValue(regionName, out var regionNode))
            {
                regionNode = new Node
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = groupNode.Id,
                    Type = NodeType.group,
                    Name = regionName,
                    Config = null
                };
                _nodes.Add(regionNode);
                regionByKey[regionName] = regionNode;
            }

            var host = ins.PublicIp ?? ins.PrivateIp ?? "";
            if (string.IsNullOrEmpty(host)) continue;

            var serverNode = new Node
            {
                Id = Guid.NewGuid().ToString(),
                ParentId = regionNode.Id,
                Type = ins.IsWindows ? NodeType.rdp : NodeType.ssh,
                Name = string.IsNullOrEmpty(ins.InstanceName) ? ins.InstanceId : ins.InstanceName,
                Config = new ConnectionConfig
                {
                    Host = host,
                    Port = (ushort)(ins.IsWindows ? 3389 : 22),
                    ResourceId = ins.InstanceId,
                    AuthSource = AuthSource.parent,
                    CloudRegionId = ins.RegionId,
                    CloudIsLightweight = false
                }
            };
            _nodes.Add(serverNode);
        }

        _storage.SaveNodes(_nodes);
        var detailLines = new List<string>();
        foreach (var s in removedDetail)
            detailLines.Add("[删除] " + s);
        foreach (var s in updatedDetail)
            detailLines.Add("[更新IP] " + s);
        foreach (var s in addedDetail)
            detailLines.Add("[新增] " + s);
        var summary = $"已删除 {removedDetail.Count} 个本地节点，更新 {updatedDetail.Count} 台 IP，新增 {addedCount} 台实例（共 {instances.Count} 台）。";
        syncWin.ReportResult(summary, true, detailLines.Count > 0 ? detailLines : null);
        _selectedNodeIds.Clear();
        _selectedNodeIds.Add(groupNode.Id);
        BuildTree(expandNodes: false);
    }

    /// <summary>根据腾讯云实例列表构建 机房→项目→服务器 节点树，所有服务器节点默认同父节点凭证。</summary>
    private static List<Node> BuildTencentCloudSubtree(string rootId, List<TencentCvmInstance> instances)
    {
        var result = new List<Node>();
        var regionIdToNode = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        var projectKeyToNode = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);

        foreach (var ins in instances.OrderBy(i => i.Region).ThenBy(i => i.ProjectId).ThenBy(i => i.InstanceName))
        {
            if (string.IsNullOrEmpty(ins.InstanceId)) continue;

            if (!regionIdToNode.TryGetValue(ins.Region ?? "", out var regionNode))
            {
                regionNode = new Node
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = rootId,
                    Type = NodeType.group,
                    Name = ins.RegionName ?? ins.Region ?? "",
                    Config = null
                };
                result.Add(regionNode);
                regionIdToNode[ins.Region ?? ""] = regionNode;
            }

            var projectKey = (ins.Region ?? "") + ":" + ins.ProjectId;
            if (!projectKeyToNode.TryGetValue(projectKey, out var projectNode))
            {
                var projectDisplayName = !string.IsNullOrWhiteSpace(ins.ProjectName)
                    ? $"{ins.ProjectName} ({ins.ProjectId})"
                    : "项目 " + ins.ProjectId;
                projectNode = new Node
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = regionNode.Id,
                    Type = NodeType.group,
                    Name = projectDisplayName,
                    Config = null
                };
                result.Add(projectNode);
                projectKeyToNode[projectKey] = projectNode;
            }

            var host = ins.PublicIp ?? ins.PrivateIp ?? "";
            if (string.IsNullOrEmpty(host)) continue;

            var serverNode = new Node
            {
                Id = Guid.NewGuid().ToString(),
                ParentId = projectNode.Id,
                Type = ins.IsWindows ? NodeType.rdp : NodeType.ssh,
                Name = string.IsNullOrEmpty(ins.InstanceName) ? ins.InstanceId : ins.InstanceName,
                Config = new ConnectionConfig
                {
                    Host = host,
                    Port = (ushort)(ins.IsWindows ? 3389 : 22),
                    ResourceId = ins.InstanceId,
                    AuthSource = AuthSource.parent
                }
            };
            result.Add(serverNode);
        }
        return result;
    }

    /// <summary>云组重建时，将父节点（云组）的默认登录凭证设为「同父节点」。</summary>
    private static void SetGroupCredentialDefaultsToParent(Node groupNode)
    {
        if (groupNode.Config == null)
            groupNode.Config = new ConnectionConfig();
        groupNode.Config.SshCredentialId = null;
        groupNode.Config.RdpCredentialId = null;
        groupNode.Config.CredentialId = null;
        groupNode.Config.AuthSource = null;
    }

    private void RebuildTencentCloudGroup(Node groupNode)
    {
        if (MessageBox.Show("重建将删除该云组下所有节点后再从云端同步，是否继续？", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        RemoveChildrenOfNode(groupNode);
        SetGroupCredentialDefaultsToParent(groupNode);
        SyncTencentCloudGroup(groupNode);
    }

    private void RebuildAliCloudGroup(Node groupNode)
    {
        if (MessageBox.Show("重建将删除该云组下所有节点后再从云端同步，是否继续？", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        RemoveChildrenOfNode(groupNode);
        SetGroupCredentialDefaultsToParent(groupNode);
        SyncAliCloudGroup(groupNode);
    }

    private void RebuildKingsoftCloudGroup(Node groupNode)
    {
        if (MessageBox.Show("重建将删除该云组下所有节点后再从云端同步，是否继续？", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        RemoveChildrenOfNode(groupNode);
        SetGroupCredentialDefaultsToParent(groupNode);
        SyncKingsoftCloudGroup(groupNode);
    }

    private void SyncTencentCloudGroup(Node groupNode)
    {
        if (groupNode.Config?.TencentSecretId == null || groupNode.Config?.TencentSecretKey == null)
        {
            MessageBox.Show("该腾讯云组未配置密钥，请先在「设置」中保存 SecretId/SecretKey。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var secretId = groupNode.Config.TencentSecretId;
        var secretKey = groupNode.Config.TencentSecretKey ?? "";
        var cts = new CancellationTokenSource();
        var syncWin = new CloudSyncProgressWindow(cts.Cancel) { Owner = this, Title = "腾讯云同步" };
        syncWin.Show();
        syncWin.ReportProgress("正在拉取 CVM/轻量（并行）…", 0, 1);

        // 进度回调统一同步封送到同步窗口的 UI 线程；CVM 与轻量并行拉取时合并两路进度
        var totalCvm = 0;
        var totalLighthouse = 0;
        var completedCvm = 0;
        var completedLighthouse = 0;
        var progressLock = new object();
        var progressCvm = new Progress<(string message, int current, int total)>(p =>
        {
            lock (progressLock)
            {
                totalCvm = p.total;
                completedCvm = p.current;
                var total = totalCvm + totalLighthouse;
                var current = completedCvm + completedLighthouse;
                syncWin.Dispatcher.Invoke(() => syncWin.ReportProgress("正在拉取 CVM/轻量（并行）…", current, total > 0 ? total : 1));
            }
        });
        var progressLighthouse = new Progress<(string message, int current, int total)>(p =>
        {
            lock (progressLock)
            {
                totalLighthouse = p.total;
                completedLighthouse = p.current;
                var total = totalCvm + totalLighthouse;
                var current = completedCvm + completedLighthouse;
                syncWin.Dispatcher.Invoke(() => syncWin.ReportProgress("正在拉取 CVM/轻量（并行）…", current, total > 0 ? total : 1));
            }
        });

        List<TencentCvmInstance>? cvmInstances = null;
        List<TencentLighthouseInstance>? lighthouseInstances = null;
        Exception? runEx = null;
        var tCvm = Task.Run(() => TencentCloudService.ListInstances(secretId, secretKey, progressCvm, cts.Token));
        var tLighthouse = Task.Run(() => TencentCloudService.ListLighthouseInstances(secretId, secretKey, progressLighthouse, cts.Token));
        var t = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(tCvm, tLighthouse);
                return (Cvm: tCvm.Result, Lighthouse: tLighthouse.Result);
            }
            catch (Exception ex)
            {
                runEx = ex;
                return (Cvm: (List<TencentCvmInstance>?)null, Lighthouse: (List<TencentLighthouseInstance>?)null);
            }
        });

        while (!t.IsCompleted && syncWin.IsLoaded && syncWin.IsVisible)
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                System.Windows.Threading.DispatcherPriority.Background,
                () => { });
            Thread.Sleep(50);
        }

        if (runEx != null || t.Result.Cvm == null || t.Result.Lighthouse == null)
        {
            if (runEx != null)
                ExceptionLog.Write(runEx, "腾讯云同步失败", toCrashLog: false);
            var errMsg = runEx != null ? GetFullExceptionMessage(runEx) : "拉取失败或已取消";
            syncWin.ReportResult(errMsg, false);
            return;
        }
        cvmInstances = t.Result.Cvm;
        lighthouseInstances = t.Result.Lighthouse;
        var cloudInstanceIds = cvmInstances.Select(i => i.InstanceId)
            .Concat(lighthouseInstances.Select(i => i.InstanceId))
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 收集本组下所有带 ResourceId 的服务器节点
        var serverNodesUnderGroup = new List<Node>();
        void CollectServerNodes(string parentId)
        {
            foreach (var n in _nodes.Where(x => x.ParentId == parentId))
            {
                if (n.Type == NodeType.ssh || n.Type == NodeType.rdp)
                {
                    if (!string.IsNullOrEmpty(n.Config?.ResourceId))
                        serverNodesUnderGroup.Add(n);
                }
                else if (n.Type == NodeType.group || n.Type == NodeType.tencentCloudGroup || n.Type == NodeType.aliCloudGroup || n.Type == NodeType.kingsoftCloudGroup)
                    CollectServerNodes(n.Id);
            }
        }
        CollectServerNodes(groupNode.Id);

        // 删除本地存在但云上已不存在的节点（先询问用户），并记录实际删除的节点名用于变动列表
        var toRemove = serverNodesUnderGroup.Where(n => !cloudInstanceIds.Contains(n.Config!.ResourceId!)).ToList();
        var removedDetail = new List<string>();
        if (toRemove.Count > 0)
        {
            if (MessageBox.Show($"云上已不存在以下 {toRemove.Count} 个实例，是否从本地树中删除？\n\n此操作不可恢复。", "xOpenTerm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                foreach (var n in toRemove)
                {
                    removedDetail.Add(n.Name ?? n.Config?.ResourceId ?? n.Id);
                    _nodes.RemoveAll(x => x.Id == n.Id);
                }
            }
            else
                toRemove = new List<Node>();
        }

        // 现有实例 ID -> 实例信息（用于更新 IP 或新增）
        var cvmInstanceMap = cvmInstances.Where(i => !string.IsNullOrEmpty(i.InstanceId)).ToDictionary(i => i.InstanceId!, StringComparer.OrdinalIgnoreCase);
        var lighthouseInstanceMap = lighthouseInstances.Where(i => !string.IsNullOrEmpty(i.InstanceId)).ToDictionary(i => i.InstanceId!, StringComparer.OrdinalIgnoreCase);

        // 更新已有节点的 Host，并记录 IP 变动用于详细列表
        var updatedDetail = new List<string>();
        foreach (var n in serverNodesUnderGroup.Where(n => _nodes.Any(x => x.Id == n.Id)))
        {
            var rid = n.Config?.ResourceId;
            if (string.IsNullOrEmpty(rid)) continue;

            // 先尝试从 CVM 实例中查找，再尝试从轻量服务器实例中查找
            if (cvmInstanceMap.TryGetValue(rid, out var cvmIns))
            {
                n.Config!.CloudRegionId = cvmIns.Region;
                n.Config.CloudIsLightweight = false;
                var newHost = cvmIns.PublicIp ?? cvmIns.PrivateIp ?? "";
                if (!string.IsNullOrEmpty(newHost) && n.Config.Host != newHost)
                {
                    var oldHost = n.Config.Host ?? "";
                    n.Config.Host = newHost;
                    updatedDetail.Add($"{n.Name ?? rid}: {oldHost} → {newHost}");
                }
            }
            else if (lighthouseInstanceMap.TryGetValue(rid, out var lighthouseIns))
            {
                n.Config!.CloudRegionId = lighthouseIns.Region;
                n.Config.CloudIsLightweight = true;
                var newHost = lighthouseIns.PublicIp ?? lighthouseIns.PrivateIp ?? "";
                if (!string.IsNullOrEmpty(newHost) && n.Config.Host != newHost)
                {
                    var oldHost = n.Config.Host ?? "";
                    n.Config.Host = newHost;
                    updatedDetail.Add($"{n.Name ?? rid}: {oldHost} → {newHost}");
                }
            }
        }

        // 新增云上有但本地没有的实例（按现有树结构插入到对应机房/项目下）
        var existingRegionNodes = _nodes.Where(x => x.ParentId == groupNode.Id && x.Type == NodeType.group).ToList();
        var existingProjectNodes = _nodes.Where(x => x.ParentId != null && existingRegionNodes.Any(r => r.Id == x.ParentId) && x.Type == NodeType.group).ToList();
        var regionByKey = existingRegionNodes.ToDictionary(n => n.Name ?? "", StringComparer.OrdinalIgnoreCase);
        var projectByKey = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in existingProjectNodes)
        {
            var regionNode = existingRegionNodes.FirstOrDefault(r => r.Id == p.ParentId);
            if (regionNode != null)
            {
                var pid = GetTencentProjectIdFromName(p.Name);
                projectByKey[(regionNode.Name ?? "") + ":" + pid] = p;
            }
        }

        var existingIds = CollectResourceIdsUnderGroup(groupNode.Id);
        var addedCount = 0;
        var addedDetail = new List<string>();

        // 处理 CVM 实例（地域 → 项目 → 服务器）
        foreach (var ins in cvmInstances)
        {
            if (string.IsNullOrEmpty(ins.InstanceId) || existingIds.Contains(ins.InstanceId)) continue;
            existingIds.Add(ins.InstanceId);
            addedCount++;
            addedDetail.Add(string.IsNullOrEmpty(ins.InstanceName) ? ins.InstanceId! : $"{ins.InstanceName} ({ins.InstanceId})");

            var regionName = ins.RegionName ?? ins.Region ?? "";
            if (!regionByKey.TryGetValue(regionName, out var regionNode))
            {
                regionNode = new Node
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = groupNode.Id,
                    Type = NodeType.group,
                    Name = ins.RegionName ?? ins.Region ?? "",
                    Config = null
                };
                _nodes.Add(regionNode);
                regionByKey[regionName] = regionNode;
            }

            var projectKey = regionName + ":" + ins.ProjectId;
            if (!projectByKey.TryGetValue(projectKey, out var projectNode))
            {
                var projectDisplayName = !string.IsNullOrWhiteSpace(ins.ProjectName)
                    ? $"{ins.ProjectName} ({ins.ProjectId})"
                    : "项目 " + ins.ProjectId;
                projectNode = new Node
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = regionNode.Id,
                    Type = NodeType.group,
                    Name = projectDisplayName,
                    Config = null
                };
                _nodes.Add(projectNode);
                projectByKey[projectKey] = projectNode;
            }

            var host = ins.PublicIp ?? ins.PrivateIp ?? "";
            if (string.IsNullOrEmpty(host)) continue;

            var serverNode = new Node
            {
                Id = Guid.NewGuid().ToString(),
                ParentId = projectNode.Id,
                Type = ins.IsWindows ? NodeType.rdp : NodeType.ssh,
                Name = string.IsNullOrEmpty(ins.InstanceName) ? ins.InstanceId : ins.InstanceName,
                Config = new ConnectionConfig
                {
                    Host = host,
                    Port = (ushort)(ins.IsWindows ? 3389 : 22),
                    ResourceId = ins.InstanceId,
                    AuthSource = AuthSource.parent,
                    CloudRegionId = ins.Region,
                    CloudIsLightweight = false
                }
            };
            _nodes.Add(serverNode);
        }

        // 处理轻量服务器实例（地域 → 服务器，无项目层）
        var lighthouseRegionKeyPrefix = "轻量服务器-";
        foreach (var ins in lighthouseInstances)
        {
            if (string.IsNullOrEmpty(ins.InstanceId) || existingIds.Contains(ins.InstanceId)) continue;
            existingIds.Add(ins.InstanceId);
            addedCount++;
            addedDetail.Add(string.IsNullOrEmpty(ins.InstanceName) ? ins.InstanceId! : $"{ins.InstanceName} ({ins.InstanceId})");

            var regionName = ins.RegionName ?? ins.Region ?? "";
            // 轻量服务器地域添加前缀以区分 CVM
            var regionKey = lighthouseRegionKeyPrefix + regionName;
            if (!regionByKey.TryGetValue(regionKey, out var regionNode))
            {
                regionNode = new Node
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = groupNode.Id,
                    Type = NodeType.group,
                    Name = regionName + " (轻量)",
                    Config = null
                };
                _nodes.Add(regionNode);
                regionByKey[regionKey] = regionNode;
            }

            var host = ins.PublicIp ?? ins.PrivateIp ?? "";
            if (string.IsNullOrEmpty(host)) continue;

            var serverNode = new Node
            {
                Id = Guid.NewGuid().ToString(),
                ParentId = regionNode.Id,
                Type = ins.IsWindows ? NodeType.rdp : NodeType.ssh,
                Name = string.IsNullOrEmpty(ins.InstanceName) ? ins.InstanceId : ins.InstanceName,
                Config = new ConnectionConfig
                {
                    Host = host,
                    Port = (ushort)(ins.IsWindows ? 3389 : 22),
                    ResourceId = ins.InstanceId,
                    AuthSource = AuthSource.parent,
                    CloudRegionId = ins.Region,
                    CloudIsLightweight = true
                }
            };
            _nodes.Add(serverNode);
        }

        _storage.SaveNodes(_nodes);
        // 汇总变动详情：删除、更新 IP、新增
        var detailLines = new List<string>();
        foreach (var s in removedDetail)
            detailLines.Add("[删除] " + s);
        foreach (var s in updatedDetail)
            detailLines.Add("[更新IP] " + s);
        foreach (var s in addedDetail)
            detailLines.Add("[新增] " + s);
        var summary = $"已删除 {removedDetail.Count} 个本地节点，更新 {updatedDetail.Count} 台 IP，新增 {addedCount} 台实例（CVM: {cvmInstances.Count}，轻量: {lighthouseInstances.Count}）。";
        syncWin.ReportResult(summary, true, detailLines.Count > 0 ? detailLines : null);
        // 同步后只恢复当前同步组的选中与展开，避免沿用旧选中项导致其它云组被展开
        _selectedNodeIds.Clear();
        _selectedNodeIds.Add(groupNode.Id);
        BuildTree(expandNodes: false);
    }

    private HashSet<string> CollectResourceIdsUnderGroup(string groupId)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in _nodes.Where(x => x.ParentId == groupId))
        {
            if ((n.Type == NodeType.ssh || n.Type == NodeType.rdp) && !string.IsNullOrEmpty(n.Config?.ResourceId))
                set.Add(n.Config!.ResourceId!);
            if (n.Type == NodeType.group || n.Type == NodeType.tencentCloudGroup || n.Type == NodeType.aliCloudGroup || n.Type == NodeType.kingsoftCloudGroup)
            {
                foreach (var id in CollectResourceIdsUnderGroup(n.Id))
                    set.Add(id);
            }
        }
        return set;
    }

    /// <summary>拼接异常及内部异常信息，便于在同步窗口显示具体错误原因（如密钥错误、API 错误码等）。</summary>
    private static string GetFullExceptionMessage(Exception ex)
    {
        var msg = ex?.Message ?? "";
        if (ex?.InnerException != null)
        {
            var inner = ex.InnerException.Message ?? "";
            if (!string.IsNullOrEmpty(inner) && !msg.Contains(inner))
                msg = msg + " " + inner;
        }
        return string.IsNullOrWhiteSpace(msg) ? "拉取失败" : msg.Trim();
    }

    /// <summary>从项目节点显示名中解析出项目 ID（支持 "项目 123" 或 "项目名 (123)" 格式）。</summary>
    private static string GetTencentProjectIdFromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "0";
        // 匹配末尾 "(数字)" 如 "默认项目 (1000101)"
        var match = System.Text.RegularExpressions.Regex.Match(name, @"\((\d+)\)\s*$");
        if (match.Success) return match.Groups[1].Value;
        // 兼容旧格式 "项目 123"
        var s = name.Replace("项目 ", "", StringComparison.Ordinal).Trim();
        return string.IsNullOrEmpty(s) ? "0" : s;
    }
}
