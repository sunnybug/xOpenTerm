using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows.Forms;
using xOpenTerm.Native;
using xOpenTerm.Services;
using Task = System.Threading.Tasks.Task;

namespace xOpenTerm.Controls;

/// <summary>
/// 采用 mRemoteNG 方式：嵌入 PuTTY/PuTTY NG 窗口显示 SSH 终端。
/// 使用与 mRemoteNG PuttyBase 相同的逻辑：-hwndparent（PuTTY NG）或 SetParent（经典 PuTTY）、命名管道传密码、Resize 等。
/// </summary>
public sealed class SshPuttyHostControl : Panel
{
    public SshPuttyHostControl()
    {
        Dock = DockStyle.Fill;
    }

    private Process? _puttyProcess;
    private IntPtr _puttyHandle;
    private bool _isPuttyNg;
    private readonly DisplayScale _display = new();

    /// <summary>默认 PuTTY 路径：优先使用当前工作目录下的 PuTTYNG.exe，否则为 PATH 中的 PuTTYNG.exe。</summary>
    public static string DefaultPuttyPath { get; set; } = GetDefaultPuttyPath();

    public event EventHandler? Closed;

    /// <summary>PuTTY 窗口已嵌入并显示时触发（可用于切换远程文件等）。</summary>
    public event EventHandler? Connected;

    public IntPtr PuttyHandle => _puttyHandle;

    public bool IsRunning => _puttyProcess != null && !_puttyProcess.HasExited;

    /// <param name="keyPassphrase">密钥口令（用于非 .ppk 密钥转 .ppk 时若需解密）。</param>
    /// <param name="useAgent">为 true 时使用 SSH Agent（Pageant），不传 -noagent；为 false 时传 -noagent 避免多密钥导致 "Too many authentication failures"。</param>
    /// <param name="fontName">保留参数，Windows 版 PuTTY 不支持命令行指定字体，仅用于兼容调用方。</param>
    /// <param name="fontSize">保留参数，Windows 版 PuTTY 不支持命令行指定字号，仅用于兼容调用方。</param>
    public void Connect(string host, int port, string username, string? password, string? keyPath, string puttyPath,
        string? keyPassphrase = null, bool useAgent = false, string? fontName = null, double fontSize = 14)
    {
        if (string.IsNullOrWhiteSpace(puttyPath) || !File.Exists(puttyPath))
        {
            var pathForLog = string.IsNullOrWhiteSpace(puttyPath) ? "(未配置或为空)" : puttyPath;
            ExceptionLog.WriteInfo($"PuTTY 路径无效或文件不存在: {pathForLog}");
            throw new FileNotFoundException("未找到 PuTTY 程序，请指定有效路径。", puttyPath);
        }

        _isPuttyNg = IsPuttyNg(puttyPath);

        // 仅单元测试时预取 host key 并写入 PuTTY 注册表；正式运行不自动忽略 host key 检查
        if (SessionManager.IsUnitTestMode)
        {
            try
            {
                Task.Run(() => SessionManager.TryCacheHostKeyForPutty(host, port, username ?? "", password, keyPath, keyPassphrase, useAgent)).Wait(TimeSpan.FromSeconds(6));
            }
            catch { /* 超时或失败不影响后续启动 PuTTY */ }
        }

        // 若为密钥文件登录且非 PuTTY 支持的 .ppk：优先使用同路径的 .ppk，否则用 puttygen 转换后使用
        keyPath = GetPuttyKeyPath(keyPath, puttyPath, keyPassphrase) ?? keyPath;

        var arguments = new List<string>();
        // 与 mRemoteNG 一致：使用 Agent 时先 -load 已保存会话（如 Default Settings），使 PuTTY 使用该会话中的 Pageant 等认证设置，再以命令行覆盖 host/port/user
        if (useAgent)
            arguments.AddRange(["-load", "Default Settings"]);
        arguments.Add("-ssh");
        arguments.Add("-2");
        // 与 mRemoteNG 一致：从不传 -noagent 时 PuTTY 会使用 Pageant；仅在不使用 Agent 时显式禁用，避免多密钥导致 "Too many authentication failures"
        if (!useAgent)
            arguments.Add("-noagent");
        // Windows 版 PuTTY 不支持 -fn；字体需在 PuTTY 选项或已保存会话中配置
        if (!string.IsNullOrEmpty(username))
            arguments.AddRange(["-l", username]);
        if (!string.IsNullOrEmpty(password))
        {
            if (_isPuttyNg)
            {
                // PuTTYNG 部分构建不支持 -pwfile，改用 -pw（密码会出现在进程参数中，建议优先使用密钥认证）
                arguments.Add("-pw");
                arguments.Add(password);
            }
            else
            {
                var pipeName = "xOpenTermPipe" + Guid.NewGuid().ToString("n")[..8];
                var thread = new Thread(() => CreatePipeServer(pipeName, password));
                thread.Start();
                arguments.AddRange(["-pwfile", $"\\\\.\\PIPE\\{pipeName}"]);
            }
        }
        if (!string.IsNullOrEmpty(keyPath) && File.Exists(keyPath))
            arguments.AddRange(["-i", keyPath]);
        arguments.AddRange(["-P", port.ToString()]);
        arguments.Add(host);

        if (_isPuttyNg)
            arguments.AddRange(["-hwndparent", Handle.ToString()]);

        // 与 mRemoteNG 一致：不设置 WorkingDirectory，子进程继承当前目录，避免影响 PuTTY 与 Pageant 的通信
        var startInfo = new ProcessStartInfo
        {
            FileName = puttyPath,
            UseShellExecute = false
        };
        foreach (var a in arguments)
            startInfo.ArgumentList.Add(a);

        _puttyProcess = new Process { StartInfo = startInfo };
        _puttyProcess.EnableRaisingEvents = true;
        _puttyProcess.Exited += (_, _) => OnClosed();
        _puttyProcess.Start();

        var waitMs = 30_000;
        _puttyProcess.WaitForInputIdle(waitMs);

        var startTicks = Environment.TickCount;
        while (_puttyHandle == IntPtr.Zero && Environment.TickCount - startTicks < waitMs)
        {
            if (_isPuttyNg)
                _puttyHandle = NativeMethods.FindWindowEx(Handle, IntPtr.Zero, null, null);
            else
            {
                _puttyProcess.Refresh();
                _puttyHandle = _puttyProcess.MainWindowHandle;
            }
            if (_puttyHandle == IntPtr.Zero)
                Thread.Sleep(10);
        }

        if (!_isPuttyNg && _puttyHandle != IntPtr.Zero)
            NativeMethods.SetParent(_puttyHandle, Handle);

        ResizePuttyWindow();

        // 延迟再次 Resize：首次 ResizePuttyWindow 时 Panel 可能尚未达到最终尺寸，
        // 200ms 后再触发一次，确保 PuTTY 终端列数与实际窗口匹配
        System.Windows.Forms.Timer delayResize = new() { Interval = 200 };
        delayResize.Tick += (_, _) =>
        {
            delayResize.Stop();
            delayResize.Dispose();
            ResizePuttyWindow();
        };
        delayResize.Start();

        try { Connected?.Invoke(this, EventArgs.Empty); } catch { }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ResizePuttyWindow();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        Close();
        base.OnHandleDestroyed(e);
    }

