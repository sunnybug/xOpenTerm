using System.Collections.Generic;
using System.Linq;
using TencentCloud.Common;
using TencentCloud.Cvm.V20170312;
using TencentCloud.Cvm.V20170312.Models;
using TencentCloud.Tag.V20180813;
using TencentCloud.Tag.V20180813.Models;

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

    /// <summary>拉取所有地域的实例，报告进度并支持取消。实例会附带项目名称（若拉取到）。</summary>
    public static List<TencentCvmInstance> ListInstances(
        string secretId,
        string secretKey,
        IProgress<(string message, int current, int total)>? progress,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var cred = new Credential { SecretId = secretId, SecretKey = secretKey };
        progress?.Report(("正在拉取项目列表…", 0, 1));
        var projectIdToName = GetProjectIdToName(secretId, secretKey, cancellationToken);

        var list = new List<TencentCvmInstance>();
        var totalRegions = Regions.Length;
        var current = 0;

        foreach (var (region, regionName) in Regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(($"正在拉取 {regionName} ({region})…", current, totalRegions));

            var client = new CvmClient(cred, region);
            var offset = 0;
            const int limit = 100;

            while (true)
            {
                var req = new DescribeInstancesRequest
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
                    list.Add(new TencentCvmInstance(
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

            current++;
        }

        progress?.Report(("拉取完成", totalRegions, totalRegions));
        return list;
    }
}
