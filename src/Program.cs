using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using xOpenTerm.Services;

namespace xOpenTerm;

internal static class Program
{
    public static bool IsTestRdpMode { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--test-rdp")
        {
            IsTestRdpMode = true;
        }

        var app = new App();
        app.InitializeComponent();
        try
        {
            app.Run();
        }
        catch (SEHException ex)
        {
            ExceptionLog.Write(ex, "SEHException in message loop (likely from native component e.g. PuTTY)");
            if (!IsTestRdpMode)
            {
                try
                {
                    MessageBox.Show(
                        "程序因外部组件异常退出，详情已写入日志。\n\n" + ex.Message + "\n\n日志目录：\n" + ExceptionLog.LogDirectory,
                        "xOpenTerm",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch { }
            }
            try
            {
                Application.Current?.Dispatcher?.InvokeShutdown();
            }
            catch { }
            Environment.Exit(1);
        }
        catch (Exception ex)
        {
            ExceptionLog.Write(ex, "Unhandled exception in Main");
            if (!IsTestRdpMode)
            {
                try
                {
                    MessageBox.Show(
                        "程序发生未处理的错误，详情已写入日志。\n\n" + ex.Message + "\n\n日志目录：\n" + ExceptionLog.LogDirectory,
                        "xOpenTerm",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch { }
            }
            try
            {
                Application.Current?.Dispatcher?.InvokeShutdown();
            }
            catch { }
            Environment.Exit(1);
        }
    }
}
