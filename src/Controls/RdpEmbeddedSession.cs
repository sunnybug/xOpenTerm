using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Windows.Forms;
using xOpenTerm.Models;
using xOpenTerm.Native;

namespace xOpenTerm.Controls;

/// <summary>在独立 STA 线程上以 WinForms 消息循环承载 RDP，通过 SetParent 嵌入到 WPF 的 Panel 中，避免 RDP ActiveX 在 WPF 消息循环中触发 SEHException；每会话一线程以支持多 RDP 标签同时打开。</summary>
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
    private volatile RdpEmbeddedFormMstsc? _form;
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

            using var form = new RdpEmbeddedFormMstsc(_host, _port, _username, _domain, _password, _options);
            form.Connected += (s, e) => _uiContext.Post(_ => Connected?.Invoke(this, EventArgs.Empty), null);
            form.Disconnected += (s, e) => _uiContext.Post(_ => Disconnected?.Invoke(this, EventArgs.Empty), null);
            form.ErrorOccurred += (s, msg) => _uiContext.Post(_ => ErrorOccurred?.Invoke(this, msg!), null);

            form.Show();
            _form = form;

            if (_closed) { form.Close(); return; }

            // 先按 panel 客户区尺寸调整 Form，再 Connect，使 DesktopWidth/DesktopHeight 与显示区域一致，SmartSizing 才能正确缩放（参考 mRemoteNG）
            if (NativeMethods.GetClientRect(parentHandle, out var initialRect) && initialRect.Width > 0 && initialRect.Height > 0)
                form.ResizeToClientArea(initialRect.Width, initialRect.Height);

            // 必须在 SetParent 之前完成配置与 Connect，否则嵌入到 WPF 窗口后 RDP COM 会与 RCW 分离
            form.DoConnect();

            NativeMethods.SetParent(form.Handle, parentHandle);
            if (NativeMethods.GetClientRect(parentHandle, out var r))
                form.ResizeToClientArea(r.Width, r.Height);

            System.Windows.Forms.Application.Run(form);
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            if (msg.Contains("External component has thrown an exception", StringComparison.OrdinalIgnoreCase))
                msg = "RDP 连接失败（可能因未填写密码或控件初始化异常）。请填写密码或使用「使用 mstsc 打开」在外部连接。";
            else if (msg.Contains("IMsRdpExCoreApi", StringComparison.OrdinalIgnoreCase) || msg.Contains("E_NOINTERFACE", StringComparison.OrdinalIgnoreCase) || msg.Contains("80004002"))
                msg = "内嵌 RDP 控件在此环境下不可用（MsRdpEx 接口不支持）。请使用右键菜单「使用 mstsc 打开」连接。";
            else
                msg = "RDP 线程异常: " + msg;
            _uiContext.Post(_ => ErrorOccurred?.Invoke(this, msg), null);
        }
        finally
        {
            _form = null;
        }
    }

    public void Disconnect()
    {
        try { _form?.Invoke(() => _form!.DoDisconnect()); } catch { }
    }

    public void Close()
    {
        _closed = true;
        try { _parentHandleQueue?.Dispose(); } catch { }
        try
        {
            _form?.Invoke(() =>
            {
                try { _form!.Close(); } catch { }
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
