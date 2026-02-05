using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Renci.SshNet;
using SshNet.Agent;
using xOpenTerm.Models;

namespace xOpenTerm.Services;

/// <summary>管理 SSH 与本地终端会话，向 UI 推送输出</summary>
public class SessionManager
{
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

    /// <summary>供 RemoteFileService 等复用：创建 SSH/SFTP 连接信息。</summary>
    internal static ConnectionInfo? CreateConnectionInfo(string host, ushort port, string username, string? password, string? keyPath, string? keyPassphrase, bool useAgent = false)
    {
        if (useAgent)
            return CreateConnectionInfoWithAgent(host, port, username);
        if (!string.IsNullOrEmpty(keyPath))
        {
            var keyFile = new PrivateKeyFile(keyPath, keyPassphrase);
            return new ConnectionInfo(host, (int)port, username, new PrivateKeyAuthenticationMethod(username, keyFile));
        }
        if (!string.IsNullOrEmpty(password))
            return new ConnectionInfo(host, (int)port, username, new PasswordAuthenticationMethod(username, password));
        return null;
    }

    /// <summary>使用 SSH Agent（OpenSSH 或 PuTTY Pageant）创建连接信息。</summary>
    private static ConnectionInfo? CreateConnectionInfoWithAgent(string host, int port, string username)
    {
        SshAgentPrivateKey[]? keys = null;
        try
        {
            // 先尝试 OpenSSH Agent（Windows 上为 openssh-ssh-agent 或 SSH_AUTH_SOCK）
            var sshAgent = new SshAgent();
            keys = sshAgent.RequestIdentities();
            if (keys is { Length: > 0 })
                return new ConnectionInfo(host, port, username, new PrivateKeyAuthenticationMethod(username, keys));
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
                return new ConnectionInfo(host, port, username, new PrivateKeyAuthenticationMethod(username, keys));
        }
        catch
        {
            // Pageant 未运行
        }

        return null;
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
            try
            {
                _stream?.Dispose();
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
            catch { }
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}
