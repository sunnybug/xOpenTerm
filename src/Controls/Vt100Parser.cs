using System.Text;
using System.Windows.Media;

namespace xOpenTerm.Controls;

/// <summary>
/// VT100/ANSI 流式解析器：SGR（颜色/粗体）、CSI 光标/擦除、ESC E（NEL）、BEL 等。
/// </summary>
public sealed class Vt100Parser
{
    private enum State
    {
        Normal,
        Escape,
        CsiParam
    }

    private State _state = State.Normal;
    private readonly StringBuilder _csiBuffer = new();
    private readonly StringBuilder _textBuffer = new();

    private Brush _fg = Brushes.LightGray;
    private Brush _bg = Brushes.Transparent;
    private bool _bold;

    private static readonly Brush[] FgPalette = CreatePalette(
        "#000000", "#CD3131", "#0DBC79", "#E5E510", "#2472C8", "#BC3FBC", "#11A8CD", "#E5E5E5");
    private static readonly Brush[] BrightFg = CreatePalette(
        "#666666", "#F14C4C", "#23D18B", "#F5F543", "#3B8EEA", "#D670D6", "#29B8DB", "#E5E5E5");
    private static readonly Brush[] BgPalette = CreatePalette(
        "#000000", "#CD3131", "#0DBC79", "#E5E510", "#2472C8", "#BC3FBC", "#11A8CD", "#E5E5E5");
    private static readonly Brush[] BrightBg = CreatePalette(
        "#666666", "#F14C4C", "#23D18B", "#F5F543", "#3B8EEA", "#D670D6", "#29B8DB", "#E5E5E5");

    private static Brush[] CreatePalette(params string[] hex)
    {
        return hex.Select(h =>
        {
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(h);
            return new SolidColorBrush(c);
        }).ToArray();
    }

    /// <summary>
    /// 喂入数据，解析后通过回调输出文本段、换行、光标与擦除。
    /// </summary>
    public void Feed(string data, Action<Vt100Segment> onSegment, Action onNewLine)
    {
        Feed(data, onSegment, onNewLine, null, null, null, null, null, null, null, null);
    }

    /// <summary>
    /// 喂入数据（含 VT100 光标/擦除回调）。
    /// </summary>
    public void Feed(string data,
        Action<Vt100Segment> onSegment,
        Action onNewLine,
        Action<int, int>? onSetCursor,
        Action? onCarriageReturn,
        Action<int>? onEraseInDisplay,
        Action<int>? onEraseInLine,
        Action<int>? onCursorUp,
        Action<int>? onCursorDown,
        Action<int>? onCursorForward,
        Action<int>? onCursorBack)
    {
        foreach (var c in data)
            ProcessChar(c, onSegment, onNewLine, onSetCursor, onCarriageReturn, onEraseInDisplay, onEraseInLine,
                onCursorUp, onCursorDown, onCursorForward, onCursorBack);
    }

    private void ProcessChar(char c,
        Action<Vt100Segment> onSegment,
        Action onNewLine,
        Action<int, int>? onSetCursor,
        Action? onCarriageReturn,
        Action<int>? onEraseInDisplay,
        Action<int>? onEraseInLine,
        Action<int>? onCursorUp,
        Action<int>? onCursorDown,
        Action<int>? onCursorForward,
        Action<int>? onCursorBack)
    {
        switch (_state)
        {
            case State.Normal:
                if (c == '\x1b')
                {
                    FlushText(onSegment);
                    _state = State.Escape;
                }
                else if (c == '\n')
                {
                    FlushText(onSegment);
                    onNewLine();
                }
                else if (c == '\r')
                {
                    FlushText(onSegment);
                    onCarriageReturn?.Invoke();
                }
                else if (c == '\x07')
                {
                    // BEL：可在此扩展蜂鸣等
                }
                else if (c >= ' ' || c == '\t')
                {
                    _textBuffer.Append(c);
                }
                break;

            case State.Escape:
                if (c == '[')
                {
                    _csiBuffer.Clear();
                    _state = State.CsiParam;
                }
                else if (c == 'E')
                {
                    // NEL (Next Line) = CR+LF
                    FlushText(onSegment);
                    onCarriageReturn?.Invoke();
                    onNewLine();
                    _state = State.Normal;
                }
                else
                {
                    _state = State.Normal;
                    _textBuffer.Append(c);
                }
                break;

            case State.CsiParam:
                if (c >= ' ' && c <= '~')
                {
                    if (_csiBuffer.Length >= MaxCsiParamStrLength)
                    {
                        _csiBuffer.Clear();
                        _state = State.Normal;
                        break;
                    }
                    _csiBuffer.Append(c);
                    if (c >= '@' && c <= '~')
                    {
                        ApplyCsi(onSegment, onSetCursor, onEraseInDisplay, onEraseInLine,
                            onCursorUp, onCursorDown, onCursorForward, onCursorBack);
                        _csiBuffer.Clear();
                        _state = State.Normal;
                    }
                }
                else
                {
                    _csiBuffer.Clear();
                    _state = State.Normal;
                }
                break;
        }
    }

    private void FlushText(Action<Vt100Segment> onSegment)
    {
        if (_textBuffer.Length == 0) return;
        onSegment(new Vt100Segment(_textBuffer.ToString(), _fg!, _bg!, _bold));
        _textBuffer.Clear();
    }

