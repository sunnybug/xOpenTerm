using System.Diagnostics;
using System.IO;
using xOpenTerm.Models;

namespace xOpenTerm.Services;

/// <summary>启动 Windows 远程桌面（mstsc）：临时 .rdp 文件 + 可选 cmdkey 写入凭据</summary>
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
        var domain = ""; // RDP 不用域
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

        var rdpContent = "screen mode id:i:2\nuse multimon:i:0\n"
            + $"full address:s:{host}:{port}\n";
        if (!string.IsNullOrEmpty(username))
            rdpContent += $"username:s:{username}\n";
        if (!string.IsNullOrEmpty(domain))
            rdpContent += $"domain:s:{domain}\n";

        var tempDir = Path.GetTempPath();
        var rdpPath = Path.Combine(tempDir, $"xOpenTerm_{node.Id.Replace("-", "_")}.rdp");
        File.WriteAllText(rdpPath, rdpContent);

        Process.Start(new ProcessStartInfo
        {
            FileName = "mstsc",
            Arguments = $"\"{rdpPath}\"",
            UseShellExecute = true
        });
    }
}
