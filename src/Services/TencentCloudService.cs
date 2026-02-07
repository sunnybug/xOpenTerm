using System.Collections.Generic;
using System.Linq;
using TencentCloud.Cvm.V20170312;
using TencentCloud.Cvm.V20170312.Models;
using TencentCloud.Common;

namespace xOpenTerm.Services;

/// <summary>腾讯云 CVM 实例信息，用于构建节点树（机房→项目→服务器）。</summary>
public record TencentCvmInstance(
    string Region,
    string RegionName,
    int ProjectId,
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

    /// <summary>拉取所有地域的实例，报告进度并支持取消。</summary>
    public static List<TencentCvmInstance> ListInstances(
        string secretId,
        string secretKey,
        IProgress<(string message, int current, int total)>? progress,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var cred = new Credential { SecretId = secretId, SecretKey = secretKey };
        var list = new List<TencentCvmInstance>();
        var totalRegions = Regions.Length;
        var current = 0;

        foreach (var (region, regionName) in Regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(($"正在拉取 {regionName} ({region})…", current, totalRegions));

            try
            {
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
                        list.Add(new TencentCvmInstance(
                            region,
                            regionName,
                            projectId,
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
            }
            catch (System.Exception)
            {
                // 某地域无权限或未开通则跳过
            }

            current++;
        }

        progress?.Report(("拉取完成", totalRegions, totalRegions));
        return list;
    }
}
