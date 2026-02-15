using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using KSYUN.SDK.KEC;

namespace xOpenTerm.Services;

/// <summary>金山云 KEC 实例信息，用于构建节点树（地域→服务器）。</summary>
public record KsyunKecInstance(
    string RegionId,
    string RegionName,
    string InstanceId,
    string InstanceName,
    string? PublicIp,
    string? PrivateIp,
    string? OsName,
    bool IsWindows);

/// <summary>拉取金山云 KEC 实例列表：先 DescribeRegions 获取有权限地域，再按地域 DescribeInstances，支持进度与取消。</summary>
/// <remarks>
/// 请求由 KSYUN.SDK.KEC 发出，若出现 SSL/网络错误，可排查以下地址（金山云 KEC 地域化 API）：
/// - 主机名：<c>kec.api.ksyun.com</c>（或按地域如 <c>kec.cn-beijing-6.api.ksyun.com</c>，以 SDK 实际为准）
/// - 协议：HTTPS
/// - 使用的 API：DescribeRegions（Version=2016-03-04）、DescribeInstances（Version=2016-03-04），地域参数 Region=cn-beijing-6 等
/// </remarks>
public static class KingsoftCloudService
{
    /// <summary>调用 DescribeRegions 时使用的默认地域（金山云 KEC 地域化 API 需指定一个地域作为 endpoint）。</summary>
    private const string DefaultRegionForApi = "cn-beijing-6";

    /// <summary>不拉取实例的地域（该地域 DescribeInstances 在某些环境下会 SSL 连接失败，跳过以保障同步可用）。</summary>
    private static readonly HashSet<string> ExcludedRegionIds = new(StringComparer.OrdinalIgnoreCase) { "cn-northwest-1", "cn-northwest-3", "cn-northwest-4" };

    /// <summary>拉取所有有权限地域的 KEC 实例，多地域并行拉取，报告进度并支持取消。</summary>
    public static List<KsyunKecInstance> ListInstances(
        string accessKeyId,
        string accessKeySecret,
        IProgress<(string message, int current, int total)>? progress,
        CancellationToken cancellationToken = default)
    {
        var client = new KsyunKecClient(DefaultRegionForApi, "https", accessKeyId, accessKeySecret);

        progress?.Report(("正在拉取地域列表…", 0, 1));
        var regionsQuery = new JObject { ["Action"] = "DescribeRegions", ["Version"] = "2016-03-04" };
        var regionsResp = client.DescribeRegions(regionsQuery);
        var regionsToken = regionsResp.data?["RegionSet"];
        var regionList = new List<(string RegionId, string RegionName)>();
        if (regionsToken is JArray arr)
        {
            foreach (var item in arr.OfType<JObject>())
            {
                var regionId = item["Region"]?.ToString();
                var regionName = item["RegionName"]?.ToString() ?? regionId ?? "";
                if (!string.IsNullOrEmpty(regionId) && !ExcludedRegionIds.Contains(regionId))
                    regionList.Add((regionId, regionName));
            }
        }

        if (regionList.Count == 0)
        {
            progress?.Report(("拉取完成", 0, 0));
            return new List<KsyunKecInstance>();
        }

        var bag = new ConcurrentBag<KsyunKecInstance>();
        var totalRegions = regionList.Count;
        int completed = 0;
        const int maxResults = 100;
        var options = new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken };

        Parallel.ForEach(regionList, options, (region) =>
        {
            var (regionId, regionName) = region;
            try
            {
                var regionClient = new KsyunKecClient(regionId, "https", accessKeyId, accessKeySecret);
                int marker = 0;

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var query = new JObject
                    {
                        ["Action"] = "DescribeInstances",
                        ["Version"] = "2016-03-04",
                        ["MaxResults"] = maxResults,
                        ["Marker"] = marker
                    };
                    var listResp = regionClient.DescribeInstances(query);
                    var instancesSet = listResp.data?["InstancesSet"] as JArray;
                    if (instancesSet == null) break;

                    foreach (var ins in instancesSet.OfType<JObject>())
                    {
                        var instanceId = ins["InstanceId"]?.ToString();
                        if (string.IsNullOrEmpty(instanceId)) continue;

                        var instanceName = ins["InstanceName"]?.ToString() ?? instanceId;
                        var privateIp = ins["PrivateIpAddress"]?.ToString();
                        string? publicIp = null;
                        var netSet = ins["NetworkInterfaceSet"] as JArray;
                        if (netSet != null)
                        {
                            foreach (var ni in netSet.OfType<JObject>())
                            {
                                var pip = ni["PublicIp"]?.ToString();
                                if (!string.IsNullOrEmpty(pip)) { publicIp = pip; break; }
                            }
                        }
                        if (string.IsNullOrEmpty(publicIp))
                            publicIp = ins["PublicIpAddress"]?.ToString();

                        var imageId = ins["ImageId"]?.ToString() ?? "";
                        var osName = ins["OsName"]?.ToString() ?? imageId;
                        var isWin = (osName.Contains("Windows", System.StringComparison.OrdinalIgnoreCase))
                            || (ins["Platform"]?.ToString() ?? "").Equals("windows", System.StringComparison.OrdinalIgnoreCase);

                        bag.Add(new KsyunKecInstance(
                            regionId,
                            regionName,
                            instanceId,
                            instanceName,
                            publicIp,
                            privateIp,
                            osName,
                            isWin));
                    }

                    var instanceCount = listResp.data?["InstanceCount"]?.Value<int>() ?? 0;
                    if (instancesSet.Count < maxResults || marker + instancesSet.Count >= instanceCount)
                        break;
                    marker += maxResults;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"金山云拉取实例失败，地域 RegionId={regionId}, RegionName={regionName}。", ex);
            }

            var c = Interlocked.Increment(ref completed);
            progress?.Report(($"正在拉取 KEC ({c}/{totalRegions} 地域)…", c, totalRegions));
        });

        progress?.Report(("拉取完成", totalRegions, totalRegions));
        return bag.ToList();
    }

    /// <summary>查询单台 KEC 实例的磁盘信息（系统盘 + 数据盘容量，单位 GB）。用于云 RDP 节点磁盘空间统计。</summary>
    public static (int? SystemDiskSizeGb, IReadOnlyList<int> DataDiskSizesGb) GetInstanceDiskInfo(
        string accessKeyId,
        string accessKeySecret,
        string instanceId,
        string regionId,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var client = new KsyunKecClient(regionId, "https", accessKeyId, accessKeySecret);
        var query = new JObject
        {
            ["Action"] = "DescribeInstances",
            ["Version"] = "2016-03-04",
            ["InstanceId"] = instanceId
        };
        var resp = client.DescribeInstances(query);
        var instancesSet = resp.data?["InstancesSet"] as JArray;
        if (instancesSet == null || instancesSet.Count == 0)
            return (null, Array.Empty<int>());
        var ins = instancesSet[0] as JObject;
        if (ins == null) return (null, Array.Empty<int>());
        var blockSet = ins["BlockDeviceMapping"] as JArray ?? ins["BlockDeviceMappingSet"] as JArray;
        if (blockSet == null) return (null, Array.Empty<int>());
        int? systemGb = null;
        var dataList = new List<int>();
        var idx = 0;
        foreach (var item in blockSet.OfType<JObject>())
        {
            var sizeToken = item["VolumeSize"];
            if (sizeToken == null) continue;
            var size = sizeToken.Type == JTokenType.Integer ? sizeToken.Value<int>() : 0;
            if (size <= 0) continue;
            if (idx == 0)
                systemGb = size;
            else
                dataList.Add(size);
            idx++;
        }
        return (systemGb, dataList);
    }
}
