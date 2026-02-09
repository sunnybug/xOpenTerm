using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TencentCloud.Common;
using TencentCloud.Cvm.V20170312;
using TencentCloud.Cvm.V20170312.Models;
using TencentCloud.Tag.V20180813;
using TencentCloud.Tag.V20180813.Models;
using TencentCloud.Lighthouse.V20200324;
using TencentCloud.Lighthouse.V20200324.Models;

namespace xOpenTerm.Services;

/// <summary>腾讯云 CVM 实例信息，用于构建节点树（机房→项目→服务器）。</summary>
public record TencentCvmInstance(
    string Region,
    string RegionName,
    int ProjectId,
    string? ProjectName,
    string InstanceId,
    string InstanceName,
    string? PublicIp,
    string? PrivateIp,
    string? OsName,
    bool IsWindows);

/// <summary>腾讯云轻量应用服务器实例信息，用于构建节点树（地域→服务器）。</summary>
public record TencentLighthouseInstance(
    string Region,
    string RegionName,
    string InstanceId,
    string InstanceName,
    string? PublicIp,
    string? PrivateIp,
    string? OsName,
    bool IsWindows);

/// <summary>拉取腾讯云 CVM 实例列表，按地域分页，支持进度与取消。</summary>
public static class TencentCloudService
{
    /// <summary>常见 CVM 地域（可扩展）。</summary>
    private static readonly (string Region, string Name)[] Regions =
    {
        ("ap-guangzhou", "广州"),
        ("ap-hongkong", "香港"),
        ("ap-shanghai", "上海"),
        ("ap-nanjing", "南京"),
        ("ap-beijing", "北京"),
        ("ap-chengdu", "成都"),
        ("ap-chongqing", "重庆"),
        ("ap-singapore", "新加坡"),
        ("ap-bangkok", "曼谷"),
        ("na-siliconvalley", "硅谷"),
    };

