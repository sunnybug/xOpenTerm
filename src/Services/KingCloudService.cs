using System.Collections.Concurrent;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace xOpenTerm.Services;

/// <summary>金山云 KEC 实例信息，用于构建节点树（地域→服务器）。</summary>
public record KingEcsInstance(
    string RegionId,
    string RegionName,
    string InstanceId,
    string InstanceName,
    string? PublicIp,
    string? PrivateIp,
    string? OsName,
    bool IsWindows);

/// <summary>金山云 KEC（云服务器）API 集成：拉取实例列表，按地域→服务器构建节点树。</summary>
public static class KingCloudService
{
    private const string ApiVersion = "2016-03-04";
    private const string Endpoint = "https://kec.api.ksyun.com";

    /// <summary>金山云 KEC 支持的地域 ID 及显示名（常见地域）。</summary>
    private static readonly IReadOnlyList<(string RegionId, string RegionName)> KecRegions = new[]
    {
        ("cn-beijing-6", "北京6"),
        ("cn-shanghai-2", "上海2"),
        ("cn-guangzhou-1", "广州1"),
        ("cn-hongkong-2", "香港2"),
        ("cn-qingdao-1", "青岛1"),
        ("cn-tianjin-1", "天津1"),
        ("cn-wuhan-1", "武汉1"),
        ("cn-chengdu-1", "成都1"),
        ("cn-xian-1", "西安1"),
        ("cn-nanjing-1", "南京1"),
        ("cn-fuzhou-1", "福州1"),
        ("cn-shenzhen-1", "深圳1"),
        ("cn-zhengzhou-1", "郑州1"),
        ("cn-suzhou-1", "苏州1"),
        ("cn-shijiazhuang-1", "石家庄1"),
        ("cn-hefei-1", "合肥1"),
        ("ap-singapore-1", "新加坡1"),
        ("ap-bangkok-1", "曼谷1"),
        ("eu-moscow-1", "莫斯科1"),
        ("us-washington-1", "华盛顿1"),
    };

