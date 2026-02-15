using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AlibabaCloud.SDK.Cms20190101;
using AlibabaCloud.SDK.Cms20190101.Models;
using AlibabaCloud.SDK.Ecs20140526;
using AlibabaCloud.SDK.Ecs20140526.Models;
using AlibabaCloud.OpenApiClient.Models;
using AlibabaCloud.SDK.SWAS_OPEN20200601;
using AlibabaCloud.SDK.SWAS_OPEN20200601.Models;

namespace xOpenTerm.Services;

/// <summary>阿里云 ECS/轻量应用服务器 实例信息，用于构建节点树（地域→服务器）。</summary>
public record AliEcsInstance(
    string RegionId,
    string RegionName,
    string InstanceId,
    string InstanceName,
    string? PublicIp,
    string? PrivateIp,
    string? OsName,
    bool IsWindows,
    bool IsLightweight = false);

/// <summary>拉取阿里云 ECS 实例列表：先 DescribeRegions 获取地域，再按地域 DescribeInstances，支持进度与取消。</summary>
public static class AliCloudService
{
    /// <summary>拉取所有地域的 ECS 实例，多地域并行拉取，报告进度并支持取消。</summary>
    public static List<AliEcsInstance> ListInstances(
        string accessKeyId,
        string accessKeySecret,
        IProgress<(string message, int current, int total)>? progress,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var config = new Config
        {
            AccessKeyId = accessKeyId,
            AccessKeySecret = accessKeySecret,
            Endpoint = "ecs-cn-hangzhou.aliyuncs.com",
            RegionId = "cn-hangzhou"
        };
        var client = new AlibabaCloud.SDK.Ecs20140526.Client(config);

        progress?.Report(("正在拉取地域列表…", 0, 1));
        var describeRegionsReq = new DescribeRegionsRequest();
        var regionsResp = client.DescribeRegions(describeRegionsReq);
        var regionList = regionsResp?.Body?.Regions?.Region ?? new List<DescribeRegionsResponseBody.DescribeRegionsResponseBodyRegions.DescribeRegionsResponseBodyRegionsRegion>();
        var regions = regionList
            .Where(r => r?.RegionId != null && (r.Status == null || r.Status == "available"))
            .OrderBy(r => r!.RegionId)
            .ToList();

        var bag = new ConcurrentBag<AliEcsInstance>();
        var totalRegions = regions.Count;
        var completed = 0;
        const int maxParallel = 8;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxParallel,
            CancellationToken = cancellationToken
        };

        Parallel.ForEach(regions, options, (region) =>
        {
            var regionId = region.RegionId ?? "";
            var regionName = region.LocalName ?? regionId;

            var regionEndpoint = !string.IsNullOrEmpty(region.RegionEndpoint)
                ? region.RegionEndpoint
                : $"ecs.{regionId}.aliyuncs.com";
            var regionConfig = new Config
            {
                AccessKeyId = accessKeyId,
                AccessKeySecret = accessKeySecret,
                Endpoint = regionEndpoint,
                RegionId = regionId
            };
            var regionClient = new AlibabaCloud.SDK.Ecs20140526.Client(regionConfig);

            var listReq = new DescribeInstancesRequest
            {
                RegionId = regionId,
                MaxResults = 100
            };
            string? nextToken = null;

            do
            {
                options.CancellationToken.ThrowIfCancellationRequested();
                listReq.NextToken = nextToken;
                var listResp = regionClient.DescribeInstances(listReq);
                var instanceList = listResp?.Body?.Instances?.Instance ?? new List<DescribeInstancesResponseBody.DescribeInstancesResponseBodyInstances.DescribeInstancesResponseBodyInstancesInstance>();

                foreach (var ins in instanceList)
                {
                    if (string.IsNullOrEmpty(ins.InstanceId)) continue;

                    var osName = ins.OSName ?? ins.OSNameEn ?? "";
                    var isWin = (ins.OSType ?? "").Equals("windows", System.StringComparison.OrdinalIgnoreCase)
                        || osName.Contains("Windows", System.StringComparison.OrdinalIgnoreCase);

                    string? publicIp = null;
                    if (ins.PublicIpAddress?.IpAddress != null && ins.PublicIpAddress.IpAddress.Count > 0)
                        publicIp = ins.PublicIpAddress.IpAddress[0];
                    if (string.IsNullOrEmpty(publicIp) && ins.EipAddress?.IpAddress != null)
                        publicIp = ins.EipAddress.IpAddress;

                    string? privateIp = null;
                    if (ins.VpcAttributes?.PrivateIpAddress?.IpAddress != null && ins.VpcAttributes.PrivateIpAddress.IpAddress.Count > 0)
                        privateIp = ins.VpcAttributes.PrivateIpAddress.IpAddress[0];
                    if (string.IsNullOrEmpty(privateIp) && ins.InnerIpAddress?.IpAddress != null && ins.InnerIpAddress.IpAddress.Count > 0)
                        privateIp = ins.InnerIpAddress.IpAddress[0];

                    bag.Add(new AliEcsInstance(
                        regionId,
                        regionName,
                        ins.InstanceId,
                        ins.InstanceName ?? ins.InstanceId ?? "",
                        publicIp,
                        privateIp,
                        osName,
                        isWin,
                        IsLightweight: false));
                }

                nextToken = listResp?.Body?.NextToken;
            } while (!string.IsNullOrEmpty(nextToken));

            var c = Interlocked.Increment(ref completed);
            progress?.Report(($"正在拉取 ECS ({c}/{totalRegions} 地域)…", c, totalRegions));
        });

