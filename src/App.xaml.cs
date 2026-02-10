using System.Windows;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using xOpenTerm.Services;

namespace xOpenTerm;

public partial class App : Application
{
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
        // 确保 Material Design 主题字典在最前（提供 Primary.Light 等），避免 Defaults 解析时报错
        var theme = new Theme();
        theme.SetBaseTheme(BaseTheme.Dark);
        ResourceDictionaryExtensions.SetTheme(Application.Current.Resources, theme);
    }

    private void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ExceptionLog.Write(e.Exception, "DispatcherUnhandledException");
        e.Handled = true; // 先标记已处理，避免 WPF 再抛一层
        MessageBox.Show(
            "程序发生未处理的错误，详情已写入日志。\n\n" + e.Exception.Message + "\n\n日志目录：\n" + ExceptionLog.LogDirectory,
            "xOpenTerm",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        // 用户确认后正常退出进程，保证能走 Shutdown/Exit 等清理流程
        Application.Current.Shutdown(1);
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
