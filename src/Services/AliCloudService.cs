using System.Collections.Generic;
using System.Linq;
using AlibabaCloud.SDK.Ecs20140526;
using AlibabaCloud.SDK.Ecs20140526.Models;
using AlibabaCloud.OpenApiClient.Models;

namespace xOpenTerm.Services;

/// <summary>阿里云 ECS 实例信息，用于构建节点树（地域→服务器）。</summary>
public record AliEcsInstance(
    string RegionId,
    string RegionName,
    string InstanceId,
    string InstanceName,
    string? PublicIp,
    string? PrivateIp,
    string? OsName,
    bool IsWindows);

/// <summary>拉取阿里云 ECS 实例列表：先 DescribeRegions 获取地域，再按地域 DescribeInstances，支持进度与取消。</summary>
public static class AliCloudService
{
    /// <summary>拉取所有地域的 ECS 实例，报告进度并支持取消。</summary>
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
        var client = new Client(config);

        progress?.Report(("正在拉取地域列表…", 0, 1));
        var describeRegionsReq = new DescribeRegionsRequest();
        var regionsResp = client.DescribeRegions(describeRegionsReq);
        var regionList = regionsResp?.Body?.Regions?.Region ?? new List<DescribeRegionsResponseBody.DescribeRegionsResponseBodyRegions.DescribeRegionsResponseBodyRegionsRegion>();
        var regions = regionList
            .Where(r => r?.RegionId != null && (r.Status == null || r.Status == "available"))
            .OrderBy(r => r!.RegionId)
            .ToList();

        var list = new List<AliEcsInstance>();
        var totalRegions = regions.Count;
        var current = 0;

        foreach (var region in regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var regionId = region.RegionId ?? "";
            var regionName = region.LocalName ?? regionId;
            progress?.Report(($"正在拉取 {regionName} ({regionId})…", current, totalRegions));

            // 每个地域必须使用该地域的 endpoint，否则会报 404 The specified endpoint cant operate this region
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
            var regionClient = new Client(regionConfig);

            var listReq = new DescribeInstancesRequest
            {
                RegionId = regionId,
                MaxResults = 100
            };
            string? nextToken = null;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                listReq.NextToken = nextToken;
                var listResp = regionClient.DescribeInstances(listReq);
                var instanceList = listResp?.Body?.Instances?.Instance ?? new List<DescribeInstancesResponseBody.DescribeInstancesResponseBodyInstances.DescribeInstancesResponseBodyInstancesInstance>();
                var instances = instanceList;

                foreach (var ins in instances)
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

                    list.Add(new AliEcsInstance(
                        regionId,
                        regionName,
                        ins.InstanceId,
                        ins.InstanceName ?? ins.InstanceId ?? "",
                        publicIp,
                        privateIp,
                        osName,
                        isWin));
                }

                nextToken = listResp?.Body?.NextToken;
            } while (!string.IsNullOrEmpty(nextToken));

            current++;
        }

        progress?.Report(("拉取完成", totalRegions, totalRegions));
        return list;
    }
}
