using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
public static class KingsoftCloudService
{
    /// <summary>调用 DescribeRegions 时使用的默认地域（金山云 KEC 地域化 API 需指定一个地域作为 endpoint）。</summary>
    private const string DefaultRegionForApi = "cn-beijing-6";

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
                if (!string.IsNullOrEmpty(regionId))
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
        var completed = 0;
        const int maxParallel = 8;
        var options = new ParallelOptions { MaxDegreeOfParallelism = maxParallel, CancellationToken = cancellationToken };

        Parallel.ForEach(regionList, options, (region) =>
        {
            var (regionId, regionName) = region;
            var regionClient = new KsyunKecClient(regionId, "https", accessKeyId, accessKeySecret);
            int marker = 0;
            const int maxResults = 100;

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

            var c = Interlocked.Increment(ref completed);
            progress?.Report(($"正在拉取 KEC ({c}/{totalRegions} 地域)…", c, totalRegions));
        });

        progress?.Report(("拉取完成", totalRegions, totalRegions));
        return bag.ToList();
    }
}