        progress?.Report(("拉取完成", totalRegions, totalRegions));
        return bag.ToList();
    }

    /// <summary>轻量应用服务器支持的地域 ID 及显示名（中国内地 + 香港及海外部分）。</summary>
    private static readonly IReadOnlyList<(string RegionId, string RegionName)> SwasRegions = new[]
    {
        ("cn-qingdao", "华北1（青岛）"),
        ("cn-beijing", "华北2（北京）"),
        ("cn-zhangjiakou", "华北3（张家口）"),
        ("cn-hohhot", "华北5（呼和浩特）"),
        ("cn-wulanchabu", "华北6（乌兰察布）"),
        ("cn-hangzhou", "华东1（杭州）"),
        ("cn-shanghai", "华东2（上海）"),
        ("cn-shenzhen", "华南1（深圳）"),
        ("cn-heyuan", "华南2（河源）"),
        ("cn-guangzhou", "华南3（广州）"),
        ("cn-chengdu", "西南1（成都）"),
        ("cn-hongkong", "中国香港"),
        ("ap-southeast-1", "新加坡"),
        ("ap-southeast-5", "印度尼西亚（雅加达）"),
        ("ap-southeast-7", "泰国（曼谷）"),
        ("ap-northeast-1", "日本（东京）"),
        ("ap-northeast-2", "韩国（首尔）"),
        ("eu-central-1", "德国（法兰克福）"),
        ("us-east-1", "美国（弗吉尼亚）"),
        ("us-west-1", "美国（硅谷）")
    };

    /// <summary>拉取所有地域的轻量应用服务器实例，多地域并行拉取，报告进度并支持取消。</summary>
    public static List<AliEcsInstance> ListSwasInstances(
        string accessKeyId,
        string accessKeySecret,
        IProgress<(string message, int current, int total)>? progress,
        CancellationToken cancellationToken = default)
    {
        var config = new AlibabaCloud.OpenApiClient.Models.Config
        {
            AccessKeyId = accessKeyId,
            AccessKeySecret = accessKeySecret,
            Endpoint = "swas-open.cn-hangzhou.aliyuncs.com",
            RegionId = "cn-hangzhou"
        };
        var bag = new ConcurrentBag<AliEcsInstance>();
        var total = SwasRegions.Count;
        var completed = 0;
        const int maxParallel = 8;
        var options = new ParallelOptions { MaxDegreeOfParallelism = maxParallel, CancellationToken = cancellationToken };

        Parallel.ForEach(SwasRegions, options, (region) =>
        {
            var regionId = region.RegionId;
            var regionName = region.RegionName;
            try
            {
                // 轻量应用服务器采用地域化接入，必须使用各地域自己的 endpoint（见阿里云文档：swas.{region}.aliyuncs.com）
                var regionConfig = new AlibabaCloud.OpenApiClient.Models.Config
                {
                    AccessKeyId = accessKeyId,
                    AccessKeySecret = accessKeySecret,
                    Endpoint = $"swas.{regionId}.aliyuncs.com",
                    RegionId = regionId
                };
                var client = new AlibabaCloud.SDK.SWAS_OPEN20200601.Client(regionConfig);
                int pageNumber = 1;
                const int pageSize = 100;
                while (true)
                {
                    options.CancellationToken.ThrowIfCancellationRequested();
                    var req = new ListInstancesRequest { RegionId = regionId, PageNumber = pageNumber, PageSize = pageSize };
                    var resp = client.ListInstances(req);
                    var list = resp?.Body?.Instances ?? new List<ListInstancesResponseBody.ListInstancesResponseBodyInstances>();
                    foreach (var ins in list)
                    {
                        if (string.IsNullOrEmpty(ins.InstanceId)) continue;
                        var publicIp = ins.PublicIpAddress
                            ?? ins.NetworkAttributes?.FirstOrDefault()?.PublicIpAddress;
                        var privateIp = ins.InnerIpAddress
                            ?? ins.NetworkAttributes?.FirstOrDefault()?.PrivateIpAddress;
                        var osType = ins.Image?.OsType ?? "";
                        var isWin = string.Equals(osType, "windows", StringComparison.OrdinalIgnoreCase);
                        bag.Add(new AliEcsInstance(
                            regionId,
                            regionName,
                            ins.InstanceId,
                            ins.InstanceName ?? ins.InstanceId,
                            publicIp,
                            privateIp,
                            osType,
                            isWin,
                            IsLightweight: true));
                    }
                    if (list.Count < pageSize || (resp?.Body?.TotalCount ?? 0) <= pageNumber * pageSize)
                        break;
                    pageNumber++;
                }
            }
            catch (Exception ex)
            {
                // 该地域可能未开通轻量或无权访问，跳过；记录日志便于排查（如香港实例未拉取）
                System.Diagnostics.Debug.WriteLine($"阿里云轻量 地域 {regionId}({regionName}) 拉取失败: {ex.Message}");
            }
            var c = Interlocked.Increment(ref completed);
            progress?.Report((message: $"正在拉取轻量应用服务器 ({c}/{total} 地域)…", current: c, total: total));
        });

        progress?.Report((message: "轻量应用服务器拉取完成", current: total, total: total));
        return bag.ToList();
    }

    /// <summary>拉取 ECS + 轻量应用服务器 全部实例并合并，用于阿里云组同步。ECS 与轻量多地域并行拉取，且两者同时并行执行。</summary>
    public static List<AliEcsInstance> ListAllInstances(
        string accessKeyId,
        string accessKeySecret,
        IProgress<(string message, int current, int total)>? progress,
        CancellationToken cancellationToken = default)
    {
        var totalEcs = 0;
        var totalSwas = SwasRegions.Count;
        var completedEcs = 0;
        var completedSwas = 0;
        var lockObj = new object();

        progress?.Report(("正在拉取 ECS/轻量（并行）…", 0, totalSwas));

        IProgress<(string message, int current, int total)>? progressEcs = progress == null ? null : new Progress<(string message, int current, int total)>(p =>
        {
            lock (lockObj)
            {
                totalEcs = p.total;
                completedEcs = p.current;
                var total = totalEcs + totalSwas;
                var current = completedEcs + completedSwas;
                progress?.Report(("正在拉取 ECS/轻量（并行）…", current, total > 0 ? total : 1));
            }
        });

        IProgress<(string message, int current, int total)>? progressSwas = progress == null ? null : new Progress<(string message, int current, int total)>(p =>
        {
            lock (lockObj)
            {
                completedSwas = p.current;
                var total = totalEcs + totalSwas;
                var current = completedEcs + completedSwas;
                progress?.Report(("正在拉取 ECS/轻量（并行）…", current, total > 0 ? total : 1));
            }
        });

        List<AliEcsInstance>? ecsList = null;
        List<AliEcsInstance>? swasList = null;
        var tEcs = Task.Run(() => ListInstances(accessKeyId, accessKeySecret, progressEcs, cancellationToken), cancellationToken);
        var tSwas = Task.Run(() => ListSwasInstances(accessKeyId, accessKeySecret, progressSwas, cancellationToken), cancellationToken);
        Task.WaitAll(tEcs, tSwas);
        ecsList = tEcs.GetAwaiter().GetResult();
        swasList = tSwas.GetAwaiter().GetResult();

        progress?.Report(("拉取完成", totalEcs + totalSwas, totalEcs + totalSwas));
        return ecsList.Concat(swasList).ToList();
    }

    /// <summary>查询单台 ECS 实例的磁盘信息（系统盘 + 数据盘容量，单位 GB）。用于云 RDP 节点磁盘空间统计。轻量实例暂不支持，返回空。</summary>
    public static (int? SystemDiskSizeGb, IReadOnlyList<int> DataDiskSizesGb) GetInstanceDiskInfo(
        string accessKeyId,
        string accessKeySecret,
        string instanceId,
        string regionId,
        bool isLightweight,
        System.Threading.CancellationToken cancellationToken = default)
    {
        if (isLightweight)
            return (null, Array.Empty<int>());
        var regionEndpoint = $"ecs.{regionId}.aliyuncs.com";
        var config = new Config
        {
            AccessKeyId = accessKeyId,
            AccessKeySecret = accessKeySecret,
            Endpoint = regionEndpoint,
            RegionId = regionId
        };
        var client = new AlibabaCloud.SDK.Ecs20140526.Client(config);
        var req = new DescribeDisksRequest { RegionId = regionId, InstanceId = instanceId };
        var resp = client.DescribeDisks(req);
        var disks = resp?.Body?.Disks?.Disk ?? new List<DescribeDisksResponseBody.DescribeDisksResponseBodyDisks.DescribeDisksResponseBodyDisksDisk>();
        int? systemGb = null;
        var dataList = new List<int>();
        foreach (var d in disks)
        {
            var size = d.Size ?? 0;
            if (size <= 0) continue;
            if (string.Equals(d.Type, "system", StringComparison.OrdinalIgnoreCase))
                systemGb = size;
            else
                dataList.Add(size);
        }
        return (systemGb, dataList);
    }

    /// <summary>通过云监控 API 查询 ECS 实例磁盘使用率（需实例已安装云监控插件）。返回各设备使用率及最大值；失败或非 ECS 返回 null。</summary>
    public static (double MaxPercent, IReadOnlyList<(string Device, double Percent)>? ByDevice)? GetInstanceDiskUsageFromApi(
        string accessKeyId,
        string accessKeySecret,
        string instanceId,
        string regionId,
        bool isLightweight,
        System.Threading.CancellationToken cancellationToken = default)
    {
        if (isLightweight) return null;
        try
        {
            var config = new AlibabaCloud.OpenApiClient.Models.Config
            {
                AccessKeyId = accessKeyId,
                AccessKeySecret = accessKeySecret,
                Endpoint = "cms.aliyuncs.com",
                RegionId = "cn-hangzhou"
            };
            var client = new AlibabaCloud.SDK.Cms20190101.Client(config);
            var req = new DescribeMetricLastRequest
            {
                Namespace = "acs_ecs",
                MetricName = "diskusage_utilization",
                Period = "60",
                Dimensions = $"[{{\"instanceId\":\"{instanceId}\"}}]"
            };
            var resp = client.DescribeMetricLast(req);
            if (resp?.Body?.Datapoints == null || string.IsNullOrWhiteSpace(resp.Body.Datapoints))
                return null;
            var list = new List<(string Device, double Percent)>();
            using var doc = JsonDocument.Parse(resp.Body.Datapoints);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array) return null;
            foreach (var item in root.EnumerateArray())
            {
                if (!item.TryGetProperty("Maximum", out var maxProp)) continue;
                if (maxProp.ValueKind != JsonValueKind.Number) continue;
                var pct = maxProp.GetDouble();
                var device = item.TryGetProperty("device", out var devProp) ? devProp.GetString() ?? "" : "";
                list.Add((device, pct));
            }
            if (list.Count == 0) return null;
            var maxPercent = list.Max(x => x.Percent);
            return (maxPercent, list);
        }
        catch
        {
            return null;
        }
    }
}