    /// <summary>使用 HMAC-SHA256 对请求参数签名（金山云部分 OpenAPI 使用类似方式，若需 SigV4 可替换此实现）。</summary>
    private static string SignRequest(string secretKey, SortedDictionary<string, string> paramsDict)
    {
        var sb = new StringBuilder();
        foreach (var kv in paramsDict)
        {
            if (sb.Length > 0) sb.Append('&');
            sb.Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value ?? ""));
        }
        var stringToSign = sb.ToString();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    /// <summary>调用金山云 KEC 单次 API（GET 带签名）。</summary>
    private static async Task<string> CallApiAsync(
        HttpClient httpClient,
        string accessKeyId,
        string accessKeySecret,
        string action,
        string region,
        Dictionary<string, string>? extraParams,
        CancellationToken cancellationToken)
    {
        var dict = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["Action"] = action,
            ["Version"] = ApiVersion,
            ["Region"] = region,
            ["AccessKeyId"] = accessKeyId,
            ["SignatureMethod"] = "HMAC-SHA256",
            ["Timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };
        if (extraParams != null)
        {
            foreach (var kv in extraParams)
                dict[kv.Key] = kv.Value ?? "";
        }
        var signature = SignRequest(accessKeySecret, dict);
        dict["Signature"] = signature;

        var query = string.Join("&", dict.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        var url = $"{Endpoint}/?{query}";
        var resp = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"金山云 API 请求失败: {resp.StatusCode}, {body}");
        return body;
    }

    /// <summary>解析 DescribeInstances 响应中的实例列表（JSON 格式）。</summary>
    private static List<KingEcsInstance> ParseDescribeInstancesResponse(string regionId, string regionName, string json)
    {
        var list = new List<KingEcsInstance>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("InstancesSet", out var set) && set.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in set.EnumerateArray())
                {
                    var instanceId = item.TryGetProperty("InstanceId", out var idNode) ? idNode.GetString() : null;
                    if (string.IsNullOrEmpty(instanceId)) continue;

                    var name = item.TryGetProperty("InstanceName", out var nameNode) ? nameNode.GetString() : instanceId;
                    string? publicIp = null;
                    string? privateIp = null;
                    if (item.TryGetProperty("NetworkInterfaceSet", out var netSet) && netSet.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var net in netSet.EnumerateArray())
                        {
                            if (net.TryGetProperty("PublicIp", out var pub) && !string.IsNullOrEmpty(pub.GetString()))
                                publicIp = pub.GetString();
                            if (net.TryGetProperty("PrivateIpAddress", out var priv) && !string.IsNullOrEmpty(priv.GetString()))
                                privateIp = priv.GetString();
                        }
                    }
                    if (string.IsNullOrEmpty(publicIp) && item.TryGetProperty("PublicIpAddress", out var pubAddr))
                        publicIp = pubAddr.GetString();
                    if (string.IsNullOrEmpty(privateIp) && item.TryGetProperty("PrivateIpAddress", out var privAddr))
                        privateIp = privAddr.GetString();

                    var osType = item.TryGetProperty("OsName", out var osNode) ? osNode.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(osType) && item.TryGetProperty("Image", out var img) && img.TryGetProperty("OsName", out var imgOs))
                        osType = imgOs.GetString() ?? "";
                    var isWin = string.Equals(osType, "windows", StringComparison.OrdinalIgnoreCase)
                        || (osType?.Contains("Windows", StringComparison.OrdinalIgnoreCase) == true);

                    list.Add(new KingEcsInstance(
                        regionId,
                        regionName,
                        instanceId,
                        name ?? instanceId,
                        publicIp,
                        privateIp,
                        osType,
                        isWin));
                }
            }
        }
        catch (JsonException)
        {
            // 响应格式可能与预期不同（如 API 返回 XML 或不同 JSON 结构）
        }
        return list;
    }

    /// <summary>拉取指定地域的 KEC 实例列表。</summary>
    private static async Task<List<KingEcsInstance>> ListInstancesInRegionAsync(
        HttpClient httpClient,
        string accessKeyId,
        string accessKeySecret,
        string regionId,
        string regionName,
        CancellationToken cancellationToken)
    {
        var json = await CallApiAsync(httpClient, accessKeyId, accessKeySecret, "DescribeInstances", regionId, null, cancellationToken).ConfigureAwait(false);
        return ParseDescribeInstancesResponse(regionId, regionName, json);
    }

    /// <summary>拉取所有地域的 KEC 实例，多地域并行拉取，报告进度并支持取消。</summary>
    public static List<KingEcsInstance> ListAllInstances(
        string accessKeyId,
        string accessKeySecret,
        IProgress<(string message, int current, int total)>? progress,
        CancellationToken cancellationToken = default)
    {
        var bag = new ConcurrentBag<KingEcsInstance>();
        var total = KecRegions.Count;
        var completed = 0;
        const int maxParallel = 6;
        var options = new ParallelOptions { MaxDegreeOfParallelism = maxParallel, CancellationToken = cancellationToken };

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(60);
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");

        try
        {
            Parallel.ForEach(KecRegions, options, (region) =>
            {
                var (regionId, regionName) = region;
                try
                {
                    var list = ListInstancesInRegionAsync(
                        httpClient,
                        accessKeyId,
                        accessKeySecret,
                        regionId,
                        regionName,
                        cancellationToken).GetAwaiter().GetResult();
                    foreach (var ins in list)
                        bag.Add(ins);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"金山云 KEC 地域 {regionId}({regionName}) 拉取失败: {ex.Message}");
                }
                var c = Interlocked.Increment(ref completed);
                progress?.Report(($"正在拉取金山云 KEC ({c}/{total} 地域)…", c, total));
            });
        }
        catch (OperationCanceledException)
        {
            progress?.Report(("已取消", completed, total));
            return bag.ToList();
        }

        progress?.Report(("拉取完成", total, total));
        return bag.ToList();
    }
}
