namespace xOpenTerm2.Controls;

/// <summary>
/// 终端行缓冲：按行存储 Vt100Segment 列表，供绘制层只渲染可见行。
/// </summary>
public sealed class TerminalBuffer
{
    private readonly List<List<Vt100Segment>> _lines = new();
    private List<Vt100Segment> _currentLine = new();

    public IReadOnlyList<IReadOnlyList<Vt100Segment>> Lines => _lines;

    public int LineCount => _lines.Count + (_currentLine.Count > 0 ? 1 : 0);

    public void AddSegment(Vt100Segment segment)
    {
        if (segment.Text.Length == 0) return;
        _currentLine.Add(segment);
    }

    public void NewLine()
    {
        _lines.Add(new List<Vt100Segment>(_currentLine));
        _currentLine.Clear();
    }

    public void Clear()
    {
        _lines.Clear();
        _currentLine.Clear();
    }

    /// <summary>
    /// 返回当前行 + 所有已完成行，用于绘制（当前行可能尚未换行）。
    /// </summary>
    public IEnumerable<IReadOnlyList<Vt100Segment>> GetAllLines()
    {
        foreach (var line in _lines)
            yield return line;
        if (_currentLine.Count > 0)
            yield return _currentLine;
    }
}
