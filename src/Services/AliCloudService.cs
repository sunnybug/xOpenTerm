using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

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

/// <summary>阿里云轻量应用服务器实例信息，用于构建节点树（地域→服务器）。</summary>
public record AliSwasInstance(
    string RegionId,
    string RegionName,
    string InstanceId,
    string InstanceName,
    string? PublicIp,
    string? PrivateIp,
    string? OsName,
    bool IsWindows);

/// <summary>拉取阿里云 ECS 实例列表，按地域分页，支持进度与取消。</summary>
public static class AliCloudService
{
    /// <summary>常见 ECS 地域（可扩展）。</summary>
    private static readonly (string RegionId, string Name)[] Regions =
    {
        ("cn-hangzhou", "杭州"),
        ("cn-shanghai", "上海"),
        ("cn-beijing", "北京"),
        ("cn-shenzhen", "深圳"),
        ("cn-guangzhou", "广州"),
        ("cn-chengdu", "成都"),
        ("cn-hongkong", "香港"),
        ("ap-southeast-1", "新加坡"),
        ("ap-southeast-2", "悉尼"),
        ("ap-southeast-3", "吉隆坡"),
        ("ap-southeast-5", "雅加达"),
        ("ap-northeast-1", "东京"),
        ("us-east-1", "弗吉尼亚"),
        ("us-west-1", "硅谷"),
    };

    /// <summary>阿里云 ECS API 版本。</summary>
    private const string ApiVersion = "2014-05-26";

    /// <summary>阿里云 API 签名算法（使用 HMAC-SHA1）。</summary>
    private static string Sign(string accessKeySecret, string stringToSign)
    {
        var key = Encoding.UTF8.GetBytes(accessKeySecret + "&");
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
        return Convert.ToBase64String(hash);
    }

    /// <summary>构建阿里云 API 请求 URL（包含签名）。</summary>
    private static string BuildRequestUrl(
        string accessKeyId,
        string accessKeySecret,
        string action,
        Dictionary<string, string> parameters,
        string regionId)
    {
        // 公共参数
        var dict = new Dictionary<string, string>
        {
            { "Action", action },
            { "Version", ApiVersion },
            { "AccessKeyId", accessKeyId },
            { "SignatureMethod", "HMAC-SHA1" },
            { "SignatureVersion", "1.0" },
            { "SignatureNonce", Guid.NewGuid().ToString() },
            { "Timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") },
            { "RegionId", regionId }
        };

        // 合并业务参数
        foreach (var kvp in parameters)
            dict[kvp.Key] = kvp.Value;

        // 按参数名字典序排序
        var sortedParams = dict.OrderBy(x => x.Key).ToList();

        // 构建查询字符串
        var queryBuilder = new StringBuilder();
        foreach (var kvp in sortedParams)
        {
            if (queryBuilder.Length > 0)
                queryBuilder.Append('&');
            queryBuilder.Append(Uri.EscapeDataString(kvp.Key));
            queryBuilder.Append('=');
            queryBuilder.Append(Uri.EscapeDataString(kvp.Value));
        }

        // 计算签名
        var stringToSign = "GET&%2F&" + Uri.EscapeDataString(queryBuilder.ToString());
        var signature = Sign(accessKeySecret, stringToSign);

        // 添加签名参数
        queryBuilder.Append("&Signature=");
        queryBuilder.Append(Uri.EscapeDataString(signature));