    public void Close()
    {
        ExceptionLog.WriteInfo("PuTTY Close 开始");
        var handle = _puttyHandle;
        _puttyHandle = IntPtr.Zero;
        // 先解除父子窗口，再结束进程，避免原生窗口在销毁时向宿主投递消息导致 SEHException
        if (handle != IntPtr.Zero && _puttyProcess != null)
        {
            try { NativeMethods.SetParent(handle, IntPtr.Zero); } catch { }
        }
        try
        {
            if (_puttyProcess != null && !_puttyProcess.HasExited)
                _puttyProcess.Kill();
        }
        catch { }
        try
        {
            _puttyProcess?.Dispose();
        }
        catch { }
        _puttyProcess = null;
        ExceptionLog.WriteInfo("PuTTY Close 结束");
    }

    public void FocusPutty()
    {
        if (_puttyHandle != IntPtr.Zero)
            NativeMethods.SetForegroundWindow(_puttyHandle);
    }

    private void ResizePuttyWindow()
    {
        if (_puttyHandle == IntPtr.Zero) return;
        try
        {
            var clientRect = ClientRectangle;
            if (clientRect.Size == System.Drawing.Size.Empty) return;

            if (_isPuttyNg)
            {
                NativeMethods.MoveWindow(_puttyHandle, clientRect.X, clientRect.Y, clientRect.Width, clientRect.Height, true);
            }
            else
            {
                int borderH = _display.ScaleHeight(SystemInformation.FrameBorderSize.Height);
                int borderW = _display.ScaleWidth(SystemInformation.FrameBorderSize.Width);
                int captionH = SystemInformation.CaptionHeight;
                NativeMethods.MoveWindow(_puttyHandle,
                    clientRect.X - borderW,
                    clientRect.Y - (captionH + borderH),
                    clientRect.Width + borderW * 2,
                    clientRect.Height + captionH + borderH * 2,
                    true);
            }
        }
        catch { }
    }