    /// <summary>CSI 参数个数上限，防止畸形数据导致 List 无限扩容 (OutOfMemoryException)。</summary>
    private const int MaxCsiParams = 64;
    /// <summary>单个参数值上限，防止超大数值导致异常。</summary>
    private const int MaxParamValue = 65535;
    /// <summary>参数字符串最大解析长度，避免超长畸形输入长时间循环。</summary>
    private const int MaxCsiParamStrLength = 1024;

    /// <summary>
    /// 解析 CSI 参数字符串为整数列表（缺省为 1）。参数个数与数值均有限制，避免恶意/畸形输入导致崩溃。
    /// </summary>
    private static void ParseCsiParams(ReadOnlySpan<char> paramStr, List<int> outParams)
    {
        outParams.Clear();
        if (paramStr.Length > MaxCsiParamStrLength)
            paramStr = paramStr.Slice(0, MaxCsiParamStrLength);
        if (paramStr.Length == 0)
        {
            outParams.Add(1);
            return;
        }
        var i = 0;
        while (i < paramStr.Length && outParams.Count < MaxCsiParams)
        {
            int value = 0;
            while (i < paramStr.Length && paramStr[i] >= '0' && paramStr[i] <= '9')
            {
                value = value * 10 + (paramStr[i] - '0');
                if (value > MaxParamValue) value = MaxParamValue;
                i++;
            }
            outParams.Add(value == 0 ? 1 : value);
            while (i < paramStr.Length && (paramStr[i] == ';' || paramStr[i] == ':')) i++;
        }
        if (outParams.Count == 0) outParams.Add(1);
    }

    private void ApplyCsi(Action<Vt100Segment> onSegment,
        Action<int, int>? onSetCursor,
        Action<int>? onEraseInDisplay,
        Action<int>? onEraseInLine,
        Action<int>? onCursorUp,
        Action<int>? onCursorDown,
        Action<int>? onCursorForward,
        Action<int>? onCursorBack)
    {
        var s = _csiBuffer.ToString();
        var cmd = s.Length > 0 ? s[^1] : '\0';
        var paramStr = s.Length > 1 ? s.AsSpan(0, s.Length - 1) : ReadOnlySpan<char>.Empty;

        if (cmd == 'm')
        {
            ApplySgr(paramStr);
            return;
        }

        var pars = new List<int>();
        ParseCsiParams(paramStr, pars);
        int p0 = pars.Count > 0 ? pars[0] : 1;
        int p1 = pars.Count > 1 ? pars[1] : 1;

        switch (cmd)
        {
            case 'A': // CUU Cursor Up
                onCursorUp?.Invoke(p0);
                break;
            case 'B': // CUD Cursor Down
                onCursorDown?.Invoke(p0);
                break;
            case 'C': // CUF Cursor Forward
                onCursorForward?.Invoke(p0);
                break;
            case 'D': // CUB Cursor Back
                onCursorBack?.Invoke(p0);
                break;
            case 'H':
            case 'f': // CUP / HVP  (row;col 1-based)
                onSetCursor?.Invoke(Math.Max(0, p0 - 1), Math.Max(0, p1 - 1));
                break;
            case 'J': // ED Erase in Display: 0=below, 1=above, 2=all
                onEraseInDisplay?.Invoke(p0);
                break;
            case 'K': // EL Erase in Line: 0=to end, 1=to start, 2=all
                onEraseInLine?.Invoke(p0);
                break;
            case 'h':
            case 'l':
                // DECSET/DECRST 等模式：仅消费
                break;
            default:
                break;
        }
    }

    private void ApplySgr(ReadOnlySpan<char> paramStr)
    {
        if (paramStr.Length == 0)
        {
            ResetSgr();
            return;
        }

        var i = 0;
        while (i < paramStr.Length)
        {
            int value = 0;
            while (i < paramStr.Length && paramStr[i] >= '0' && paramStr[i] <= '9')
            {
                value = value * 10 + (paramStr[i] - '0');
                i++;
            }
            if (i < paramStr.Length && paramStr[i] == ';') i++;

            switch (value)
            {
                case 0: ResetSgr(); break;
                case 1: _bold = true; break;
                case 4: break; // underline 暂不实现
                case 22: _bold = false; break;
                case 30: case 31: case 32: case 33: case 34: case 35: case 36: case 37:
                    _fg = FgPalette[value - 30]; break;
                case 40: case 41: case 42: case 43: case 44: case 45: case 46: case 47:
                    _bg = BgPalette[value - 40]; break;
                case 39: _fg = Brushes.LightGray; break;
                case 49: _bg = Brushes.Transparent; break;
                case 90: case 91: case 92: case 93: case 94: case 95: case 96: case 97:
                    _fg = BrightFg[value - 90]; break;
                case 100: case 101: case 102: case 103: case 104: case 105: case 106: case 107:
                    _bg = BrightBg[value - 100]; break;
            }
        }
    }

    private void ResetSgr()
    {
        _fg = Brushes.LightGray;
        _bg = Brushes.Transparent;
        _bold = false;
    }

    /// <summary>
    /// 将未刷新的文本以当前属性输出（例如连接关闭前调用）。
    /// </summary>
    public void Flush(Action<Vt100Segment> onSegment)
    {
        FlushText(onSegment);
    }
}

/// <summary>
/// 一段带样式的终端文本。Foreground/Background 为 null 时使用控件默认色。
/// </summary>
public readonly record struct Vt100Segment(string Text, Brush? Foreground, Brush? Background, bool Bold);
