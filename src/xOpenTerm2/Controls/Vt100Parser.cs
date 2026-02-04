using System.Text;
using System.Windows.Media;

namespace xOpenTerm2.Controls;

/// <summary>
/// VT100/ANSI 流式解析器，解析 SGR（颜色/粗体）及常用 CSI，输出带样式的文本段。
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
    /// 喂入数据，解析后通过回调输出文本段与换行。
    /// </summary>
    public void Feed(string data, Action<Vt100Segment> onSegment, Action onNewLine)
    {
        foreach (var c in data)
            ProcessChar(c, onSegment, onNewLine);
    }

    private void ProcessChar(char c, Action<Vt100Segment> onSegment, Action onNewLine)
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
                    // \r 仅移动光标到行首，不换行；\r\n 时由 \n 负责换行
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
                else
                {
                    _state = State.Normal;
                    _textBuffer.Append(c);
                }
                break;

            case State.CsiParam:
                if (c >= ' ' && c <= '~')
                {
                    _csiBuffer.Append(c);
                    if (c >= '@' && c <= '~')
                    {
                        ApplyCsi(onSegment);
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
        onSegment(new Vt100Segment(_textBuffer.ToString(), _fg, _bg, _bold));
        _textBuffer.Clear();
    }

    private void ApplyCsi(Action<Vt100Segment> onSegment)
    {
        var s = _csiBuffer.ToString();
        var cmd = s.Length > 0 ? s[^1] : '\0';
        var paramStr = s.Length > 1 ? s.AsSpan(0, s.Length - 1) : ReadOnlySpan<char>.Empty;

        if (cmd == 'm')
        {
            ApplySgr(paramStr);
            return;
        }

        if (cmd == 'J' || cmd == 'K' || cmd == 'H' || cmd == 'f' || cmd == 'h' || cmd == 'l')
        {
            // 清屏/擦行/光标/模式：仅消费，不改变当前 SGR
            return;
        }

        // 其他 CSI：忽略
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
/// 一段带样式的终端文本。
/// </summary>
public readonly record struct Vt100Segment(string Text, Brush Foreground, Brush Background, bool Bold);
