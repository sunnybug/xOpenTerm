using System.Runtime.InteropServices;
using System.Windows;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>
/// 自定义入口，用于捕获消息循环中由原生组件（如嵌入的 PuTTY）抛出的 SEHException，
/// 避免进程直接崩溃且无友好提示。
/// </summary>
internal static class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new App();
        app.InitializeComponent();
        try
        {
            app.Run();
        }
        catch (SEHException ex)
        {
            ExceptionLog.Write(ex, "SEHException in message loop (likely from native component e.g. PuTTY)");
            try
            {
                MessageBox.Show(
                    "程序因外部组件异常退出，详情已写入日志。\n\n" + ex.Message + "\n\n日志目录：\n" + ExceptionLog.LogDirectory,
                    "xOpenTerm",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { }
            Environment.Exit(1);
        }
    }
}