    /// <summary>拉取项目 ID→名称 映射（用于节点树显示项目名）。失败时返回空字典，不影响实例拉取。</summary>
    private static Dictionary<ulong, string> GetProjectIdToName(string secretId, string secretKey, System.Threading.CancellationToken cancellationToken)
    {
        var map = new Dictionary<ulong, string>();
        try
        {
            var cred = new Credential { SecretId = secretId, SecretKey = secretKey };
            var client = new TagClient(cred, "ap-guangzhou");
            var offset = 0;
            const int limit = 1000;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var req = new DescribeProjectsRequest { AllList = 0UL, Limit = (ulong)limit, Offset = (ulong)offset };
                var resp = client.DescribeProjectsSync(req);
                if (resp.Projects == null) break;
                foreach (var p in resp.Projects)
                {
                    if (p.ProjectId.HasValue && !string.IsNullOrEmpty(p.ProjectName))
                        map[p.ProjectId.Value] = p.ProjectName;
                }
                var projectCount = resp.Projects.Count();
                if (projectCount < limit) break;
                offset += limit;
            }
        }
        catch
        {
            // 忽略：无权限或 Tag 未开通时仍可显示项目 ID
        }
        return map;
    }

    /// <summary>拉取所有地域的实例，多地域并行拉取，报告进度并支持取消。实例会附带项目名称（若拉取到）。</summary>
    public static List<TencentCvmInstance> ListInstances(
        string secretId,
        string secretKey,
        IProgress<(string message, int current, int total)>? progress,
        System.Threading.CancellationToken cancellationToken = default)
    {
        progress?.Report(("正在拉取项目列表…", 0, 1));
        var projectIdToName = GetProjectIdToName(secretId, secretKey, cancellationToken);

        var cred = new Credential { SecretId = secretId, SecretKey = secretKey };
        var bag = new ConcurrentBag<TencentCvmInstance>();
        var totalRegions = Regions.Length;
        var completed = 0;
        const int maxParallel = 8;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxParallel,
            CancellationToken = cancellationToken
        };

        Parallel.ForEach(Regions, options, (regionTuple) =>
        {
            var (region, regionName) = regionTuple;
            var client = new CvmClient(cred, region);
            var offset = 0;
            const int limit = 100;

            while (true)
            {
                options.CancellationToken.ThrowIfCancellationRequested();
                var req = new TencentCloud.Cvm.V20170312.Models.DescribeInstancesRequest
                {
                    Offset = offset,
                    Limit = limit
                };
                var resp = client.DescribeInstancesSync(req);
                if (resp.InstanceSet == null) break;

                foreach (var ins in resp.InstanceSet)
                {
                    var osName = ins.OsName ?? "";
                    var isWin = osName.Contains("Windows", System.StringComparison.OrdinalIgnoreCase);
                    var publicIp = ins.PublicIpAddresses?.FirstOrDefault();
                    var privateIp = ins.PrivateIpAddresses?.FirstOrDefault();
                    var projectId = (int)(ins.Placement?.ProjectId ?? 0);
                    projectIdToName.TryGetValue((ulong)projectId, out var projectName);
                    bag.Add(new TencentCvmInstance(
                        region,
                        regionName,
                        projectId,
                        projectName,
                        ins.InstanceId ?? "",
                        ins.InstanceName ?? ins.InstanceId ?? "",
                        publicIp,
                        privateIp,
                        osName,
                        isWin));
                }

                var got = resp.InstanceSet == null ? 0 : resp.InstanceSet.Count();
                if (got < limit)
                    break;
                offset += limit;
            }

            var c = Interlocked.Increment(ref completed);
            progress?.Report(($"正在拉取 CVM ({c}/{totalRegions} 地域)…", c, totalRegions));
        });

        progress?.Report(("拉取完成", totalRegions, totalRegions));
        return bag.ToList();
    }

    /// <summary>常见轻量应用服务器地域（可扩展）。</summary>
    private static readonly (string Region, string Name)[] LighthouseRegions =
    {
        ("ap-guangzhou", "广州"),
        ("ap-hongkong", "香港"),
        ("ap-shanghai", "上海"),
        ("ap-nanjing", "南京"),
        ("ap-beijing", "北京"),
        ("ap-chengdu", "成都"),
        ("ap-chongqing", "重庆"),
        ("ap-singapore", "新加坡"),
        ("ap-bangkok", "曼谷"),
        ("ap-tokyo", "东京"),
        ("ap-seoul", "首尔"),
        ("na-siliconvalley", "硅谷"),
    };

    /// <summary>拉取所有地域的轻量应用服务器实例，多地域并行拉取，报告进度并支持取消。</summary>
    public static List<TencentLighthouseInstance> ListLighthouseInstances(
        string secretId,
        string secretKey,
        IProgress<(string message, int current, int total)>? progress,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var cred = new Credential { SecretId = secretId, SecretKey = secretKey };
        var bag = new ConcurrentBag<TencentLighthouseInstance>();
        var totalRegions = LighthouseRegions.Length;
        var completed = 0;
        const int maxParallel = 8;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxParallel,
            CancellationToken = cancellationToken
        };

        Parallel.ForEach(LighthouseRegions, options, (regionTuple) =>
        {
            var (region, regionName) = regionTuple;
            try
            {
                var client = new LighthouseClient(cred, region);
                var offset = 0;
                const int limit = 100;

                while (true)
                {
                    options.CancellationToken.ThrowIfCancellationRequested();
                    var req = new TencentCloud.Lighthouse.V20200324.Models.DescribeInstancesRequest
                    {
                        Offset = offset,
                        Limit = limit
                    };
                    var resp = client.DescribeInstancesSync(req);
                    if (resp.InstanceSet == null) break;

                    foreach (var ins in resp.InstanceSet)
                    {
                        var osName = ins.OsName ?? "";
                        var isWin = osName.Contains("Windows", System.StringComparison.OrdinalIgnoreCase);

                        string? publicIp = null;
                        if (ins.PublicAddresses != null && ins.PublicAddresses.Length > 0)
                            publicIp = ins.PublicAddresses[0];
                        string? privateIp = null;
                        if (ins.PrivateAddresses != null && ins.PrivateAddresses.Length > 0)
                            privateIp = ins.PrivateAddresses[0];

                        bag.Add(new TencentLighthouseInstance(
                            region,
                            regionName,
                            ins.InstanceId ?? "",
                            ins.InstanceName ?? ins.InstanceId ?? "",
                            publicIp,
                            privateIp,
                            osName,
                            isWin));
                    }

                    var got = resp.InstanceSet == null ? 0 : resp.InstanceSet.Length;
                    if (got < limit)
                        break;
                    offset += limit;
                }
            }
            catch (Exception ex)
            {
                // 某个地域失败不影响其他地域
                System.Diagnostics.Debug.WriteLine($"拉取轻量服务器 {regionName} 失败: {ex.Message}");
            }

            var c = Interlocked.Increment(ref completed);
            progress?.Report(($"正在拉取轻量服务器 ({c}/{totalRegions} 地域)…", c, totalRegions));
        });

        progress?.Report(("拉取完成", totalRegions, totalRegions));
        return bag.ToList();
    }
}