        return $"https://ecs.aliyuncs.com/?{queryBuilder}";
    }

    /// <summary>发送 HTTP GET 请求并解析 JSON 响应。</summary>
    private static async Task<JsonDocument> SendRequestAsync(
        string accessKeyId,
        string accessKeySecret,
        string action,
        Dictionary<string, string> parameters,
        string regionId,
        System.Threading.CancellationToken cancellationToken)
    {
        var url = BuildRequestUrl(accessKeyId, accessKeySecret, action, parameters, regionId);
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        var response = await client.GetAsync(url, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(content);
    }

    /// <summary>拉取指定地域的 ECS 实例列表（支持分页）。</summary>
    private static async Task<List<AliEcsInstance>> ListRegionInstancesAsync(
        string accessKeyId,
        string accessKeySecret,
        string regionId,
        System.Threading.CancellationToken cancellationToken)
    {
        var list = new List<AliEcsInstance>();
        var pageNumber = 1;
        const int pageSize = 100;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var parameters = new Dictionary<string, string>
            {
                { "PageNumber", pageNumber.ToString() },
                { "PageSize", pageSize.ToString() }
            };

            var json = await SendRequestAsync(accessKeyId, accessKeySecret, "DescribeInstances", parameters, regionId, cancellationToken);
            var root = json.RootElement;

            // 检查错误
            if (root.TryGetProperty("Code", out var code) && code.GetString() != null)
            {
                var message = root.GetProperty("Message").GetString();
                throw new Exception($"阿里云 API 错误: {code.GetString()} - {message}");
            }

            if (!root.TryGetProperty("Instances", out var instances) ||
                !instances.TryGetProperty("Instance", out var instanceArray))
                break;

            foreach (var inst in instanceArray.EnumerateArray())
            {
                var instanceId = inst.GetProperty("InstanceId").GetString() ?? "";
                var instanceName = inst.GetProperty("InstanceName").GetString() ?? instanceId;
                var osName = inst.TryGetProperty("OSName", out var osNameElem) ? osNameElem.GetString() ?? "" : "";
                var isWin = osName.Contains("Windows", StringComparison.OrdinalIgnoreCase);

                // 提取公网 IP
                string? publicIp = null;
                if (inst.TryGetProperty("PublicIpAddress", out var publicIpElem) &&
                    publicIpElem.TryGetProperty("IpAddress", out var ipArray))
                {
                    publicIp = ipArray.EnumerateArray().FirstOrDefault().GetString();
                }

                // 提取私网 IP
                string? privateIp = null;
                if (inst.TryGetProperty("VpcAttributes", out var vpcAttr) &&
                    vpcAttr.TryGetProperty("PrivateIpAddress", out var privateIpElem))
                {
                    privateIp = privateIpElem.GetString();
                }
                else if (inst.TryGetProperty("InnerIpAddress", out var innerIpElem) &&
                    innerIpElem.TryGetProperty("IpAddress", out var innerIpArray))
                {
                    privateIp = innerIpArray.EnumerateArray().FirstOrDefault().GetString();
                }

                list.Add(new AliEcsInstance(
                    regionId,
                    regionId,
                    instanceId,
                    instanceName,
                    publicIp,
                    privateIp,
                    osName,
                    isWin));
            }

            // 检查是否还有更多页
            var totalCount = root.TryGetProperty("TotalCount", out var totalCountElem) ? totalCountElem.GetInt32() : 0;
            if (list.Count >= totalCount)
                break;

            pageNumber++;
        }

        return list;
    }

    /// <summary>拉取所有地域的实例，报告进度并支持取消。</summary>
    public static List<AliEcsInstance> ListInstances(
        string accessKeyId,
        string accessKeySecret,
        IProgress<(string message, int current, int total)>? progress,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var list = new List<AliEcsInstance>();
        var totalRegions = Regions.Length;
        var current = 0;

        foreach (var (regionId, regionName) in Regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(($"正在拉取 {regionName} ({regionId})…", current, totalRegions));

            try
            {
                var instances = Task.Run(() => ListRegionInstancesAsync(accessKeyId, accessKeySecret, regionId, cancellationToken), cancellationToken).GetAwaiter().GetResult();
                // 替换地域名称为中文
                var renamed = instances.Select(i => new AliEcsInstance(
                    i.RegionId,
                    regionName,
                    i.InstanceId,
                    i.InstanceName,
                    i.PublicIp,
                    i.PrivateIp,
                    i.OsName,
                    i.IsWindows));
                list.AddRange(renamed);
            }
            catch (Exception ex)
            {
                // 某个地域失败不影响其他地域
                System.Diagnostics.Debug.WriteLine($"拉取 {regionName} 失败: {ex.Message}");
            }

            current++;
        }

        progress?.Report(("拉取完成", totalRegions, totalRegions));
        return list;
    }

    /// <summary>常见轻量应用服务器地域（可扩展）。</summary>
    private static readonly (string RegionId, string Name)[] SwasRegions =
    {
        ("cn-hangzhou", "杭州"),
        ("cn-shanghai", "上海"),
        ("cn-beijing", "北京"),
        ("cn-shenzhen", "深圳"),
        ("cn-guangzhou", "广州"),
        ("cn-chengdu", "成都"),
        ("cn-hongkong", "香港"),
        ("ap-southeast-1", "新加坡"),
        ("ap-southeast-2", "悉尼"),
        ("ap-southeast-3", "吉隆坡"),
        ("ap-southeast-5", "雅加达"),
        ("ap-northeast-1", "东京"),
    };

    /// <summary>轻量应用服务器 API 版本。</summary>
    private const string SwasApiVersion = "2020-06-01";

    /// <summary>拉取指定地域的轻量应用服务器实例列表（支持分页）。</summary>
    private static async Task<List<AliSwasInstance>> ListSwasRegionInstancesAsync(
        string accessKeyId,
        string accessKeySecret,
        string regionId,
        System.Threading.CancellationToken cancellationToken)
    {
        var list = new List<AliSwasInstance>();
        var pageNumber = 1;
        const int pageSize = 100;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var parameters = new Dictionary<string, string>
            {
                { "PageNumber", pageNumber.ToString() },
                { "PageSize", pageSize.ToString() }
            };

            var json = await SendSwasRequestAsync(accessKeyId, accessKeySecret, "DescribeInstances", parameters, regionId, cancellationToken);
            var root = json.RootElement;

            // 检查错误
            if (root.TryGetProperty("Code", out var code) && code.GetString() != null)
            {
                var message = root.GetProperty("Message").GetString();
                throw new Exception($"阿里云轻量服务器 API 错误: {code.GetString()} - {message}");
            }

            if (!root.TryGetProperty("Instances", out var instances) ||
                !instances.TryGetProperty("Instance", out var instanceArray))
                break;

            foreach (var inst in instanceArray.EnumerateArray())
            {
                var instanceId = inst.GetProperty("InstanceId").GetString() ?? "";
                var instanceName = inst.GetProperty("InstanceName").GetString() ?? instanceId;
                var osName = inst.TryGetProperty("ImageInfo", out var imageInfo) &&
                    imageInfo.TryGetProperty("OsName", out var osNameElem)
                    ? osNameElem.GetString() ?? "" : "";
                var isWin = osName.Contains("Windows", StringComparison.OrdinalIgnoreCase);

                // 提取公网 IP
                string? publicIp = null;
                if (inst.TryGetProperty("PublicIPAddress", out var publicIpElem) &&
                    publicIpElem.ValueKind != System.Text.Json.JsonValueKind.Null)
                {
                    publicIp = publicIpElem.GetString();
                }

                // 提取私网 IP
                string? privateIp = null;
                if (inst.TryGetProperty("InnerIPAddress", out var privateIpElem) &&
                    privateIpElem.ValueKind != System.Text.Json.JsonValueKind.Null)
                {
                    privateIp = privateIpElem.GetString();
                }

                list.Add(new AliSwasInstance(
                    regionId,
                    regionId,
                    instanceId,
                    instanceName,
                    publicIp,
                    privateIp,
                    osName,
                    isWin));
            }

            // 检查是否还有更多页
            var totalCount = root.TryGetProperty("TotalCount", out var totalCountElem) ? totalCountElem.GetInt32() : 0;
            if (list.Count >= totalCount)
                break;

            pageNumber++;
        }

        return list;
    }

    /// <summary>发送轻量应用服务器 API 请求（使用 swas-open.aliyuncs.com）。</summary>
    private static async Task<JsonDocument> SendSwasRequestAsync(
        string accessKeyId,
        string accessKeySecret,
        string action,
        Dictionary<string, string> parameters,
        string regionId,
        System.Threading.CancellationToken cancellationToken)
    {
        // 公共参数
        var dict = new Dictionary<string, string>
        {
            { "Action", action },
            { "Version", SwasApiVersion },
            { "AccessKeyId", accessKeyId },
            { "SignatureMethod", "HMAC-SHA1" },
            { "SignatureVersion", "1.0" },
            { "SignatureNonce", Guid.NewGuid().ToString() },
            { "Timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") },
            { "RegionId", regionId }
        };

        // 合并业务参数
        foreach (var kvp in parameters)
            dict[kvp.Key] = kvp.Value;

        // 按参数名字典序排序
        var sortedParams = dict.OrderBy(x => x.Key).ToList();

        // 构建查询字符串
        var queryBuilder = new StringBuilder();
        foreach (var kvp in sortedParams)
        {
            if (queryBuilder.Length > 0)
                queryBuilder.Append('&');
            queryBuilder.Append(Uri.EscapeDataString(kvp.Key));
            queryBuilder.Append('=');
            queryBuilder.Append(Uri.EscapeDataString(kvp.Value));
        }

        // 计算签名
        var stringToSign = "GET&%2F&" + Uri.EscapeDataString(queryBuilder.ToString());
        var signature = Sign(accessKeySecret, stringToSign);

        // 添加签名参数
        queryBuilder.Append("&Signature=");
        queryBuilder.Append(Uri.EscapeDataString(signature));

        var url = $"https://swas-open.aliyuncs.com/?{queryBuilder}";
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        var response = await client.GetAsync(url, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(content);
    }

    /// <summary>拉取所有地域的轻量应用服务器实例，报告进度并支持取消。</summary>
    public static List<AliSwasInstance> ListSwasInstances(
        string accessKeyId,
        string accessKeySecret,
        IProgress<(string message, int current, int total)>? progress,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var list = new List<AliSwasInstance>();
        var totalRegions = SwasRegions.Length;
        var current = 0;

        foreach (var (regionId, regionName) in SwasRegions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(($"正在拉取轻量服务器 {regionName} ({regionId})…", current, totalRegions));

            try
            {
                var instances = Task.Run(() => ListSwasRegionInstancesAsync(accessKeyId, accessKeySecret, regionId, cancellationToken), cancellationToken).GetAwaiter().GetResult();
                // 替换地域名称为中文
                var renamed = instances.Select(i => new AliSwasInstance(
                    i.RegionId,
                    regionName,
                    i.InstanceId,
                    i.InstanceName,
                    i.PublicIp,
                    i.PrivateIp,
                    i.OsName,
                    i.IsWindows));
                list.AddRange(renamed);
            }
            catch (Exception ex)
            {
                // 某个地域失败不影响其他地域
                System.Diagnostics.Debug.WriteLine($"拉取轻量服务器 {regionName} 失败: {ex.Message}");
            }

            current++;
        }

        progress?.Report(("拉取完成", totalRegions, totalRegions));
        return list;
    }
}
