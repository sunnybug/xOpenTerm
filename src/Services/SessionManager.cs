using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using Renci.SshNet;
using Renci.SshNet.Common;
using SshNet.Agent;
using xOpenTerm.Models;

namespace xOpenTerm.Services;

/// <summary>管理 SSH 与本地终端会话，向 UI 推送输出</summary>
public class SessionManager
{
    /// <summary>在远程主机上执行单条命令并返回标准输出（用于状态栏统计等）。支持直连与跳板链。执行后即断开。</summary>
    /// <param name="connectionTimeout">连接超时，null 表示使用库默认。</param>
    public static async Task<string?> RunSshCommandAsync(
        string host, ushort port, string username,
        string? password, string? keyPath, string? keyPassphrase,
        List<JumpHop>? jumpChain, bool useAgent,
        string command, CancellationToken cancellationToken = default,
        TimeSpan? connectionTimeout = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (jumpChain == null || jumpChain.Count == 0)
                    return RunSshCommandDirect(host, port, username, password, keyPath, keyPassphrase, useAgent, command, connectionTimeout);

                string connectHost = jumpChain[0].Host;
                var connectPort = (uint)jumpChain[0].Port;
                var chainDisposables = new List<IDisposable>();

                for (var i = 0; i < jumpChain.Count; i++)
                {
                    var hop = jumpChain[i];
                    var conn = CreateConnectionInfo(connectHost, (ushort)connectPort, hop.Username, hop.Password, hop.KeyPath, hop.KeyPassphrase, hop.UseAgent, connectionTimeout);
                    if (conn == null) return null;
                    var client = new SshClient(conn);
                    AcceptAnyHostKey(client);
                    client.Connect();
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
                    return RunSshCommandDirect(connectHost, (ushort)connectPort, username, password, keyPath, keyPassphrase, useAgent, command, connectionTimeout);
                }
                finally
                {
                    for (var i = chainDisposables.Count - 1; i >= 0; i--)
                    {
                        try { chainDisposables[i]?.Dispose(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex is SshConnectionException && ex.Message.Contains("Too many authentication failures", StringComparison.OrdinalIgnoreCase))
                    throw;
                return null;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string? RunSshCommandDirect(string host, ushort port, string username,
        string? password, string? keyPath, string? keyPassphrase, bool useAgent, string command,
        TimeSpan? connectionTimeout = null)
    {
        var timeoutSec = connectionTimeout?.TotalSeconds.ToString("0") ?? "null";
        var commandOneLine = string.IsNullOrEmpty(command) ? "" : command.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
        ExceptionLog.WriteInfo($"[RunSshCommandDirect] 开始 host={host} port={port} connectionTimeout={timeoutSec}s command=[{commandOneLine}]");
        var totalSw = Stopwatch.StartNew();

        var conn = CreateConnectionInfo(host, port, username, password, keyPath, keyPassphrase, useAgent, connectionTimeout);
        if (conn == null)
        {
            ExceptionLog.WriteInfo($"[RunSshCommandDirect] CreateConnectionInfo 返回 null host={host}");
            return null;
        }
        using var client = new SshClient(conn);
        AcceptAnyHostKey(client);
        var connectSw = Stopwatch.StartNew();
        try
        {
            client.Connect();
            connectSw.Stop();
            ExceptionLog.WriteInfo($"[RunSshCommandDirect] Connect 完成 host={host} 耗时={connectSw.ElapsedMilliseconds}ms");
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
            return null;
        }
        try
        {
            var cmdSw = Stopwatch.StartNew();
            ExceptionLog.WriteInfo($"[RunSshCommandDirect] RunCommand 开始 host={host} command=[{commandOneLine}]");
            using var cmd = client.RunCommand(command);
            var result = cmd.Result;
            cmdSw.Stop();
            totalSw.Stop();
            ExceptionLog.WriteInfo($"[RunSshCommandDirect] RunCommand 完成 host={host} 命令耗时={cmdSw.ElapsedMilliseconds}ms 总耗时={totalSw.ElapsedMilliseconds}ms 输出长度={result?.Length ?? 0}");
            return result;
        }
        catch (Exception ex)
        {
            totalSw.Stop();
            ExceptionLog.WriteInfo($"[RunSshCommandDirect] RunCommand 异常 host={host} 总耗时={totalSw.ElapsedMilliseconds}ms");
            ExceptionLog.Write(ex, $"[RunSshCommandDirect] RunCommand host={host}", toCrashLog: false);
            return null;
        }
        finally
        {
            try { client.Disconnect(); } catch { }
        }
    }
    private readonly ConcurrentDictionary<string, ISessionHandle> _sessions = new();

    public event EventHandler<(string SessionId, string Data)>? DataReceived;
    public event EventHandler<string>? SessionClosed;
    /// <summary>SSH 会话连接成功后触发（仅 SSH，不含本地会话）</summary>
    public event EventHandler<string>? SessionConnected;

    public void CreateLocalSession(string sessionId, string nodeId, string protocol, Action<string> onError)
    {
        string exe;
        string args;
        if (protocol.Equals("cmd", StringComparison.OrdinalIgnoreCase))
        {
            exe = "cmd.exe";
            args = "/Q";
        }
        else
        {
            exe = "powershell.exe";
            args = "-NoLogo -NoExit";
        }

        var si = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        try
        {
            var process = Process.Start(si);
            if (process == null) { onError("无法启动进程"); return; }

            var handle = new LocalSessionHandle(sessionId, nodeId, process, data =>
            {
                DataReceived?.Invoke(this, (sessionId, data));
            });
            handle.Closed += (_, _) => SessionClosed?.Invoke(this, sessionId);
            _sessions[sessionId] = handle;
            handle.StartReading();
        }
        catch (Exception ex)
        {
            onError(ex.Message);
        }
    }

    /// <summary>创建 SSH 会话。直连或经 jumpChain 多跳（本地端口转发链）连接目标 host:port。</summary>
    public void CreateSshSession(string sessionId, string nodeId,
        string host, ushort port, string username,
        string? password, string? keyPath, string? keyPassphrase,
        List<JumpHop>? jumpChain, bool useAgent,
        Action<string> onError)
    {
        try
        {
            if (jumpChain == null || jumpChain.Count == 0)
            {
                CreateSshSessionDirect(sessionId, nodeId, host, port, username, password, keyPath, keyPassphrase, useAgent, null, onError);
                return;
            }

            // 多跳：沿跳板机链建立本地端口转发，最后连到 127.0.0.1:lastBoundPort 即目标
            string connectHost = jumpChain[0].Host;
            var connectPort = (uint)jumpChain[0].Port;
            var chainDisposables = new List<IDisposable>();

            for (var i = 0; i < jumpChain.Count; i++)
            {
                var hop = jumpChain[i];
                var conn = CreateConnectionInfo(connectHost, (ushort)connectPort, hop.Username, hop.Password, hop.KeyPath, hop.KeyPassphrase, hop.UseAgent);
                if (conn == null) { onError(hop.UseAgent ? $"跳板机 {i + 1}：请启动 SSH Agent 并添加私钥" : $"跳板机 {i + 1} 请配置密码或私钥"); return; }

                var client = new SshClient(conn);
                AcceptAnyHostKey(client);
                client.Connect();
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

            CreateSshSessionDirect(sessionId, nodeId, connectHost, (ushort)connectPort, username, password, keyPath, keyPassphrase, useAgent, chainDisposables, onError);
        }
        catch (Exception ex)
        {
            onError(ex.Message);
        }
    }

    /// <summary>仅单元测试时为 true（由测试 SetUpFixture 设置 XOPENTERM_UNIT_TEST=1）。为 true 时自动接受/忽略 host key 检查。</summary>
    internal static bool IsUnitTestMode => Environment.GetEnvironmentVariable("XOPENTERM_UNIT_TEST") == "1";

    /// <summary>在连接前自动接受服务器 host key，避免首次连接或 known_hosts 未包含时连接失败。仅单元测试环境下生效；正式运行不自动忽略 host key。供 CreateConnectionInfo 的调用方在 new SshClient/SftpClient 后、Connect 前调用。</summary>
    internal static void AcceptAnyHostKey(BaseClient client)
    {
        if (client == null || !IsUnitTestMode) return;
        client.HostKeyReceived += (_, e) => e.CanTrust = true;
    }

    /// <summary>在启动 PuTTY 前调用：仅单元测试时用 SSH.NET 预取 host key 并写入 PuTTY 注册表。正式运行不写入。</summary>
    internal static void TryCacheHostKeyForPutty(string host, int port, string? username, string? password, string? keyPath, string? keyPassphrase, bool useAgent)
    {
        if (!IsUnitTestMode) return;
        try
        {
            var conn = CreateConnectionInfo(host, port == 0 ? (ushort)22 : (ushort)port, username ?? "", password, keyPath, keyPassphrase, useAgent);
            if (conn == null) return;
            byte[]? keyBlob = null;
            using var client = new SshClient(conn);
            client.HostKeyReceived += (_, e) =>
            {
                e.CanTrust = true;
                keyBlob = e.HostKey;
            };
            conn.Timeout = TimeSpan.FromSeconds(8);
            client.Connect();
            try
            {
                if (keyBlob == null) return;
                if (!TryFormatHostKeyForPuttyRegistry(keyBlob, out var puttyKeyName, out var puttyValue)) return;
                WritePuttyHostKeyToRegistry(puttyKeyName, puttyValue, host, port);
            }
            finally
            {
                try { client.Disconnect(); } catch { }
            }
        }
        catch (Exception ex)
        {
            ExceptionLog.WriteInfo($"[TryCacheHostKeyForPutty] 预取 host key 失败 host={host} port={port}: {ex.Message}");
        }
    }

    private static bool TryFormatHostKeyForPuttyRegistry(byte[] blob, out string puttyKeyName, out string puttyValue)
    {
        puttyKeyName = "";
        puttyValue = "";
        // SSH wire: uint32 len, string type [, for rsa: uint32 len_e, e, uint32 len_n, n ]
        if (blob.Length < 8) return false;
        var offset = 0;
        int ReadUInt32Be()
        {
            if (offset + 4 > blob.Length) return 0;
            var v = (blob[offset] << 24) | (blob[offset + 1] << 16) | (blob[offset + 2] << 8) | blob[offset + 3];
            offset += 4;
            return v;
        }
        var typeLen = ReadUInt32Be();
        if (offset + typeLen > blob.Length) return false;
        var type = Encoding.ASCII.GetString(blob, offset, typeLen);
        offset += typeLen;
        if (type == "ssh-rsa" && offset + 8 <= blob.Length)
        {
            var eLen = ReadUInt32Be();
            if (offset + eLen > blob.Length) return false;
            var eBytes = blob.AsSpan(offset, eLen).ToArray();
            offset += eLen;
            var nLen = ReadUInt32Be();
            if (offset + nLen > blob.Length) return false;
            var nBytes = blob.AsSpan(offset, nLen).ToArray();
            puttyKeyName = "rsa2";
            puttyValue = "0x" + BytesToHex(eBytes) + ",0x" + BytesToHex(nBytes);
            return true;
        }
        if (type == "ssh-dss" && offset + 4 <= blob.Length)
        {
            var pLen = ReadUInt32Be();
            if (offset + pLen > blob.Length) return false;
            var p = blob.AsSpan(offset, pLen).ToArray();
            offset += pLen;
            var qLen = ReadUInt32Be();
            if (offset + qLen > blob.Length) return false;
            var q = blob.AsSpan(offset, qLen).ToArray();
            offset += qLen;
            var gLen = ReadUInt32Be();
            if (offset + gLen > blob.Length) return false;
            var g = blob.AsSpan(offset, gLen).ToArray();
            offset += gLen;
            var yLen = ReadUInt32Be();
            if (offset + yLen > blob.Length) return false;
            var y = blob.AsSpan(offset, yLen).ToArray();
            puttyKeyName = "dss";
            puttyValue = "0x" + BytesToHex(p) + ",0x" + BytesToHex(q) + ",0x" + BytesToHex(g) + ",0x" + BytesToHex(y);
            return true;
        }
        return false;
    }

    private static string BytesToHex(byte[] bytes)
    {
        if (bytes.Length == 0) return "";
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static void WritePuttyHostKeyToRegistry(string puttyKeyType, string puttyValue, string host, int port)
    {
        var keyName = $"{puttyKeyType}@{port}:{host}";
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\SimonTatham\PuTTY\SshHostKeys", writable: true);
            key?.SetValue(keyName, puttyValue);
        }
        catch (Exception ex)
        {
            ExceptionLog.WriteInfo($"[WritePuttyHostKeyToRegistry] 写入注册表失败 {keyName}: {ex.Message}");
        }
    }

    /// <summary>供 RemoteFileService 等复用：创建 SSH/SFTP 连接信息。</summary>
    /// <param name="connectionTimeout">连接超时，null 表示使用库默认。</param>
    internal static ConnectionInfo? CreateConnectionInfo(string host, ushort port, string username, string? password, string? keyPath, string? keyPassphrase, bool useAgent = false, TimeSpan? connectionTimeout = null)
    {
        ConnectionInfo? conn;
        if (useAgent)
            conn = CreateConnectionInfoWithAgent(host, port, username, connectionTimeout);
        else if (!string.IsNullOrEmpty(keyPath))
        {
            var keyFile = new PrivateKeyFile(keyPath, keyPassphrase);
            conn = new ConnectionInfo(host, (int)port, username, new PrivateKeyAuthenticationMethod(username, keyFile));
        }
        else if (!string.IsNullOrEmpty(password))
            conn = new ConnectionInfo(host, (int)port, username, new PasswordAuthenticationMethod(username, password));
        else
            conn = null;
        if (conn != null && connectionTimeout.HasValue)
            conn.Timeout = connectionTimeout.Value;
        return conn;
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

    private void CreateSshSessionDirect(string sessionId, string nodeId,
        string host, ushort port, string username,
        string? password, string? keyPath, string? keyPassphrase,
        bool useAgent,
        List<IDisposable>? chainDisposables,
        Action<string> onError)
    {
        try
        {
            var conn = CreateConnectionInfo(host, port, username, password, keyPath, keyPassphrase, useAgent);
            if (conn == null)
            {
                onError(useAgent ? "请启动 SSH Agent（OpenSSH 或 PuTTY Pageant）并添加私钥" : "请配置密码或私钥");
                return;
            }

            var client = new SshClient(conn);
            AcceptAnyHostKey(client);
            client.Connect();
            var stream = client.CreateShellStream("xterm", 80, 24, 800, 600, 1024);

            var handle = new SshSessionHandle(sessionId, nodeId, client, stream, chainDisposables, data =>
            {
                DataReceived?.Invoke(this, (sessionId, data));
            });
            handle.Closed += (_, _) =>
            {
                _sessions.TryRemove(sessionId, out _);
                SessionClosed?.Invoke(this, sessionId);
            };
            _sessions[sessionId] = handle;
            handle.StartReading();
            SessionConnected?.Invoke(this, sessionId);
        }
        catch (Exception ex)
        {
            onError(ex.Message);
        }
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

internal class LocalSessionHandle : ISessionHandle
{
    private readonly string _sessionId;
    private readonly Process _process;
    private readonly Action<string> _onData;
    private readonly object _lock = new();
    private bool _closed;

    public event EventHandler? Closed;

    public LocalSessionHandle(string sessionId, string nodeId, Process process, Action<string> onData)
    {
        _sessionId = sessionId;
        _process = process;
        _onData = onData;
    }

    public void StartReading()
    {
        Task.Run(() =>
        {
            var buffer = new char[4096];
            try
            {
                var reader = _process.StandardOutput;
                int n;
                while (!_process.HasExited && (n = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    var s = new string(buffer, 0, n);
                    _onData(s);
                }
            }
            catch { }
            if (!_closed) { _closed = true; Closed?.Invoke(this, EventArgs.Empty); }
        });
        Task.Run(() =>
        {
            try
            {
                var err = _process.StandardError.ReadToEnd();
                if (!string.IsNullOrEmpty(err)) _onData(err);
            }
            catch { }
        });
    }

    public void Write(string data)
    {
        lock (_lock)
        {
            if (_closed || _process.HasExited) return;
            try { _process.StandardInput.Write(data); _process.StandardInput.Flush(); } catch { }
        }
    }

    public void Resize(ushort rows, ushort cols) { }

    public void Close()
    {
        lock (_lock)
        {
            if (_closed) return;
            _closed = true;
            ExceptionLog.WriteInfo($"LocalSession Close: {_sessionId}");
            try { if (!_process.HasExited) _process.Kill(true); } catch { }
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}

internal class SshSessionHandle : ISessionHandle
{
    private readonly string _sessionId;
    private readonly SshClient _client;
    private readonly ShellStream _stream;
    private readonly List<IDisposable>? _chainDisposables; // 多跳时的跳板客户端与转发端口，逆序释放
    private readonly Action<string> _onData;
    private readonly object _lock = new();
    private bool _closed;

    public event EventHandler? Closed;

    public SshSessionHandle(string sessionId, string nodeId, SshClient client, ShellStream stream, List<IDisposable>? chainDisposables, Action<string> onData)
    {
        _sessionId = sessionId;
        _client = client;
        _stream = stream;
        _chainDisposables = chainDisposables;
        _onData = onData;
    }

    public void StartReading()
    {
        Task.Run(() =>
        {
            var buffer = new byte[4096];
            try
            {
                while (!_closed && _client.IsConnected)
                {
                    if (_stream.DataAvailable)
                    {
                        int n = _stream.Read(buffer, 0, buffer.Length);
                        if (n <= 0) break;
                        var s = Encoding.UTF8.GetString(buffer, 0, n);
                        _onData(s);
                    }
                    else
                        Thread.Sleep(50);
                }
            }
            catch { }
            if (!_closed) { _closed = true; Closed?.Invoke(this, EventArgs.Empty); }
        });
    }

    public void Write(string data)
    {
        lock (_lock)
        {
            if (_closed || !_client.IsConnected) return;
            try { _stream.Write(data); _stream.Flush(); } catch { }
        }
    }

    public void Resize(ushort rows, ushort cols)
    {
        // SSH.NET ShellStream 无窗口大小变更 API，忽略
    }

    public void Close()
    {
        lock (_lock)
        {
            if (_closed) return;
            _closed = true;
            ExceptionLog.WriteInfo($"SshSession Close 开始: {_sessionId}");
            try
            {
                _stream?.Dispose();
                ExceptionLog.WriteInfo($"SshSession Close 断开连接: {_sessionId}");
                _client?.Disconnect();
                _client?.Dispose();
                // 多跳链逆序释放：先停转发端口，再断跳板连接
                if (_chainDisposables != null)
                {
                    for (var i = _chainDisposables.Count - 1; i >= 0; i--)
                    {
                        try { _chainDisposables[i]?.Dispose(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                ExceptionLog.Write(ex, "SshSession Close 异常: " + _sessionId);
            }
            ExceptionLog.WriteInfo($"SshSession Close 结束: {_sessionId}");
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}