    private void OnClosed()
    {
        _puttyHandle = IntPtr.Zero;
        try { Closed?.Invoke(this, EventArgs.Empty); } catch { }
    }

    private static void CreatePipeServer(string pipeName, string password)
    {
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.None);
        server.WaitForConnection();
        using var writer = new StreamWriter(server, new UTF8Encoding(false)) { AutoFlush = true };
        writer.Write(password);
    }

    private static bool IsPuttyNg(string filename)
    {
        try
        {
            if (string.IsNullOrEmpty(filename) || !File.Exists(filename)) return false;
            var vi = FileVersionInfo.GetVersionInfo(filename);
            return (vi.InternalName ?? "").Contains("PuTTYNG", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// 解析供 PuTTY 使用的密钥路径：若已是 .ppk 则直接返回；否则查找 原路径.ppk，存在则用其，否则用 puttygen 转换并保存为 原路径.ppk 后返回。
    /// </summary>
    /// <returns>供 -i 使用的密钥路径，若无需密钥或解析失败则返回 null。</returns>
    private static string? GetPuttyKeyPath(string? keyPath, string puttyPath, string? keyPassphrase)
    {
        if (string.IsNullOrWhiteSpace(keyPath) || !File.Exists(keyPath))
            return null;

        const StringComparison cmp = StringComparison.OrdinalIgnoreCase;
        if (keyPath.EndsWith(".ppk", cmp))
            return keyPath;

        var ppkPath = keyPath + ".ppk";
        if (File.Exists(ppkPath))
        {
            ExceptionLog.WriteInfo($"[PuttyKey] 使用已有 .ppk: {ppkPath}");
            return ppkPath;
        }

        // 优先进程内转换（无交互），不支持的类型再尝试 puttygen
        if (PemToPpkConverter.TryConvert(keyPath, ppkPath, keyPassphrase))
        {
            ExceptionLog.WriteInfo($"[PuttyKey] 已转换并保存(进程内): {ppkPath}");
            return ppkPath;
        }
        if (TryConvertToPpkWithPuttygen(keyPath, ppkPath, puttyPath, keyPassphrase))
        {
            ExceptionLog.WriteInfo($"[PuttyKey] 已转换并保存(puttygen): {ppkPath}");
            return ppkPath;
        }

        return null;
    }

    private static bool TryConvertToPpkWithPuttygen(string keyPath, string ppkPath, string puttyPath, string? keyPassphrase)
    {
        var dir = Path.GetDirectoryName(puttyPath);
        if (string.IsNullOrEmpty(dir)) return false;

        var puttygen = Path.Combine(dir, "puttygen.exe");
        if (!File.Exists(puttygen))
            puttygen = Path.Combine(dir, "PuTTYgen.exe");
        if (!File.Exists(puttygen))
        {
            ExceptionLog.WriteInfo("[PuttyKey] 未找到 puttygen.exe，无法转换非 .ppk 密钥");
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = puttygen,
                ArgumentList = { keyPath, "-O", "private", "-o", ppkPath },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(startInfo);
            if (process == null) return false;

            if (!string.IsNullOrEmpty(keyPassphrase))
            {
                process.StandardInput.WriteLine(keyPassphrase);
                process.StandardInput.Close();
            }
            // 先等待退出再读 stderr，避免管道满导致死锁
            if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
            {
                try { process.Kill(); } catch { }
                ExceptionLog.WriteInfo("[PuttyKey] puttygen 超时");
                return false;
            }
            var err = process.StandardError.ReadToEnd();
            if (process.ExitCode != 0)
            {
                ExceptionLog.WriteInfo($"[PuttyKey] puttygen 退出码 {process.ExitCode}: {err?.Trim()}");
                return false;
            }
            return File.Exists(ppkPath);
        }
        catch (Exception ex)
        {
            ExceptionLog.Write(ex, "[PuttyKey] puttygen 转换失败");
            return false;
        }
    }

    private static string GetDefaultPuttyPath()
    {
        // 1) 当前工作目录下的 PuTTYNG.exe
        var cwd = Environment.CurrentDirectory;
        if (!string.IsNullOrEmpty(cwd))
        {
            var puttyInCwd = Path.Combine(cwd, "PuTTYNG.exe");
            if (File.Exists(puttyInCwd)) return puttyInCwd;
        }
        // 2) 未找到则使用环境变量 PATH 中的 PuTTYNG.exe
        return "PuTTYNG.exe";
    }

    private sealed class DisplayScale
    {
        public int ScaleHeight(int height) => height;
        public int ScaleWidth(int width) => width;
    }
}
