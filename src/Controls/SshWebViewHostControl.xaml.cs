using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
    /// <summary>进程内共享的 WebView2 环境（仅创建一次），避免多控件/并发初始化时出现 "already initialized with a different CoreWebView2Environment"。</summary>
    private static readonly Lazy<Task<CoreWebView2Environment>> SharedEnvironment = new Lazy<Task<CoreWebView2Environment>>(CreateSharedEnvironmentAsync);

    private SshTerminalBridge? _bridge;
    private bool _webViewReady;
    private bool _webViewInitFailed;
    private string? _webViewInitErrorMessage;
    private bool _messageHandlerSubscribed;
    private WebView2? _webView;
    private readonly SemaphoreSlim _ensureWebViewLock = new(1, 1);
    /// <summary>单例异步初始化任务，避免多个调用方同时执行 EnsureCoreWebView2Async 导致死锁。</summary>
    private Task? _ensureWebViewInitTask;

    public SshWebViewHostControl()
    {
        InitializeComponent();
        _webView = new WebView2();
        // 不在此处加入视觉树，否则 WPF 会立即用默认环境隐式初始化，导致后续无法改用自定义数据目录
        Loaded += OnLoaded;
    }

    private WebView2? WebView => _webView;

    public event EventHandler? Closed;

    /// <summary>终端已连接并显示时触发（可用于远程文件、状态栏轮询等）。</summary>
    public event EventHandler? Connected;

    /// <summary>终端首次收到服务端输出并送往 WebView 时触发（用于 test-connect 校验非黑屏）。</summary>
    public event EventHandler? FirstOutputReceived;

    public bool IsRunning => _bridge?.IsConnected ?? false;

    /// <summary>上次连接失败时的错误信息（用于 test-connect 等场景输出具体原因）。</summary>
    public string? LastConnectionError { get; private set; }

    private bool _hasReceivedOutput;

    /// <summary>连接 SSH 并创建交互式 Shell。支持跳板链与现有认证方式。</summary>
    public async void Connect(string host, int port, string username, string? password, string? keyPath, string? keyPassphrase, bool useAgent, List<JumpHop>? jumpChain)
    {
        LastConnectionError = null;
        await EnsureWebViewReadyAsync();
        if (_webViewInitFailed || !_webViewReady)
        {
            LastConnectionError = _webViewInitErrorMessage ?? "WebView2 初始化失败";
            var detail = string.IsNullOrEmpty(_webViewInitErrorMessage) ? "请确认已安装 WebView2 运行时。" : _webViewInitErrorMessage;
            if (!xOpenTerm.Program.IsTestConnectMode)
                MessageBox.Show("WebView2 初始化失败，无法打开终端。\n\n" + detail, "终端不可用", MessageBoxButton.OK, MessageBoxImage.Warning);
            Closed?.Invoke(this, EventArgs.Empty);
            return;
        }
        _bridge?.Dispose();
        _bridge = new SshTerminalBridge();
        _bridge.OnOutput = s =>
        {
            void Send()
            {
                if (WebView?.CoreWebView2 == null) return;
                try
                {
                    if (!_hasReceivedOutput)
                    {
                        _hasReceivedOutput = true;
                        FirstOutputReceived?.Invoke(this, EventArgs.Empty);
                    }
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
            var message = ex.Message;
            if (ex.InnerException != null)
                message += " " + ex.InnerException.Message;
            LastConnectionError = message;
            if (WebView?.CoreWebView2 != null)
            {
                try
                {
                    var errText = "连接失败：" + message + "\r\n";
                    var payload = JsonSerializer.Serialize(new { type = "output", data = errText });
                    WebView.CoreWebView2.PostWebMessageAsString(payload);
                }
                catch { }
            }
            _bridge?.OnClosed(message);
            _bridge?.Dispose();
            _bridge = null;
            Closed?.Invoke(this, EventArgs.Empty);
            if (!xOpenTerm.Program.IsTestConnectMode)
                MessageBox.Show(message, "SSH 连接失败", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        try { _ensureWebViewLock?.Dispose(); } catch { }
    }

    public void FocusTerminal()
    {
        WebView?.Focus();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await EnsureWebViewReadyAsync();
    }

    /// <summary>WebView2 用户数据目录，使用可写的 %LocalAppData%\xOpenTerm\WebView2，避免 dotnet run 时落在 Program Files 下导致无法写入。</summary>
    private static string GetWebView2UserDataFolder()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = string.IsNullOrEmpty(baseDir)
            ? Path.Combine(Path.GetTempPath(), "xOpenTerm", "WebView2")
            : Path.Combine(baseDir, "xOpenTerm", "WebView2");
        try { Directory.CreateDirectory(dir); } catch { }
        return dir;
    }

    private static async Task<CoreWebView2Environment> CreateSharedEnvironmentAsync()
    {
        var userDataFolder = GetWebView2UserDataFolder();
        return await CoreWebView2Environment.CreateAsync(null, userDataFolder, null);
    }

    /// <summary>仅执行一次的 WebView2 核心初始化，供多路调用方共享，避免并发 EnsureCoreWebView2Async 死锁。</summary>
    private async Task EnsureCoreWebView2OnceAsync()
    {
        var env = await SharedEnvironment.Value;
        if (WebView == null) return;
        // 部分环境下 CoreWebView2 需在控件已加入视觉树后才完成初始化，先加入再 Ensure
        await Dispatcher.InvokeAsync(() =>
        {
            if (WebView != null && !RootGrid.Children.Contains(WebView))
                RootGrid.Children.Add(WebView);
        });
        await WebView.EnsureCoreWebView2Async(env);
    }

    private async Task EnsureWebViewReadyAsync()
    {
        if (_webViewReady || _webViewInitFailed || WebView == null) return;
        await _ensureWebViewLock.WaitAsync();
        bool weInit = false;
        try
        {
            if (_webViewReady || _webViewInitFailed || WebView == null) return;
            weInit = true;
        }
        finally
        {
            _ensureWebViewLock.Release();
        }
        if (!weInit) return;
        Task initTask;
        await _ensureWebViewLock.WaitAsync();
        try
        {
            if (_ensureWebViewInitTask == null)
                _ensureWebViewInitTask = EnsureCoreWebView2OnceAsync();
            initTask = _ensureWebViewInitTask;
        }
        finally
        {
            _ensureWebViewLock.Release();
        }
        try
        {
            await initTask;
        }
        catch (Exception ex)
        {
            ExceptionLog.Write(ex, "WebView2 初始化失败", toCrashLog: false);
            _webViewInitFailed = true;
            _webViewInitErrorMessage = ex.Message;
            if (ex.InnerException != null)
                _webViewInitErrorMessage += "\n" + ex.InnerException.Message;
            return;
        }
        await _ensureWebViewLock.WaitAsync();
        try
        {
            if (_webViewReady || _webViewInitFailed || WebView == null) return;
            if (!RootGrid.Children.Contains(WebView))
                RootGrid.Children.Add(WebView);
            if (!_messageHandlerSubscribed)
            {
                WebView!.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                _messageHandlerSubscribed = true;
            }
            // 使用 Navigate 而非设置 Source，避免隐式用默认环境再次初始化（见 WebView2 文档与 GitHub #1782）
            var htmlPath = Path.Combine(AppContext.BaseDirectory, "TerminalPage.html");
            var useFile = File.Exists(htmlPath);
            if (useFile)
                WebView!.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
            else
                WebView!.NavigateToString(GetEmbeddedTerminalHtml());
            _webViewReady = true;
        }
        finally
        {
            _ensureWebViewLock.Release();
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
