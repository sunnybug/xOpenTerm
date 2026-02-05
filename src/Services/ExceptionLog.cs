using System.Diagnostics;
using System.IO;
using System.Text;

namespace xOpenTerm.Services;

/// <summary>
/// 将未捕获异常写入日志文件，便于排查崩溃。
/// 日志目录：%LocalAppData%\xOpenTerm\logs\ 或当前进程目录下的 logs 子目录。
/// </summary>
public static class ExceptionLog
{
    private static readonly object Lock = new();
    private static string? _logDir;

    public static string LogDirectory
    {
        get
        {
            if (_logDir != null) return _logDir;
            try
            {
                var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(baseDir))
                    baseDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName) ?? ".";
                _logDir = Path.Combine(baseDir, "xOpenTerm", "logs");
                if (!Directory.Exists(_logDir))
                    Directory.CreateDirectory(_logDir);
            }
            catch
            {
                _logDir = Path.Combine(".", "logs");
            }
            return _logDir;
        }
    }

    /// <summary>
    /// 写入一条异常日志（含时间、消息、堆栈），文件名带日期便于按天查看。
    /// </summary>
    public static void Write(Exception ex, string? context = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("----------------------------------------");
        sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        if (!string.IsNullOrEmpty(context))
            sb.AppendLine("Context: " + context);
        sb.AppendLine("Message: " + ex.Message);
        sb.AppendLine("Type: " + ex.GetType().FullName);
        sb.AppendLine("StackTrace:");
        sb.AppendLine(ex.StackTrace);
        for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
        {
            sb.AppendLine("--- Inner ---");
            sb.AppendLine(inner.Message);
            sb.AppendLine(inner.StackTrace);
        }
        sb.AppendLine();

        var content = sb.ToString();
        lock (Lock)
        {
            try
            {
                var file = Path.Combine(LogDirectory, "exceptions_" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                File.AppendAllText(file, content, Encoding.UTF8);
            }
            catch
            {
                try { Trace.WriteLine("[xOpenTerm] ExceptionLog write failed: " + content); } catch { }
            }
        }
    }

    /// <summary>
    /// 写入一条普通信息到同一目录的 info 日志（用于记录启动、连接等）。
    /// </summary>
    public static void WriteInfo(string message)
    {
        lock (Lock)
        {
            try
            {
                var file = Path.Combine(LogDirectory, "info_" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine;
                File.AppendAllText(file, line, Encoding.UTF8);
            }
            catch { }
        }
    }
}
