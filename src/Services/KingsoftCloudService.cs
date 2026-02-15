using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
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

                        var imageObj = ins["Image"] as JObject;
                        var imageId = ins["ImageId"]?.ToString() ?? imageObj?["ImageId"]?.ToString() ?? "";
                        var osName = ins["OsName"]?.ToString() ?? imageObj?["OsName"]?.ToString() ?? imageObj?["Name"]?.ToString() ?? imageId;
                        var platform = ins["Platform"]?.ToString() ?? imageObj?["Platform"]?.ToString() ?? "";
                        var isWin = (osName.Contains("Windows", System.StringComparison.OrdinalIgnoreCase))
                            || platform.Contains("windows", System.StringComparison.OrdinalIgnoreCase);

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

    /// <summary>通过云监控 GetMetricStatistics 查询 KEC 实例磁盘使用率（需实例已安装监控代理）。失败或无数据返回 null。</summary>
    public static (double MaxPercent, IReadOnlyList<(string Device, double Percent)>? ByDevice)? GetInstanceDiskUsageFromApi(
        string accessKeyId,
        string accessKeySecret,
        string instanceId,
        string regionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var endUtc = DateTime.UtcNow.AddMinutes(-2);
            var startUtc = endUtc.AddMinutes(-10);
            var startTime = startUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var endTime = endUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            var query = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["AccessKey"] = accessKeyId,
                ["Action"] = "GetMetricStatistics",
                ["Aggregate"] = "Max",
                ["EndTime"] = endTime,
                ["InstanceID"] = instanceId,
                ["MetricName"] = "disk.utilizition.total",
                ["Namespace"] = "KEC",
                ["Period"] = "60",
                ["Service"] = "monitor",
                ["SignatureMethod"] = "HMAC-SHA256",
                ["SignatureVersion"] = "1.0",
                ["StartTime"] = startTime,
                ["Timestamp"] = timestamp,
                ["Version"] = "2010-05-25"
            };
            var canonical = string.Join("&", query.Select(kv => $"{Rfc3986Encode(kv.Key)}={Rfc3986Encode(kv.Value)}"));
            var sign = ComputeHmacSha256Hex(canonical, accessKeySecret);
            query["Signature"] = sign;

            var queryString = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
            var host = $"monitor.{regionId}.api.ksyun.com";
            var url = $"https://{host}/?{queryString}";

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Host", host);
            var resp = client.GetAsync(url, cancellationToken).GetAwaiter().GetResult();
            resp.EnsureSuccessStatusCode();
            var json = resp.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            var root = JObject.Parse(json);
            var result = root["getMetricStatisticsResult"];
            if (result == null) return null;
            var members = result["datapoints"]?["member"] as JArray;
            if (members == null || members.Count == 0) return null;

            double maxPct = 0;
            foreach (var m in members.OfType<JObject>())
            {
                var maxToken = m["max"];
                if (maxToken == null) continue;
                if (!double.TryParse(maxToken.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val)) continue;
                if (val > maxPct) maxPct = val;
            }
            var diskList = new List<(string, double)> { ("", maxPct) };
            return (maxPct, diskList);
        }
        catch
        {
            return null;
        }
    }

    private static string Rfc3986Encode(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var bytes = Encoding.UTF8.GetBytes(value);
        var sb = new StringBuilder();
        foreach (var b in bytes)
        {
            if ((b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z') || (b >= '0' && b <= '9') || b == '-' || b == '_' || b == '.' || b == '~')
                sb.Append((char)b);
            else
                sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }

    private static string ComputeHmacSha256Hex(string data, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return string.Concat(hash.Select(b => b.ToString("x2")));
    }
}
