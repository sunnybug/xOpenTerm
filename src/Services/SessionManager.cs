using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;
using SshNet.Agent;
using xOpenTerm.Models;

namespace xOpenTerm.Services;

/// <summary>管理 SSH 与本地终端会话，向 UI 推送输出</summary>
public class SessionManager
{
    /// <summary>在远程主机上执行单条命令并返回标准输出（用于状态栏统计等）。支持直连与跳板链。执行后即断开。失败时 FailureReason 为具体原因（认证失败/连接失败等）。</summary>
    /// <param name="connectionTimeout">连接超时，null 表示使用库默认。</param>
    public static async Task<(string? Output, string? FailureReason)> RunSshCommandAsync(
        string host, ushort port, string username,
        string? password, string? keyPath, string? keyPassphrase,
        List<JumpHop>? jumpChain, bool useAgent,
        string command, CancellationToken cancellationToken = default,
        TimeSpan? connectionTimeout = null)
    {
        // 在开始操作前检查取消
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            // 在后台线程开始时再次检查取消
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (jumpChain == null || jumpChain.Count == 0)
                    return RunSshCommandDirect(host, port, username, password, keyPath, keyPassphrase, useAgent, command, connectionTimeout, cancellationToken);

                string connectHost = jumpChain[0].Host;
                var connectPort = (uint)jumpChain[0].Port;
                var chainDisposables = new List<IDisposable>();

                for (var i = 0; i < jumpChain.Count; i++)
                {
                    // 每个跳板机连接前检查取消
                    cancellationToken.ThrowIfCancellationRequested();

                    var hop = jumpChain[i];
                    var (conn, authFailure) = CreateConnectionInfo(connectHost, (ushort)connectPort, hop.Username, hop.Password, hop.KeyPath, hop.KeyPassphrase, hop.UseAgent, connectionTimeout);
                    if (conn == null) return (null, "跳板机连接失败：" + (authFailure ?? "未配置认证方式（请配置密码、私钥或 SSH Agent）"));
                    var client = new SshClient(conn);
                    AcceptAnyHostKey(client);
                    try
                    {
                        client.Connect();
                    }
                    catch (Exception ex)
                    {
                        if (ex is SshConnectionException && ex.Message.Contains("Too many authentication failures", StringComparison.OrdinalIgnoreCase))
                            throw;
                        return (null, "跳板机连接失败：" + ClassifyConnectException(ex));
                    }
                    chainDisposables.Add(client);
                    var nextHost = i + 1 < jumpChain.Count ? jumpChain[i + 1].Host : host;
                    var nextPort = (uint)(i + 1 < jumpChain.Count ? jumpChain[i + 1].Port : port);
                    var fwd = new ForwardedPortLocal("127.0.0.1", 0, nextHost, nextPort);
                    client.AddForwardedPort(fwd);
                    fwd.Start();
                    chainDisposables.Add(fwd);
                    connectHost = "127.0.0.1";
                    connectPort = fwd.BoundPort;
                }

                try
                {
                    return RunSshCommandDirect(connectHost, (ushort)connectPort, username, password, keyPath, keyPassphrase, useAgent, command, connectionTimeout, cancellationToken);
                }
                finally
                {
                    for (var i = chainDisposables.Count - 1; i >= 0; i--)
                    {
                        try { chainDisposables[i]?.Dispose(); } catch { }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw; // 重新抛出取消异常
            }
            catch (Exception ex)
            {
                if (ex is SshConnectionException && ex.Message.Contains("Too many authentication failures", StringComparison.OrdinalIgnoreCase))
                    throw;
                return (null, ClassifyConnectException(ex));
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>将 Connect 阶段异常归类为用户可读的失败原因（认证失败 vs 连接失败等）。</summary>
    private static string ClassifyConnectException(Exception ex)
    {
        var msg = ex.Message ?? "";
        if (ex is SocketException sock)
        {
            return sock.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => "连接被拒绝（端口不通或未开放，请检查主机、端口与防火墙）。",
                SocketError.TimedOut => "连接超时（请检查网络与主机是否可达）。",
                SocketError.HostNotFound => "无法解析主机名（请检查主机地址）。",
                _ => $"网络错误：{sock.SocketErrorCode}（{msg}）。"
            };
        }
        if (ex is SshAuthenticationException)
        {
            if (msg.Contains("No suitable authentication method", StringComparison.OrdinalIgnoreCase))
                return "认证失败：服务器不接受当前认证方式（请检查密码或密钥）。";
            return "认证失败：密码错误或密钥未被接受。" + (string.IsNullOrEmpty(msg) ? "" : " " + msg);
        }
        if (ex is SshConnectionException)
        {
            if (msg.Contains("server response does not contain ssh protocol identification", StringComparison.OrdinalIgnoreCase))
                return "端口不通或非 SSH 服务（该端口可能被其他服务占用）。";
            return "连接异常：" + (string.IsNullOrEmpty(msg) ? "请检查主机与端口。" : msg);
        }
        if (ex is TimeoutException)
            return "连接超时（请检查网络与主机是否可达）。";
        return "连接失败：" + (string.IsNullOrEmpty(msg) ? ex.GetType().Name : msg);
    }

    private static (string? output, string? failureReason) RunSshCommandDirect(string host, ushort port, string username,
        string? password, string? keyPath, string? keyPassphrase, bool useAgent, string command,
        TimeSpan? connectionTimeout = null, CancellationToken cancellationToken = default)
    {
        // 连接前检查取消
        cancellationToken.ThrowIfCancellationRequested();

        var timeoutSec = connectionTimeout?.TotalSeconds.ToString("0") ?? "null";
        var commandOneLine = string.IsNullOrEmpty(command) ? "" : command.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
        ExceptionLog.WriteInfo($"[RunSshCommandDirect] 开始 host={host} port={port} connectionTimeout={timeoutSec}s command=[{commandOneLine}]");
        var totalSw = Stopwatch.StartNew();

        var (conn, authFailure) = CreateConnectionInfo(host, port, username, password, keyPath, keyPassphrase, useAgent, connectionTimeout);
        if (conn == null)
        {
            ExceptionLog.WriteInfo($"[RunSshCommandDirect] CreateConnectionInfo 返回 null host={host} reason={authFailure}");
            return (null, authFailure ?? "未配置认证方式（请配置密码、私钥或 SSH Agent）");
        }
        using var client = new SshClient(conn);
        AcceptAnyHostKey(client);
        var connectSw = Stopwatch.StartNew();
        try
        {
            // 连接前再次检查取消
            cancellationToken.ThrowIfCancellationRequested();

            client.Connect();
            connectSw.Stop();
            ExceptionLog.WriteInfo($"[RunSshCommandDirect] Connect 完成 host={host} 耗时={connectSw.ElapsedMilliseconds}ms");
        }
        catch (OperationCanceledException)
        {
            connectSw.Stop();
            ExceptionLog.WriteInfo($"[RunSshCommandDirect] Connect 被取消 host={host} 耗时={connectSw.ElapsedMilliseconds}ms");
            throw;
        }
        catch (Exception ex)
        {
            connectSw.Stop();
            ExceptionLog.WriteInfo($"[RunSshCommandDirect] Connect 异常 host={host} 耗时={connectSw.ElapsedMilliseconds}ms");
            // 认证失败次数过多时不再重试，让调用方弹窗并停止轮询
            if (ex is SshConnectionException && ex.Message.Contains("Too many authentication failures", StringComparison.OrdinalIgnoreCase))
            {
                ExceptionLog.WriteInfo($"[RunSshCommandDirect] 连接失败 host={host}：Too many authentication failures。说明：使用 Agent 时会按顺序尝试所有密钥，服务器通常限制约 6 次尝试。建议减少 Agent 中的密钥数量或改用指定私钥。");
                ExceptionLog.Write(ex, $"[RunSshCommandDirect] Too many authentication failures host={host}", toCrashLog: false);
                throw;
            }
            ExceptionLog.Write(ex, $"[RunSshCommandDirect] host={host}", toCrashLog: false);
            return (null, ClassifyConnectException(ex));
        }
        try
        {
            // 命令执行前检查取消
            cancellationToken.ThrowIfCancellationRequested();

            var cmdSw = Stopwatch.StartNew();
            ExceptionLog.WriteInfo($"[RunSshCommandDirect] RunCommand 开始 host={host} command=[{commandOneLine}]");
            using var cmd = client.RunCommand(command);
            var result = cmd.Result;
            cmdSw.Stop();
            totalSw.Stop();
            ExceptionLog.WriteInfo($"[RunSshCommandDirect] RunCommand 完成 host={host} 命令耗时={cmdSw.ElapsedMilliseconds}ms 总耗时={totalSw.ElapsedMilliseconds}ms 输出长度={result?.Length ?? 0}");
            return (result, null);
        }
        catch (OperationCanceledException)
        {
            totalSw.Stop();
            ExceptionLog.WriteInfo($"[RunSshCommandDirect] RunCommand 被取消 host={host} 总耗时={totalSw.ElapsedMilliseconds}ms");
            throw;
        }
        catch (Exception ex)
        {
            totalSw.Stop();
            ExceptionLog.WriteInfo($"[RunSshCommandDirect] RunCommand 异常 host={host} 总耗时={totalSw.ElapsedMilliseconds}ms");
            ExceptionLog.Write(ex, $"[RunSshCommandDirect] RunCommand host={host}", toCrashLog: false);
            return (null, "命令执行失败：" + (ex.Message ?? ex.GetType().Name));
        }
        finally
        {
            try { client.Disconnect(); } catch { }
        }
    }
    private readonly ConcurrentDictionary<string, ISessionHandle> _sessions = new();

    /// <summary>仅单元测试时为 true（由测试 SetUpFixture 设置 XOPENTERM_UNIT_TEST=1）。为 true 时自动接受/忽略 host key 检查。</summary>
    internal static bool IsUnitTestMode => Environment.GetEnvironmentVariable("XOPENTERM_UNIT_TEST") == "1";

    /// <summary>在连接前自动接受服务器 host key，避免首次连接或 known_hosts 未包含时连接失败。仅单元测试环境下生效；正式运行不自动忽略 host key。供 CreateConnectionInfo 的调用方在 new SshClient/SftpClient 后、Connect 前调用。</summary>
    internal static void AcceptAnyHostKey(BaseClient client)
    {
        if (client == null || !IsUnitTestMode) return;
        client.HostKeyReceived += (_, e) => e.CanTrust = true;
    }

    /// <summary>供 RemoteFileService 等复用：创建 SSH/SFTP 连接信息。失败时 failureReason 为可展示给用户的原因。</summary>
    /// <param name="connectionTimeout">连接超时，null 表示使用库默认。</param>
    internal static (ConnectionInfo? conn, string? failureReason) CreateConnectionInfo(string host, ushort port, string username, string? password, string? keyPath, string? keyPassphrase, bool useAgent = false, TimeSpan? connectionTimeout = null)
    {
        ConnectionInfo? conn;
        string? failureReason = null;
        if (useAgent)
        {
            conn = CreateConnectionInfoWithAgent(host, port, username, connectionTimeout);
            if (conn == null)
                failureReason = "已选择 SSH Agent，但 Agent 未运行或无可用的密钥";
        }
        else if (!string.IsNullOrEmpty(keyPath))
        {
            var keyFile = new PrivateKeyFile(keyPath, keyPassphrase);
            conn = new ConnectionInfo(host, (int)port, username, new PrivateKeyAuthenticationMethod(username, keyFile));
        }
        else if (!string.IsNullOrEmpty(password))
        {
            conn = new ConnectionInfo(host, (int)port, username, new PasswordAuthenticationMethod(username, password));
        }
        else
        {
            conn = null;
            failureReason = "未配置认证方式（请配置密码、私钥或 SSH Agent）";
        }
        if (conn != null && connectionTimeout.HasValue)
            conn.Timeout = connectionTimeout.Value;
        return (conn, conn == null ? failureReason : null);
    }

    /// <summary>使用 SSH Agent（OpenSSH 或 PuTTY Pageant）创建连接信息。</summary>
    private static ConnectionInfo? CreateConnectionInfoWithAgent(string host, int port, string username, TimeSpan? connectionTimeout = null)
    {
        SshAgentPrivateKey[]? keys = null;
        string? agentSource = null;
        try
        {
            // 先尝试 OpenSSH Agent（Windows 上为 openssh-ssh-agent 或 SSH_AUTH_SOCK）
            var sshAgent = new SshAgent();
            keys = sshAgent.RequestIdentities();
            if (keys is { Length: > 0 })
            {
                agentSource = "OpenSSH Agent";
                LogAgentKeyAttempt(host, port, username, agentSource, keys);
                var conn = new ConnectionInfo(host, port, username, new PrivateKeyAuthenticationMethod(username, keys));
                if (connectionTimeout.HasValue) conn.Timeout = connectionTimeout.Value;
                return conn;
            }
        }
        catch
        {
            // OpenSSH Agent 不可用（未运行或非 Windows 默认管道）
        }

        try
        {
            // 再尝试 PuTTY Pageant（Windows 上常用）
            var pageant = new Pageant();
            keys = pageant.RequestIdentities();
            if (keys is { Length: > 0 })
            {
                agentSource = "PuTTY Pageant";
                LogAgentKeyAttempt(host, port, username, agentSource, keys);
                var conn = new ConnectionInfo(host, port, username, new PrivateKeyAuthenticationMethod(username, keys));
                if (connectionTimeout.HasValue) conn.Timeout = connectionTimeout.Value;
                return conn;
            }
        }
        catch
        {
            // Pageant 未运行
        }

        return null;
    }

    /// <summary>将 Agent 密钥尝试过程写入日志（密钥数量、顺序、类型/算法/指纹等详细信息及 Too many authentication failures 提示）。</summary>
    private static void LogAgentKeyAttempt(string host, int port, string username, string agentSource, SshAgentPrivateKey[] keys)
    {
        var n = keys.Length;
        ExceptionLog.WriteInfo($"[SSH Agent] host={host} port={port} user={username} 使用 {agentSource}，共 {n} 个密钥，将按顺序依次尝试。服务器通常限制约 6 次认证尝试，若密钥过多可能出现 Too many authentication failures。");
        for (var i = 0; i < keys.Length; i++)
        {
            var detail = DescribeAgentKey(keys[i], i + 1, n);
            ExceptionLog.WriteInfo($"[SSH Agent] 密钥 {i + 1}/{n}: {detail}");
        }
    }

    /// <summary>通过反射（含私有字段）提取 Agent 密钥的 Comment 与公钥字节，生成指纹与算法信息以便区分是哪个 key。</summary>
    private static string DescribeAgentKey(SshAgentPrivateKey key, int indexOneBased, int total)
    {
        var sb = new StringBuilder();
        var type = key.GetType();
        sb.Append($"key[{indexOneBased - 1}] 类型={type.Name}");
        string? comment = null;
        string? fingerprint = null;
        string? algorithm = null;
        try
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var mi in type.GetFields(flags).Cast<MemberInfo>().Concat(type.GetProperties(flags).Cast<MemberInfo>()))
            {
                object? value = null;
                var name = mi.Name;
                if (mi is FieldInfo fi)
                {
                    try { value = fi.GetValue(key); }
                    catch { continue; }
                }
                else if (mi is PropertyInfo pi && pi.GetIndexParameters().Length == 0)
                {
                    try { value = pi.GetValue(key); }
                    catch { continue; }
                }
                if (value == null) continue;
                if (value is string s && !string.IsNullOrEmpty(s) && (name.IndexOf("Comment", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("comment", StringComparison.OrdinalIgnoreCase) >= 0))
                    comment = s;
                if (value is byte[] bytes && bytes.Length > 20)
                {
                    try
                    {
                        var algo = TryGetSshKeyAlgorithmFromBlob(bytes);
                        var fp = Convert.ToBase64String(SHA256.HashData(bytes)).TrimEnd('=');
                        if (!string.IsNullOrEmpty(algo) && algo.StartsWith("ssh-", StringComparison.Ordinal))
                        {
                            fingerprint = fp;
                            algorithm = algo;
                        }
                        else if (string.IsNullOrEmpty(fingerprint))
                            fingerprint = fp;
                    }
                    catch { }
                }
            }
            if (!string.IsNullOrEmpty(comment))
                sb.Append($" Comment=\"{comment}\"");
            if (!string.IsNullOrEmpty(algorithm))
                sb.Append($" 算法={algorithm}");
            if (!string.IsNullOrEmpty(fingerprint))
                sb.Append($" SHA256:{fingerprint}");
            if (string.IsNullOrEmpty(comment) && string.IsNullOrEmpty(fingerprint))
                sb.Append(" (未解析到 Comment/公钥字节，请检查 SshNet.Agent 版本或实现)");
        }
        catch (Exception ex)
        {
            sb.Append($" (DescribeAgentKey异常:{ex.Message})");
        }
        return sb.ToString();
    }

    /// <summary>从 SSH 公钥 blob（agent 格式）前若干字节解析算法名（如 ssh-ed25519、ssh-rsa）。</summary>
    private static string? TryGetSshKeyAlgorithmFromBlob(byte[] blob)
    {
        try
        {
            if (blob.Length < 4) return null;
            var len = (blob[0] << 24) | (blob[1] << 16) | (blob[2] << 8) | blob[3];
            if (len <= 0 || len > 200 || 4 + len > blob.Length) return null;
            return Encoding.UTF8.GetString(blob, 4, len);
        }
        catch { return null; }
    }

    public void WriteToSession(string sessionId, string data)
    {
        if (_sessions.TryGetValue(sessionId, out var h))
            h.Write(data);
    }

    public void ResizeSession(string sessionId, ushort rows, ushort cols)
    {
        if (_sessions.TryGetValue(sessionId, out var h))
            h.Resize(rows, cols);
    }

    public void CloseSession(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var h))
            h.Close();
    }

    /// <summary>关闭所有会话（退出时调用，确保子进程/连接释放后进程能退出）。</summary>
    public void CloseAllSessions()
    {
        var ids = _sessions.Keys.ToList();
        ExceptionLog.WriteInfo($"CloseAllSessions 开始, 会话数={ids.Count}");
        foreach (var sessionId in ids)
        {
            if (_sessions.TryRemove(sessionId, out var h))
            {
                try
                {
                    ExceptionLog.WriteInfo($"CloseAllSessions 关闭会话: {sessionId}");
                    h.Close();
                }
                catch (Exception ex)
                {
                    ExceptionLog.Write(ex, "CloseAllSessions 关闭会话异常: " + sessionId);
                }
            }
        }
        ExceptionLog.WriteInfo("CloseAllSessions 结束");
    }

    public bool HasSession(string sessionId) => _sessions.ContainsKey(sessionId);
}

internal interface ISessionHandle
{
    void Write(string data);
    void Resize(ushort rows, ushort cols);
    void Close();
    event EventHandler? Closed;
}


