using System.Runtime.Versioning;
using System.Windows.Forms;
using RoyalApps.Community.Rdp.WinForms.Configuration;
using RoyalApps.Community.Rdp.WinForms.Controls;
using SensitiveString = RoyalApps.Community.Rdp.WinForms.Configuration.SensitiveString;
using xOpenTerm.Models;
using xOpenTerm.Native;

namespace xOpenTerm.Controls;

/// <summary>在独立线程上运行的 WinForms 窗体，承载 RDP 控件。通过 SetParent 嵌入到 WPF 的 Panel 中，使 RDP 消息在 WinForms 消息循环中处理，避免 WPF 消息循环中的 SEHException（参考 mRemoteNG 纯 WinForms 承载 RDP）。</summary>
[SupportedOSPlatform("windows")]
internal sealed class RdpEmbeddedForm : Form
{
    private readonly RdpControl _rdpControl;
    private readonly string _host;
    private readonly int _port;
    private readonly string _username;
    private readonly string _domain;
    private readonly string? _password;
    private readonly RdpConnectionOptions? _options;

    public event EventHandler? Disconnected;
    public event EventHandler? Connected;
    public event EventHandler<string>? ErrorOccurred;

    public RdpEmbeddedForm(string host, int port, string username, string domain, string? password, RdpConnectionOptions? options)
    {
        _host = host ?? "";
        _port = port;
        _username = username ?? "";
        _domain = domain ?? "";
        _password = password;
        _options = options;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        Size = new System.Drawing.Size(800, 600);
        MinimumSize = new System.Drawing.Size(400, 300);

        _rdpControl = new RdpControl { Dock = DockStyle.Fill };
        _rdpControl.OnDisconnected += Rdp_OnDisconnected;
        _rdpControl.OnConnected += Rdp_OnConnected;
        Controls.Add(_rdpControl);
    }

    private static void ApplyOptions(dynamic config, RdpConnectionOptions? options)
    {
        if (options == null) return;
        try
        {
            if (options.SmartSizing)
            {
                object? display = null;
                try { display = config.Display; } catch { }
                if (display != null)
                {
                    var displayType = display.GetType();
                    var resizeProp = displayType.GetProperty("ResizeBehavior");
                    if (resizeProp != null)
                    {
                        var enumType = resizeProp.PropertyType;
                        var smartSizing = Enum.Parse(enumType, "SmartSizing", ignoreCase: true);
                        resizeProp.SetValue(display, smartSizing);
                    }
                }
            }
            if (options.RedirectClipboard)
            {
                try
                {
                    var prop = config.GetType().GetProperty("RedirectClipboard");
                    prop?.SetValue(config, true);
                }
                catch { }
            }
        }
        catch { }
    }

    public void DoConnect()
    {
        try
        {
            if (!_rdpControl.IsHandleCreated) return;
            var config = _rdpControl.RdpConfiguration;
            config.Server = _host;
            config.Port = _port;
            config.Credentials.Username = _username;
            config.Credentials.Domain = _domain;
            if (!string.IsNullOrEmpty(_password))
                config.Credentials.Password = new SensitiveString(_password);
            ApplyOptions(config, _options);
            _rdpControl.Connect();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, "连接失败: " + ex.Message);
        }
    }

    public void DoDisconnect()
    {
        try { _rdpControl.Disconnect(); } catch { }
    }

    public void ResizeToClientArea(int width, int height)
    {
        if (IsDisposed || !IsHandleCreated) return;
        NativeMethods.SetWindowPos(Handle, IntPtr.Zero, 0, 0, width, height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    private void Rdp_OnDisconnected(object? sender, DisconnectedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Description))
            ErrorOccurred?.Invoke(this, e.Description);
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private void Rdp_OnConnected(object? sender, EventArgs e) => Connected?.Invoke(this, EventArgs.Empty);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rdpControl.OnDisconnected -= Rdp_OnDisconnected;
            _rdpControl.OnConnected -= Rdp_OnConnected;
            DoDisconnect();
            _rdpControl.Dispose();
        }
        base.Dispose(disposing);
    }
}
