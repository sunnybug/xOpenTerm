using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace xOpenTerm.Controls;

/// <summary>
/// 自定义绘制终端内容：仅绘制可见行，使用 FormattedText 按段着色。
/// </summary>
public sealed class TerminalSurface : FrameworkElement
{
    private TerminalBuffer? _buffer;
    private double _lineHeight = 18;
    private double _charWidth = 8.5;
    private Typeface? _typeface;
    private double _fontSize = 14;

    public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register(
        nameof(FontFamily), typeof(FontFamily), typeof(TerminalSurface),
        new PropertyMetadata(new FontFamily("Consolas"), OnMetricsChanged));

    public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(
        nameof(FontSize), typeof(double), typeof(TerminalSurface),
        new PropertyMetadata(14.0, OnMetricsChanged));

    public static readonly DependencyProperty DefaultForegroundProperty = DependencyProperty.Register(
        nameof(DefaultForeground), typeof(Brush), typeof(TerminalSurface),
        new PropertyMetadata(Brushes.LightGray));

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public Brush DefaultForeground
    {
        get => (Brush)GetValue(DefaultForegroundProperty);
        set => SetValue(DefaultForegroundProperty, value);
    }

    private static void OnMetricsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TerminalSurface s)
            s.UpdateMetrics();
    }

    public TerminalBuffer? Buffer
    {
        get => _buffer;
        set
        {
            _buffer = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public TerminalSurface()
    {
        Focusable = true;
        ClipToBounds = true;
        UpdateMetrics();
    }

    private void UpdateMetrics()
    {
        _fontSize = FontSize;
        _typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var ft = CreateFormattedText("Ay", _typeface, _fontSize, Brushes.White, false, 1.0);
        _lineHeight = Math.Ceiling(ft.Height * 1.2);
        _charWidth = CreateFormattedText("W", _typeface, _fontSize, Brushes.White, false, 1.0).Width;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_buffer == null) return new Size(0, 0);
        var lineCount = _buffer.LineCount;
        if (lineCount == 0) return new Size(0, 0);
        // 使用可用宽度（如 ScrollViewer 视口），避免固定 80 列导致行过窄
        var width = availableSize.Width > 0 && !double.IsInfinity(availableSize.Width)
            ? availableSize.Width
            : 120 * _charWidth;
        var height = lineCount * _lineHeight;
        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_buffer == null) return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var scrollViewer = FindScrollViewer(this);
        double offsetY = 0, viewportHeight = ActualHeight;
        if (scrollViewer != null)
        {
            offsetY = scrollViewer.VerticalOffset;
            viewportHeight = scrollViewer.ViewportHeight;
        }

        int firstLine = (int)(offsetY / _lineHeight);
        int lastLine = (int)Math.Ceiling((offsetY + viewportHeight) / _lineHeight);

        var allLines = _buffer.GetAllLines().ToList();
        for (int i = firstLine; i <= lastLine && i < allLines.Count; i++)
        {
            var line = allLines[i];
            double y = i * _lineHeight;
            double x = 0;
            foreach (var seg in line)
            {
                var fg = seg.Foreground ?? DefaultForeground;
                var ft = CreateFormattedText(seg.Text, _typeface!, _fontSize, fg, seg.Bold, dpi);
                var rect = new Rect(x, y, ft.Width, _lineHeight);
                if (seg.Background != null && seg.Background != Brushes.Transparent)
                    dc.DrawRectangle(seg.Background, null, rect);
                dc.DrawText(ft, new Point(x, y));
                x += ft.Width;
            }
        }
    }

    private static FormattedText CreateFormattedText(string text, Typeface typeface, double fontSize, Brush foreground, bool bold, double pixelsPerDip)
    {
        var weight = bold ? FontWeights.Bold : FontWeights.Normal;
        var tf = new Typeface(typeface.FontFamily, typeface.Style, weight, typeface.Stretch);
        return new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            tf,
            fontSize,
            foreground,
            pixelsPerDip);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject child)
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is ScrollViewer sv) return sv;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }
}
