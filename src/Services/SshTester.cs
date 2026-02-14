using System.Net.Sockets;
using System.Text.RegularExpressions;
using Renci.SshNet;
using Renci.SshNet.Common;
using xOpenTerm.Models;

namespace xOpenTerm.Services;

/// <summary>SSH 连接测试（用于节点/凭证/隧道中的“测试连接”按钮）</summary>
public static class SshTester
{
    /// <summary>测试结果：Success 为 true 表示成功，否则 FailureReason 为具体失败原因。</summary>
    public record TestResult(bool Success, string? FailureReason);

    /// <param name="logContext">可选，日志来源标识（如「节点设置」「隧道编辑」），便于在 log 中区分。</param>
    public static TestResult Test(string host, ushort port, string username,
        string? password, string? keyPath, string? keyPassphrase, bool useAgent = false, string? logContext = null)
    {
        var prefix = string.IsNullOrEmpty(logContext) ? "[SshTester]" : $"[SshTester][{logContext}]";
        var authDesc = useAgent ? "Agent" : !string.IsNullOrEmpty(keyPath) ? "单密钥" : !string.IsNullOrEmpty(password) ? "密码" : "无";
        ExceptionLog.WriteInfo($"{prefix} Test 开始 host={host} port={port} user={username} 认证方式={authDesc}" + (keyPath != null ? $" keyPath={keyPath}" : ""));
        try
        {
            ConnectionInfo? conn;
            if (useAgent)
            {
                conn = SessionManager.CreateConnectionInfo(host, port, username, null, null, null, true);
            }
            else if (!string.IsNullOrEmpty(keyPath))
            {
                var keyFile = new PrivateKeyFile(keyPath, keyPassphrase);
                conn = new ConnectionInfo(host, port, username, new PrivateKeyAuthenticationMethod(username, keyFile));
            }
            else if (!string.IsNullOrEmpty(password))
            {
                conn = new ConnectionInfo(host, port, username, new PasswordAuthenticationMethod(username, password));
            }
            else
            {
                return new TestResult(false, "未配置认证方式（请填写密码或选择密钥/Agent）。");
            }
            if (conn == null)
                return new TestResult(false, "无法创建连接（如使用 Agent 请确认已启动 Pageant 等）。");
            using var client = new SshClient(conn);
            SessionManager.AcceptAnyHostKey(client);
            ExceptionLog.WriteInfo($"{prefix} 开始 Connect host={host} port={port}");
            client.Connect();
            client.Disconnect();
            ExceptionLog.WriteInfo($"{prefix} Test 成功 host={host} port={port}");
            return new TestResult(true, null);
        }
        catch (SocketException ex)
        {
            return new TestResult(false, ex.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => "端口不通或连接被拒绝（请检查主机、端口与防火墙）。",
                SocketError.TimedOut => "连接超时（请检查网络与主机是否可达）。",
                SocketError.HostNotFound => "无法解析主机名（请检查主机地址）。",
                _ => $"网络错误：{ex.SocketErrorCode}（{ex.Message}）。"
            });
        }
        catch (SshAuthenticationException ex)
        {
            var msg = ex.Message ?? "";
            // 常见：Please login as the user "ubuntu" rather than the user "root".
            var match = Regex.Match(msg, @"[Pp]lease\s+login\s+as\s+the\s+user\s+[""']([^""']+)[""']\s+rather\s+than", RegexOptions.IgnoreCase);
            if (match.Success)
                return new TestResult(false, $"请使用用户 \"{match.Groups[1].Value}\" 登录，不能使用当前用户名。");
            if (msg.Contains("No suitable authentication method", StringComparison.OrdinalIgnoreCase))
                return new TestResult(false, "认证失败：服务器不接受当前认证方式（请检查密码或密钥）。");
            return new TestResult(false, "认证失败：密码错误或密钥未被接受。" + (string.IsNullOrEmpty(msg) ? "" : " " + msg));
        }
        catch (SshConnectionException ex)
        {
            var msg = ex.Message ?? "";
            if (msg.Contains("Too many authentication failures", StringComparison.OrdinalIgnoreCase))
            {
                ExceptionLog.WriteInfo($"{prefix} 连接失败 host={host} port={port}：Too many authentication failures。说明：使用 Agent 时会按顺序尝试所有密钥，服务器通常限制约 6 次尝试，密钥过多会导致被断开。建议减少 Agent 中的密钥数量或改用指定私钥。");
                ExceptionLog.Write(ex, $"{prefix} Too many authentication failures host={host} port={port}", toCrashLog: false);
            }
            if (msg.Contains("server response does not contain ssh protocol identification", StringComparison.OrdinalIgnoreCase))
                return new TestResult(false, "端口不通或非 SSH 服务（该端口可能被其他服务占用）。");
            return new TestResult(false, "连接异常：" + (string.IsNullOrEmpty(msg) ? "请检查主机与端口。" : msg));
        }
        catch (TimeoutException)
        {
            return new TestResult(false, "连接超时（请检查网络与主机是否可达）。");
        }
        catch (Exception ex)
        {
            return new TestResult(false, "连接失败：" + (ex.Message ?? ex.GetType().Name));
        }
    }
}
