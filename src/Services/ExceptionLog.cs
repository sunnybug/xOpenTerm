using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace xOpenTerm.Services;

/// <summary>
/// 日志级别枚举
/// </summary>
public enum LogLevel
{
    DEBUG,
    INFO,
    WARN,
    ERR,
    FATAL
}

/// <summary>
/// 将未捕获异常写入日志文件，便于排查崩溃。
/// 日志目录：当前进程目录下的 log 子目录。
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
                var cwd = Environment.CurrentDirectory;
                _logDir = Path.Combine(cwd, "log");
                if (!Directory.Exists(_logDir))
                    Directory.CreateDirectory(_logDir);
            }
            catch
            {
                _logDir = Path.Combine(".", "log");
            }
            return _logDir;
        }
    }

    /// <summary>
    /// 写入一条日志
    /// </summary>
    public static void Log(LogLevel level, string message, [System.Runtime.CompilerServices.CallerFilePath] string filePath = "", [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
    {
        var fileName = Path.GetFileName(filePath);
        var logLine = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}] [{level.ToString()}] [{fileName}:{lineNumber}] [{message}]{Environment.NewLine}";

        lock (Lock)
        {
            try
            {
                var file = Path.Combine(LogDirectory, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                File.AppendAllText(file, logLine, Encoding.UTF8);
            }
            catch
            {
                try { Trace.WriteLine("[xOpenTerm] ExceptionLog write failed: " + logLine); } catch { }
            }
        }
    }

    /// <summary>
    /// 写入调试日志
    /// </summary>
    public static void Debug(string message, [System.Runtime.CompilerServices.CallerFilePath] string filePath = "", [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
    {
        Log(LogLevel.DEBUG, message, filePath, lineNumber);
    }

    /// <summary>
    /// 写入信息日志
    /// </summary>
    public static void Info(string message, [System.Runtime.CompilerServices.CallerFilePath] string filePath = "", [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
    {
        Log(LogLevel.INFO, message, filePath, lineNumber);
    }

    /// <summary>
    /// 写入警告日志
    /// </summary>
    public static void Warn(string message, [System.Runtime.CompilerServices.CallerFilePath] string filePath = "", [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
    {
        Log(LogLevel.WARN, message, filePath, lineNumber);
    }

    /// <summary>
    /// 写入错误日志
    /// </summary>
    public static void Error(string message, [System.Runtime.CompilerServices.CallerFilePath] string filePath = "", [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
    {
        Log(LogLevel.ERR, message, filePath, lineNumber);
    }

    /// <summary>
    /// 写入致命错误日志
    /// </summary>
    public static void Fatal(string message, [System.Runtime.CompilerServices.CallerFilePath] string filePath = "", [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
    {
        Log(LogLevel.FATAL, message, filePath, lineNumber);
    }

    /// <summary>
    /// 写入一条异常日志（含时间、消息、堆栈），文件名带日期便于按天查看。
    /// </summary>
    /// <param name="toCrashLog">为 true 时写入 _crash.log（未处理异常）；为 false 时仅写入当日普通日志（已处理的业务异常，如云同步失败）。</param>
    public static void Write(Exception ex, string? context = null, bool toCrashLog = true, [System.Runtime.CompilerServices.CallerFilePath] string filePath = "", [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
    {
        var sb = new StringBuilder();
        sb.AppendLine("----------------------------------------");
        var level = toCrashLog ? "FATAL" : "ERR";
        sb.AppendLine($"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}] [{level}] [{Path.GetFileName(filePath)}:{lineNumber}] Exception occurred");
        if (!string.IsNullOrEmpty(context))
            sb.AppendLine($"Context: {context}");
        sb.AppendLine($"Message: {ex.Message}");
        sb.AppendLine($"Type: {ex.GetType().FullName}");
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
                var fileName = DateTime.Now.ToString("yyyy-MM-dd") + (toCrashLog ? "_crash.log" : ".log");
                var file = Path.Combine(LogDirectory, fileName);
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
        Info(message);
    }
}
