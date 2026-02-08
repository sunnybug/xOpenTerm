using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows.Forms;
using xOpenTerm.Native;

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

    /// <summary>默认 PuTTY 路径：优先使用应用目录 bin/PuTTYNG.exe，否则为 PuTTYNG.exe。</summary>
    public static string DefaultPuttyPath { get; set; } = GetDefaultPuttyPath();

    public event EventHandler? Closed;

    /// <summary>PuTTY 窗口已嵌入并显示时触发（可用于切换远程文件等）。</summary>
    public event EventHandler? Connected;

    public IntPtr PuttyHandle => _puttyHandle;

    public bool IsRunning => _puttyProcess != null && !_puttyProcess.HasExited;

    /// <param name="useAgent">为 true 时使用 SSH Agent（Pageant），不传 -noagent；为 false 时传 -noagent 避免多密钥导致 "Too many authentication failures"。</param>
    /// <param name="fontName">保留参数，Windows 版 PuTTY 不支持命令行指定字体，仅用于兼容调用方。</param>
    /// <param name="fontSize">保留参数，Windows 版 PuTTY 不支持命令行指定字号，仅用于兼容调用方。</param>
    public void Connect(string host, int port, string username, string? password, string? keyPath, string puttyPath,
        bool useAgent = false, string? fontName = null, double fontSize = 14)
    {
        if (string.IsNullOrWhiteSpace(puttyPath) || !File.Exists(puttyPath))
        {
            throw new FileNotFoundException("未找到 PuTTY 程序，请指定有效路径。", puttyPath);
        }

        _isPuttyNg = IsPuttyNg(puttyPath);

        var arguments = new List<string>();
        arguments.Add("-ssh");
        arguments.Add("-2");
        // 仅在使用密码/单密钥时禁用 Pageant，避免多密钥导致 "Too many authentication failures"；使用 Agent 时必须允许 Pageant
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

        var puttyDir = Path.GetDirectoryName(puttyPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = puttyPath,
            UseShellExecute = false,
            WorkingDirectory = string.IsNullOrEmpty(puttyDir) ? Environment.CurrentDirectory : puttyDir
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
        _puttyHandle = IntPtr.Zero;
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

    private static string GetDefaultPuttyPath()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        // 1) 应用目录下的 bin\PuTTYNG.exe（例如部署时把 PuTTY 放在 exe 同级的 bin 子目录）
        var binPuttyNg = Path.Combine(appDir, "bin", "PuTTYNG.exe");
        if (File.Exists(binPuttyNg)) return binPuttyNg;
        // 2) 输出在 bin\Debug\net8.0-windows 时，上一级 bin 目录的 PuTTYNG.exe（项目根 bin）
        var parentBin = Path.GetDirectoryName(Path.GetDirectoryName(appDir));
        if (!string.IsNullOrEmpty(parentBin))
        {
            var puttyInParentBin = Path.Combine(parentBin, "PuTTYNG.exe");
            if (File.Exists(puttyInParentBin)) return puttyInParentBin;
        }
        return "PuTTYNG.exe";
    }

    private sealed class DisplayScale
    {
        public int ScaleHeight(int height) => height;
        public int ScaleWidth(int width) => width;
    }
}
