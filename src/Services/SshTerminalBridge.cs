using System.Buffers;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;
using xOpenTerm.Models;

namespace xOpenTerm.Services;

/// <summary>
/// 桥接 SSH Shell 与 WebView2：从 ShellStream 读取输出发往前端，接收前端输入写入 ShellStream。
/// 支持直连与跳板链，复用 SessionManager 的认证与跳板逻辑。
/// </summary>
public sealed class SshTerminalBridge : IDisposable
{
    private const int Columns = 80;
    private const int Rows = 24;
    private const int Width = 800;
    private const int Height = 600;
    private const int BufferSize = 1024;

    private SshClient? _client;
    private ShellStream? _shell;
    private List<IDisposable>? _chainDisposables;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private readonly object _writeLock = new();

    /// <summary>收到 SSH 输出时调用，参数为 UTF-8 字符串。应在 UI 线程执行并调用 PostWebMessage 等。</summary>
    public Action<string>? OnOutput { get; set; }

    /// <summary>连接已关闭或出错时调用。</summary>
    public Action<string?>? OnClosed { get; set; }

    public bool IsConnected => _shell != null && _shell.CanRead;

    /// <summary>
    /// 连接并创建交互式 Shell。支持直连与跳板链；认证方式与 SessionManager 一致（密码、私钥、Agent）。
    /// </summary>
    public async Task ConnectAsync(string host, int port, string username, string? password, string? keyPath, string? keyPassphrase, bool useAgent, List<JumpHop>? jumpChain, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            try
            {
                if (jumpChain == null || jumpChain.Count == 0)
                {
                    ConnectDirect(host, (ushort)port, username, password, keyPath, keyPassphrase, useAgent);
                    return;
                }

                string connectHost = jumpChain[0].Host;
                var connectPort = (ushort)jumpChain[0].Port;
                _chainDisposables = new List<IDisposable>();

                for (var i = 0; i < jumpChain.Count; i++)
                {
                    var hop = jumpChain[i];
                    var conn = SessionManager.CreateConnectionInfo(connectHost, connectPort, hop.Username, hop.Password, hop.KeyPath, hop.KeyPassphrase, hop.UseAgent);
                    if (conn == null)
                        throw new InvalidOperationException("跳板机连接失败：未配置认证方式（请配置密码、私钥或 SSH Agent）");
                    var client = new SshClient(conn);
                    SessionManager.AcceptAnyHostKey(client);
                    client.Connect();
                    _chainDisposables.Add(client);
                    var nextHost = i + 1 < jumpChain.Count ? jumpChain[i + 1].Host : host;
                    var nextPort = (ushort)(i + 1 < jumpChain.Count ? jumpChain[i + 1].Port : port);
                    var fwd = new ForwardedPortLocal("127.0.0.1", 0, nextHost, nextPort);
                    client.AddForwardedPort(fwd);
                    fwd.Start();
                    _chainDisposables.Add(fwd);
                    connectHost = "127.0.0.1";
                    connectPort = (ushort)fwd.BoundPort;
                }

                ConnectDirect(connectHost, connectPort, username, password, keyPath, keyPassphrase, useAgent);
            }
            catch (Exception ex)
            {
                OnClosed?.Invoke(ex.Message);
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);

        _readCts = new CancellationTokenSource();
        _readTask = ReadShellOutputAsync(_readCts.Token);
    }

    private void ConnectDirect(string host, ushort port, string username, string? password, string? keyPath, string? keyPassphrase, bool useAgent)
    {
        var conn = SessionManager.CreateConnectionInfo(host, port, username, password, keyPath, keyPassphrase, useAgent);
        if (conn == null)
            throw new InvalidOperationException("未配置认证方式（请配置密码、私钥或 SSH Agent）");
        _client = new SshClient(conn);
        SessionManager.AcceptAnyHostKey(_client);
        _client.Connect();
        _shell = _client.CreateShellStream("xterm", Columns, Rows, Width, Height, BufferSize);
    }

    /// <summary>将用户输入写入 SSH Shell（由 WebView2 消息调用）。</summary>
    public void SendInput(string data)
    {
        if (string.IsNullOrEmpty(data)) return;
        lock (_writeLock)
        {
            try
            {
                _shell?.Write(Encoding.UTF8.GetBytes(data));
                _shell?.Flush();
            }
            catch (Exception ex)
            {
                OnClosed?.Invoke(ex.Message);
            }
        }
    }

    private async Task ReadShellOutputAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            while (_shell != null && !cancellationToken.IsCancellationRequested)
            {
                int count;
                try
                {
                    count = await _shell.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OnClosed?.Invoke(ex.Message);
                    break;
                }

                if (count <= 0) break;

                var text = Encoding.UTF8.GetString(buffer.AsSpan(0, count));
                OnOutput?.Invoke(text);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void Dispose()
    {
        _readCts?.Cancel();
        try { _readTask?.GetAwaiter().GetResult(); } catch { }
        _shell?.Dispose();
        _shell = null;
        _client?.Disconnect();
        _client?.Dispose();
        _client = null;
        if (_chainDisposables != null)
        {
            for (var i = _chainDisposables.Count - 1; i >= 0; i--)
            {
                try { _chainDisposables[i]?.Dispose(); } catch { }
            }
            _chainDisposables = null;
        }
        _readCts?.Dispose();
    }
}
