using System.Windows;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using xOpenTerm.Services;

namespace xOpenTerm;

public partial class App : Application
{
    private static bool _isHandlingDispatcherException = false;
    public App()
    {
        Startup += OnStartup;
        Exit += (_, _) => ExceptionLog.WriteInfo("App Exit 事件（进程即将退出）");
        // UI 线程未处理异常（可阻止进程退出并写日志）
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        // 非 UI 线程未处理异常（进程即将退出，仅写日志）
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        // Task 内未观察的异常（避免后台任务抛错导致进程静默崩溃）
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnStartup(object sender, StartupEventArgs e)
    {
        // 确保 HTTPS 请求使用 TLS 1.2+，避免金山云等 API 出现 SSL 连接失败
        System.Net.ServicePointManager.SecurityProtocol =
            System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls13;

        // 确保 Material Design 主题字典在最前（提供 Primary.Light 等），避免 Defaults 解析时报错
        var theme = new Theme();
        theme.SetBaseTheme(BaseTheme.Dark);
        ResourceDictionaryExtensions.SetTheme(Application.Current.Resources, theme);
    }

    private void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (_isHandlingDispatcherException) return;
        _isHandlingDispatcherException = true;

        ExceptionLog.Write(e.Exception, "DispatcherUnhandledException");
        e.Handled = true;

        if (!Program.IsTestRdpMode)
        {
            try
            {
                MessageBox.Show(
                    "程序发生未处理的错误，详情已写入日志。\n\n" + e.Exception.Message + "\n\n日志目录：\n" + ExceptionLog.LogDirectory,
                    "xOpenTerm",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { }
        }
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        if (ex != null)
            ExceptionLog.Write(ex, "UnhandledException (IsTerminating=" + e.IsTerminating + ")");
        else
            ExceptionLog.Write(new InvalidOperationException("UnhandledException with non-Exception: " + e.ExceptionObject), "UnhandledException");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        foreach (var ex in e.Exception.Flatten().InnerExceptions)
            ExceptionLog.Write(ex, "UnobservedTaskException");
        e.SetObserved();
    }
}
