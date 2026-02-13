using System.Runtime.Versioning;
using System.Windows.Forms;
using AxMSTSCLib;
using MSTSCLib;
using xOpenTerm.Models;
using xOpenTerm.Native;

namespace xOpenTerm.Controls;

/// <summary>使用系统 MSTSCAX 控件（mstscax.dll）的 RDP 窗体，参考 mRemoteNG，不依赖 MsRdpEx。</summary>
[SupportedOSPlatform("windows")]
internal sealed class RdpEmbeddedFormMstsc : Form
{
    private readonly AxMsRdpClient8NotSafeForScripting _rdpControl;
    private readonly string _host;
    private readonly int _port;
    private readonly string _username;
    private readonly string _domain;
    private readonly string? _password;
    private readonly RdpConnectionOptions? _options;

    public event EventHandler? Disconnected;
    public event EventHandler? Connected;
    public event EventHandler<string>? ErrorOccurred;

    public RdpEmbeddedFormMstsc(string host, int port, string username, string domain, string? password, RdpConnectionOptions? options)
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

        _rdpControl = new AxMsRdpClient8NotSafeForScripting { Dock = DockStyle.Fill };
        _rdpControl.OnDisconnected += Rdp_OnDisconnected;
        _rdpControl.OnConnected += Rdp_OnConnected;
        _rdpControl.OnLoginComplete += Rdp_OnLoginComplete;
        _rdpControl.OnFatalError += Rdp_OnFatalError;
        Controls.Add(_rdpControl);
    }

    public void DoConnect()
    {
        try
        {
            _rdpControl.CreateControl();
            while (!_rdpControl.Created)
            {
                System.Windows.Forms.Application.DoEvents();
                System.Threading.Thread.Sleep(50);
            }

            var rdp = (MsRdpClient8NotSafeForScripting)_rdpControl.GetOcx();
            rdp.Server = _host;
            rdp.UserName = _username;
            rdp.Domain = _domain;
            if (!string.IsNullOrEmpty(_password))
                rdp.AdvancedSettings2.ClearTextPassword = _password;

            if (_port != 3389)
                rdp.AdvancedSettings2.RDPPort = _port;

            rdp.AdvancedSettings7.EnableCredSspSupport = true;
            if (_options?.UseConsoleSession == true)
                rdp.AdvancedSettings7.ConnectToAdministerServer = true;

            rdp.AdvancedSettings2.SmartSizing = _options?.SmartSizing ?? false;
            if (_options?.RedirectClipboard == true)
                rdp.AdvancedSettings6.RedirectClipboard = true;

            rdp.DesktopWidth = Math.Max(400, Width);
            rdp.DesktopHeight = Math.Max(300, Height);

            rdp.Connect();
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            if (msg.Contains("separated from its underlying RCW", StringComparison.OrdinalIgnoreCase) || msg.Contains("RCW", StringComparison.OrdinalIgnoreCase))
                msg = "RDP 控件与 COM 对象已分离，连接失败。请关闭后重试或使用「使用 mstsc 打开」。";
            else
                msg = "连接失败: " + msg;
            ErrorOccurred?.Invoke(this, msg);
        }
    }

    public void DoDisconnect()
    {
        try
        {
            if (_rdpControl.IsDisposed) return;
            var rdp = _rdpControl.GetOcx() as MsRdpClient8NotSafeForScripting;
            rdp?.Disconnect();
        }
        catch { /* ignore */ }
    }

    public void ResizeToClientArea(int width, int height)
    {
        if (IsDisposed || !IsHandleCreated) return;
        NativeMethods.SetWindowPos(Handle, IntPtr.Zero, 0, 0, width, height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    private void Rdp_OnDisconnected(object? sender, IMsTscAxEvents_OnDisconnectedEvent e)
    {
        try
        {
            var reason = ((dynamic)e).discReason;
            var ext = ((dynamic)e).extendedDisconnectReason;
            if (reason != 0 && !_rdpControl.IsDisposed)
            {
                var msg = _rdpControl.GetErrorDescription((uint)reason, (uint)ext);
                if (!string.IsNullOrEmpty(msg))
                    ErrorOccurred?.Invoke(this, msg);
            }
        }
        catch { /* 忽略取原因失败 */ }
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private void Rdp_OnConnected(object? sender, EventArgs e) => Connected?.Invoke(this, EventArgs.Empty);

    private void Rdp_OnLoginComplete(object? sender, EventArgs e) { /* 登录完成 */ }

    private void Rdp_OnFatalError(object? sender, IMsTscAxEvents_OnFatalErrorEvent e)
    {
        var code = ((dynamic)e).errorCode;
        ErrorOccurred?.Invoke(this, "RDP 致命错误: " + code);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rdpControl.OnDisconnected -= Rdp_OnDisconnected;
            _rdpControl.OnConnected -= Rdp_OnConnected;
            _rdpControl.OnLoginComplete -= Rdp_OnLoginComplete;
            _rdpControl.OnFatalError -= Rdp_OnFatalError;
            DoDisconnect();
            _rdpControl.Dispose();
        }
        base.Dispose(disposing);
    }
}
