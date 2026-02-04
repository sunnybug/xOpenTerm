using System.Windows.Media;

namespace xOpenTerm2.Controls;

/// <summary>
/// 终端一格：字符 + 前景/背景/粗体。
/// </summary>
public readonly struct TerminalCell
{
    public readonly char Char;
    public readonly Brush? Foreground;
    public readonly Brush? Background;
    public readonly bool Bold;

    public TerminalCell(char c, Brush? fg, Brush? bg, bool bold)
    {
        Char = c;
        Foreground = fg;
        Background = bg;
        Bold = bold;
    }

    public bool StyleEquals(in TerminalCell other) =>
        Foreground == other.Foreground && Background == other.Background && Bold == other.Bold;
}

/// <summary>
/// 终端行缓冲：基于光标的 2D 网格，支持 VT100 光标移动、ED/EL、\r 覆盖等，按行输出线段供绘制。
/// </summary>
public sealed class TerminalBuffer
{
    private const int DefaultCols = 256;
    private const int MaxRows = 10000;

    private readonly int _cols;
    private readonly List<TerminalCell[]> _grid = new();
    private readonly TerminalCell _defaultCell;
    private int _cursorRow;
    private int _cursorCol;

    public TerminalBuffer(Brush? defaultForeground = null, int cols = DefaultCols)
    {
        _cols = cols;
        _defaultCell = new TerminalCell(' ', defaultForeground, Brushes.Transparent, false);
    }

    public int LineCount => _grid.Count;
    public int CursorRow => _cursorRow;
    public int CursorCol => _cursorCol;
    public int Cols => _cols;

    public void SetCursor(int row, int col)
    {
        _cursorRow = Math.Clamp(row, 0, MaxRows - 1);
        _cursorCol = Math.Clamp(col, 0, _cols - 1);
    }

    public void MoveCursorUp(int n = 1)
    {
        _cursorRow = Math.Max(0, _cursorRow - n);
    }

    public void MoveCursorDown(int n = 1)
    {
        _cursorRow += n;
        EnsureRow(_cursorRow);
    }

    public void MoveCursorForward(int n = 1)
    {
        _cursorCol = Math.Min(_cols - 1, _cursorCol + n);
    }

    public void MoveCursorBack(int n = 1)
    {
        _cursorCol = Math.Max(0, _cursorCol - n);
    }

    public void AddSegment(Vt100Segment segment)
    {
        if (segment.Text.Length == 0) return;
        EnsureRow(_cursorRow);
        var row = _grid[_cursorRow];
        var fg = segment.Foreground ?? _defaultCell.Foreground;
        var bg = segment.Background ?? _defaultCell.Background;
        foreach (var c in segment.Text)
        {
            if (_cursorCol >= _cols) break;
            row[_cursorCol] = new TerminalCell(c, fg, bg, segment.Bold);
            _cursorCol++;
        }
    }

    public void NewLine()
    {
        _cursorRow++;
        _cursorCol = 0;
        EnsureRow(_cursorRow);
    }

    /// <summary>回车：光标移到行首（后续写入会覆盖）。</summary>
    public void CarriageReturn()
    {
        _cursorCol = 0;
    }

    /// <summary>ED: 0=光标到屏末, 1=屏首到光标, 2=全屏。</summary>
    public void EraseInDisplay(int mode)
    {
        EnsureRow(_cursorRow);
        switch (mode)
        {
            case 0: // 从光标到屏末
                ClearLineRange(_cursorRow, _cursorCol, _cols - _cursorCol);
                for (int r = _cursorRow + 1; r < _grid.Count; r++)
                    ClearLineRange(r, 0, _cols);
                break;
            case 1: // 从屏首到光标
                for (int r = 0; r < _cursorRow; r++)
                    ClearLineRange(r, 0, _cols);
                ClearLineRange(_cursorRow, 0, _cursorCol + 1);
                break;
            case 2: // 全屏
                _grid.Clear();
                _cursorRow = 0;
                _cursorCol = 0;
                EnsureRow(0);
                break;
        }
    }

    /// <summary>EL: 0=光标到行末, 1=行首到光标, 2=整行。</summary>
    public void EraseInLine(int mode)
    {
        EnsureRow(_cursorRow);
        var row = _grid[_cursorRow];
        switch (mode)
        {
            case 0:
                ClearLineRange(_cursorRow, _cursorCol, _cols - _cursorCol);
                break;
            case 1:
                ClearLineRange(_cursorRow, 0, _cursorCol + 1);
                break;
            case 2:
                for (int i = 0; i < _cols; i++) row[i] = _defaultCell;
                break;
        }
    }

    private void EnsureRow(int row)
    {
        while (_grid.Count <= row && _grid.Count < MaxRows)
        {
            var line = new TerminalCell[_cols];
            for (int i = 0; i < _cols; i++) line[i] = _defaultCell;
            _grid.Add(line);
        }
    }

    private void ClearLineRange(int row, int startCol, int count)
    {
        if (row < 0 || row >= _grid.Count) return;
        var line = _grid[row];
        int end = Math.Min(startCol + count, _cols);
        for (int i = startCol; i < end; i++)
            line[i] = _defaultCell;
    }

    public void Clear()
    {
        _grid.Clear();
        _cursorRow = 0;
        _cursorCol = 0;
    }

    /// <summary>
    /// 返回所有行，每行由网格合并为 Vt100Segment 列表，供绘制。
    /// </summary>
    public IEnumerable<IReadOnlyList<Vt100Segment>> GetAllLines()
    {
        for (int r = 0; r < _grid.Count; r++)
        {
            var row = _grid[r];
            var list = new List<Vt100Segment>();
            int i = 0;
            while (i < _cols)
            {
                var cell = row[i];
                if (cell.Char == ' ' && cell.Foreground == _defaultCell.Foreground &&
                    cell.Background == _defaultCell.Background && !cell.Bold)
                {
                    i++;
                    continue;
                }
                int start = i;
                while (i < _cols && row[i].StyleEquals(cell))
                    i++;
                var text = new string(row.Skip(start).Take(i - start).Select(c => c.Char).ToArray());
                if (text.Length != 0)
                    list.Add(new Vt100Segment(text, cell.Foreground, cell.Background, cell.Bold));
            }
            yield return list;
        }
    }

    /// <summary>
    /// 兼容原有只读接口：Lines 为按行线段列表的只读视图（每次从网格重新生成）。
    /// </summary>
    public IReadOnlyList<IReadOnlyList<Vt100Segment>> Lines => GetAllLines().ToList();
}
