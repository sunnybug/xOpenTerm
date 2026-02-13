using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Windows.Forms;
using xOpenTerm.Models;
using xOpenTerm.Native;

namespace xOpenTerm.Controls;

/// <summary>在独立线程上以 WinForms 消息循环承载 RDP，通过 SetParent 嵌入到 WPF 的 Panel 中，避免 RDP ActiveX 在 WPF 消息循环中触发 SEHException（参考 mRemoteNG）。</summary>
[SupportedOSPlatform("windows")]
public sealed class RdpEmbeddedSession
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _username;
    private readonly string _domain;
    private readonly string? _password;
    private readonly RdpConnectionOptions? _options;
    private readonly Panel _panel;
    private readonly SynchronizationContext _uiContext;
    private readonly BlockingCollection<IntPtr> _parentHandleQueue = new();
    private volatile RdpEmbeddedForm? _form;
    private Thread? _thread;
    private bool _closed;

    public event EventHandler? Disconnected;
    public event EventHandler? Connected;
    public event EventHandler<string>? ErrorOccurred;

    public RdpEmbeddedSession(string host, int port, string username, string domain, string? password,
        RdpConnectionOptions? options, Panel panel, SynchronizationContext uiContext)
    {
        _host = host ?? "";
        _port = port;
        _username = username ?? "";
        _domain = domain ?? "";
        _password = password;
        _options = options;
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        _uiContext = uiContext ?? throw new ArgumentNullException(nameof(uiContext));
    }

    public void Start()
    {
        void SendParentHandle()
        {
            if (_closed || !_panel.IsHandleCreated) return;
            try { _parentHandleQueue.Add(_panel.Handle); } catch (ObjectDisposedException) { }
        }

        if (_panel.IsHandleCreated)
            SendParentHandle();
        else
            _panel.HandleCreated += (_, _) => SendParentHandle();

        _panel.Resize += Panel_Resize;

        _thread = new Thread(RdpThreadProc)
        {
            IsBackground = true,
            Name = "RdpEmbedded"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void Panel_Resize(object? sender, EventArgs e)
    {
        var form = _form;
        if (form == null || form.IsDisposed) return;
        try
        {
            form.Invoke(() =>
            {
                if (!form.IsDisposed && _panel.IsHandleCreated)
                    form.ResizeToClientArea(_panel.ClientSize.Width, _panel.ClientSize.Height);
            });
        }
        catch (ObjectDisposedException) { }
    }

    private void RdpThreadProc()
    {
        try
        {
            IntPtr parentHandle;
            try
            {
                parentHandle = _parentHandleQueue.Take();
            }
            catch (InvalidOperationException)
            {
                return;
            }

            if (_closed) return;

            using var form = new RdpEmbeddedForm(_host, _port, _username, _domain, _password, _options);
            form.Connected += (s, e) => _uiContext.Post(_ => Connected?.Invoke(this, EventArgs.Empty), null);
            form.Disconnected += (s, e) => _uiContext.Post(_ => Disconnected?.Invoke(this, EventArgs.Empty), null);
            form.ErrorOccurred += (s, msg) => _uiContext.Post(_ => ErrorOccurred?.Invoke(this, msg!), null);

            form.Show();
            _form = form;

            if (_closed) { form.Close(); return; }

            NativeMethods.SetParent(form.Handle, parentHandle);
            if (NativeMethods.GetClientRect(parentHandle, out var r))
                form.ResizeToClientArea(r.Width, r.Height);

            form.DoConnect();
            System.Windows.Forms.Application.Run(form);
        }
        catch (Exception ex)
        {
            _uiContext.Post(_ => ErrorOccurred?.Invoke(this, "RDP 线程异常: " + ex.Message), null);
        }
        finally
        {
            _form = null;
        }
    }

    public void Disconnect()
    {
        try { _form?.Invoke(() => _form.DoDisconnect()); } catch { }
    }

    public void Close()
    {
        _closed = true;
        try { _parentHandleQueue?.Dispose(); } catch { }
        try
        {
            _form?.Invoke(() =>
            {
                try { _form?.Close(); } catch { }
            });
        }
        catch { }
        _panel.Resize -= Panel_Resize;
        try
        {
            _thread?.Join(3000);
        }
        catch { }
    }
}
