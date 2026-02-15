using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using xOpenTerm.Models;
using xOpenTerm.Services;

namespace xOpenTerm.Controls;

/// <summary>
/// SSH 终端宿主控件：WebView2 内嵌 xterm.js 页面，通过 SshTerminalBridge 与 SSH.NET ShellStream 桥接。
/// </summary>
public partial class SshWebViewHostControl : UserControl
{
    private SshTerminalBridge? _bridge;
    private bool _webViewReady;
    private bool _messageHandlerSubscribed;
    private WebView2? _webView;

    public SshWebViewHostControl()
    {
        InitializeComponent();
        _webView = new WebView2();
        RootGrid.Children.Add(_webView);
        Loaded += OnLoaded;
    }

    private WebView2? WebView => _webView;

    public event EventHandler? Closed;

    /// <summary>终端已连接并显示时触发（可用于远程文件、状态栏轮询等）。</summary>
    public event EventHandler? Connected;

    public bool IsRunning => _bridge?.IsConnected ?? false;

    /// <summary>连接 SSH 并创建交互式 Shell。支持跳板链与现有认证方式。</summary>
    public async void Connect(string host, int port, string username, string? password, string? keyPath, string? keyPassphrase, bool useAgent, List<JumpHop>? jumpChain)
    {
        await EnsureWebViewReadyAsync();
        _bridge?.Dispose();
        _bridge = new SshTerminalBridge();
        _bridge.OnOutput = s =>
        {
            void Send()
            {
                if (WebView?.CoreWebView2 == null) return;
                try
                {
                    var payload = JsonSerializer.Serialize(new { type = "output", data = s });
                    WebView.CoreWebView2.PostWebMessageAsString(payload);
                }
                catch { }
            }
            if (Dispatcher.CheckAccess())
                Send();
            else
                Dispatcher.BeginInvoke(Send);
        };
        _bridge.OnClosed = msg =>
        {
            void Raise()
            {
                Closed?.Invoke(this, EventArgs.Empty);
            }
            if (Dispatcher.CheckAccess())
                Raise();
            else
                Dispatcher.BeginInvoke(Raise);
        };

        try
        {
            await _bridge.ConnectAsync(host, port, username ?? "", password, keyPath, keyPassphrase, useAgent, jumpChain);
            Connected?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ExceptionLog.Write(ex, "SSH 连接失败", toCrashLog: false);
            _bridge?.Dispose();
            _bridge = null;
            throw;
        }
    }

    public void Close()
    {
        _bridge?.Dispose();
        _bridge = null;
        try
        {
            WebView?.CoreWebView2?.Navigate("about:blank");
            WebView?.Dispose();
            _webView = null;
        }
        catch { }
    }

    public void FocusTerminal()
    {
        WebView?.Focus();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await EnsureWebViewReadyAsync();
    }

    private async System.Threading.Tasks.Task EnsureWebViewReadyAsync()
    {
        if (_webViewReady || WebView == null) return;
        try
        {
            await WebView.EnsureCoreWebView2Async(null);
            if (!_messageHandlerSubscribed)
            {
                WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                _messageHandlerSubscribed = true;
            }
            var htmlPath = Path.Combine(AppContext.BaseDirectory, "TerminalPage.html");
            if (File.Exists(htmlPath))
                WebView.Source = new Uri(htmlPath);
            else
                WebView.NavigateToString(GetEmbeddedTerminalHtml());
            _webViewReady = true;
        }
        catch (Exception ex)
        {
            ExceptionLog.Write(ex, "WebView2 初始化失败", toCrashLog: false);
            throw;
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(json)) return;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("type", out var type) && type.GetString() == "input"
                && root.TryGetProperty("data", out var data))
            {
                _bridge?.SendInput(data.GetString() ?? "");
            }
        }
        catch { }
    }

    private static string GetEmbeddedTerminalHtml()
    {
        return """
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="UTF-8">
<title>SSH</title>
<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/xterm@5.3.0/css/xterm.css">
<style>html,body{height:100%;margin:0;background:#0c0c0c}#t{height:100%;padding:8px}</style>
</head>
<body><div id="t"></div>
<script src="https://cdn.jsdelivr.net/npm/xterm@5.3.0/lib/xterm.js"></script>
<script src="https://cdn.jsdelivr.net/npm/xterm-addon-fit@0.8.0/lib/xterm-addon-fit.js"></script>
<script>
var term=new Terminal({cursorBlink:true,fontSize:14,theme:{background:'#0c0c0c',foreground:'#ccc'}});
var fit=new FitAddon.FitAddon();term.loadAddon(fit);term.open(document.getElementById('t'));fit.fit();
window.onresize=function(){fit.fit();};
if(window.chrome&&window.chrome.webview){
  term.onData(function(d){window.chrome.webview.postMessage(JSON.stringify({type:'input',data:d}));});
  window.chrome.webview.addEventListener('message',function(e){
    try{var m=JSON.parse(e.data);if(m.type==='output'&&m.data)term.write(m.data);}catch(){}
  });
}
</script>
</body>
</html>
""";
    }
}
