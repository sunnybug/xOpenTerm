using System.Runtime.Versioning;
using System.Windows.Forms;
using RoyalApps.Community.Rdp.WinForms.Configuration;
using RoyalApps.Community.Rdp.WinForms.Controls;
using SensitiveString = RoyalApps.Community.Rdp.WinForms.Configuration.SensitiveString;

namespace xOpenTerm.Controls;

/// <summary>在 WinForms 中承载 RDP 控件并连接，供 WPF 通过 WindowsFormsHost 嵌入标签页。基于 RoyalApps.Community.Rdp.WinForms（MsRdpEx）。</summary>
[SupportedOSPlatform("windows")]
public class RdpHostControl : System.Windows.Forms.UserControl
{
    private readonly RdpControl _rdpControl;
    private readonly string _host;
    private readonly int _port;
    private readonly string _username;
    private readonly string _domain;
    private readonly string? _password;

    public event EventHandler? Disconnected;
    public event EventHandler? Connected;
    public event EventHandler<string>? ErrorOccurred;

    public RdpHostControl(string host, int port, string username, string domain, string? password)
    {
        _host = host ?? "";
        _port = port;
        _username = username ?? "";
        _domain = domain ?? "";
        _password = password;
        Dock = DockStyle.Fill;
        MinimumSize = new System.Drawing.Size(400, 300);

        _rdpControl = new RdpControl { Dock = DockStyle.Fill };
        _rdpControl.OnDisconnected += Rdp_OnDisconnected;
        _rdpControl.OnConnected += Rdp_OnConnected;
        Controls.Add(_rdpControl);

        var config = _rdpControl.RdpConfiguration;
        config.Server = _host;
        config.Port = _port;
        config.Credentials.Username = _username;
        config.Credentials.Domain = _domain;
        if (!string.IsNullOrEmpty(_password))
            config.Credentials.Password = new SensitiveString(_password);
    }

    /// <summary>发起连接。若控件尚未创建/加载，会延迟到 Load 后再连接，避免 InvalidActiveXStateException。</summary>
    public void Connect()
    {
        void DoConnect()
        {
            try
            {
                _rdpControl.Connect();
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, "连接失败: " + ex.Message);
            }
        }

        if (IsHandleCreated && _rdpControl.IsHandleCreated)
        {
            BeginInvoke(new Action(() => { if (!IsDisposed) DoConnect(); }));
            return;
        }

        void OnReady(object? s, EventArgs e)
        {
            Load -= OnReady;
            // 延迟到下一消息循环，必要时再延迟一帧，确保 WindowsFormsHost 与 ActiveX 已创建
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;
                    DoConnect();
                }));
            }));
        }
        Load += OnReady;
    }

    public void Disconnect()
    {
        try
        {
            _rdpControl.Disconnect();
        }
        catch { /* ignore */ }
    }

    private void Rdp_OnDisconnected(object? sender, RoyalApps.Community.Rdp.WinForms.Controls.DisconnectedEventArgs e)
    {
        BeginInvoke(() =>
        {
            if (!string.IsNullOrEmpty(e.Description))
                ErrorOccurred?.Invoke(this, e.Description);
            Disconnected?.Invoke(this, EventArgs.Empty);
        });
    }

    private void Rdp_OnConnected(object? sender, EventArgs e)
    {
        BeginInvoke(() =>
        {
            Connected?.Invoke(this, EventArgs.Empty);
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rdpControl.OnDisconnected -= Rdp_OnDisconnected;
            _rdpControl.OnConnected -= Rdp_OnConnected;
            Disconnect();
            _rdpControl.Dispose();
        }
        base.Dispose(disposing);
    }
}
