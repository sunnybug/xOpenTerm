using System.Diagnostics;
using System.IO;
using System.Text;
using xOpenTerm.Models;

namespace xOpenTerm.Services;

/// <summary>启动 Windows 远程桌面（mstsc）：临时 .rdp 文件 + 可选 cmdkey 写入凭据。参考 mRemoteNG 支持域、控制台会话、剪贴板、SmartSizing、RD Gateway。</summary>
public static class RdpLauncher
{
    public static void Launch(Node node)
    {
        if (node.Type != NodeType.rdp)
            throw new ArgumentException("节点类型不是 RDP");
        var config = node.Config ?? throw new InvalidOperationException("RDP 节点缺少配置");
        var host = config.Host?.Trim();
        if (string.IsNullOrEmpty(host))
            throw new InvalidOperationException("请填写 RDP 主机地址");
        var port = config.Port ?? 3389;
        var username = config.Username?.Trim() ?? "administrator";
        var domain = config.Domain?.Trim() ?? "";
        var password = config.Password;

        if (!string.IsNullOrEmpty(password))
        {
            var target = $"TERMSRV/{host}";
            var user = string.IsNullOrEmpty(domain) ? username : $"{domain}\\{username}";
            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = "cmdkey",
                    Arguments = $"/generic:{target} /user:{user} /pass:{password}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(start);
                p?.WaitForExit(3000);
            }
            catch
            {
                // 忽略 cmdkey 失败，继续用 mstsc
            }
        }

        var sb = new StringBuilder();
        sb.Append("screen mode id:i:2\nuse multimon:i:0\n");
        sb.Append($"full address:s:{host}:{port}\n");
        if (!string.IsNullOrEmpty(username))
            sb.Append($"username:s:{username}\n");
        if (!string.IsNullOrEmpty(domain))
            sb.Append($"domain:s:{domain}\n");

        // 参考 mRemoteNG：扩展选项
        if (config.RdpRedirectClipboard == true)
            sb.Append("redirectclipboard:i:1\n");
        sb.Append("smart sizing:i:1\n");
        if (config.RdpUseConsoleSession == true)
            sb.Append("administrativesession:i:1\n");

        // RD Gateway（参考 mRemoteNG RDGatewayUsageMethod）
        var gwMethod = config.RdpGatewayUsageMethod ?? 0;
        if (!string.IsNullOrWhiteSpace(config.RdpGatewayHostname) && gwMethod != 0)
        {
            sb.Append($"gatewayhostname:s:{config.RdpGatewayHostname!.Trim()}\n");
            sb.Append($"gatewayusagemethod:i:{gwMethod}\n");
            // gatewaycredentialssource: 0=用连接凭据 1=询问 2=智能卡 4=单独凭据
            var useConnCreds = config.RdpGatewayUseConnectionCredentials ?? 1;
            sb.Append($"gatewaycredentialssource:i:{(useConnCreds == 1 ? 0 : 4)}\n");
            if (useConnCreds != 1 && !string.IsNullOrEmpty(config.RdpGatewayUsername))
            {
                sb.Append($"gatewayusername:s:{config.RdpGatewayUsername}\n");
                if (!string.IsNullOrEmpty(config.RdpGatewayDomain))
                    sb.Append($"gatewaydomain:s:{config.RdpGatewayDomain}\n");
                if (!string.IsNullOrEmpty(config.RdpGatewayPassword))
                    sb.Append($"gatewaypassword:b:{Convert.ToBase64String(Encoding.Unicode.GetBytes(config.RdpGatewayPassword))}\n");
            }
        }

        var tempDir = Path.GetTempPath();
        var rdpPath = Path.Combine(tempDir, $"xOpenTerm_{node.Id.Replace("-", "_")}.rdp");
        File.WriteAllText(rdpPath, sb.ToString());

        Process.Start(new ProcessStartInfo
        {
            FileName = "mstsc",
            Arguments = $"\"{rdpPath}\"",
            UseShellExecute = true
        });
    }
}
