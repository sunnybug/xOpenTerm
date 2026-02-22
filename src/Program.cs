using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using xOpenTerm.Services;

namespace xOpenTerm;

internal static class Program
{
    public static bool IsTestRdpMode { get; private set; }
    public static bool IsTestScanPortMode { get; private set; }
    public static bool IsTestConnectMode { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--test-rdp")
        {
            IsTestRdpMode = true;
        }
        else if (args.Length > 0 && args[0] == "--test-scan-port")
        {
            IsTestScanPortMode = true;
        }
        else if (args.Length > 0 && args[0] == "--test-connect")
        {
            IsTestConnectMode = true;
        }

        var app = new App();
        app.InitializeComponent();
        try
        {
            app.Run();
        }
        catch (SEHException ex)
        {
            ExceptionLog.Write(ex, "SEHException in message loop (likely from native component e.g. PuTTY/RDP)");
            if (!IsTestRdpMode && !IsTestScanPortMode && !IsTestConnectMode)
            {
                try
                {
                    MessageBox.Show(
                        "程序因内嵌的远程桌面(RDP)或 PuTTY 等原生组件异常而退出，详情已写入日志。\n\n建议：关闭其他标签页后重试，或更新 Windows / PuTTY。\n\n" + ex.Message + "\n\n日志目录：\n" + ExceptionLog.LogDirectory,
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
            if (!IsTestRdpMode && !IsTestScanPortMode && !IsTestConnectMode)
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
