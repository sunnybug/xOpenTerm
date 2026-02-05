using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace xOpenTerm.Controls;

public partial class TerminalControl : UserControl
{
    public event EventHandler<string>? DataToSend;

    private readonly TerminalBuffer _buffer = new();
    private readonly Vt100Parser _parser = new();
    private TerminalSurface? _surface;

    public TerminalControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _surface = new TerminalSurface
        {
            Buffer = _buffer,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14,
            DefaultForeground = (Brush)FindResource("TextPrimary")
        };
        _surface.KeyDown += TerminalSurface_KeyDown;
        _surface.MouseDown += (_, _) => _surface.Focus();
        SurfaceHost.Child = _surface;
    }

    public void Append(string data)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Append(data));
            return;
        }

        _parser.Feed(data,
            seg => _buffer.AddSegment(seg),
            () => _buffer.NewLine(),
            (r, c) => _buffer.SetCursor(r, c),
            () => _buffer.CarriageReturn(),
            mode => _buffer.EraseInDisplay(mode),
            mode => _buffer.EraseInLine(mode),
            n => _buffer.MoveCursorUp(n),
            n => _buffer.MoveCursorDown(n),
            n => _buffer.MoveCursorForward(n),
            n => _buffer.MoveCursorBack(n));

        _surface?.InvalidateMeasure();
        _surface?.InvalidateVisual();

        ScrollViewer.ScrollToBottom();
    }

    public void Clear()
    {
        _buffer.Clear();
        _surface?.InvalidateMeasure();
        _surface?.InvalidateVisual();
    }

    private void TerminalSurface_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataToSend == null) return;
        var c = KeyToChar(e);
        if (c != null)
        {
            DataToSend(this, c);
            e.Handled = true;
        }
    }

    private static string? KeyToChar(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            return "\r\n";
        if (e.Key == Key.Back)
            return "\x7f";
        if (e.Key == Key.Tab)
            return "\t";
        var text = e.Key switch
        {
            Key.Space => " ",
            Key.Left => "\x1b[D",
            Key.Right => "\x1b[C",
            Key.Up => "\x1b[A",
            Key.Down => "\x1b[B",
            Key.Home => "\x1b[H",
            Key.End => "\x1b[F",
            _ => null
        };
        if (text != null) return text;
        var c = MapKeyToChar(e);
        return c != null ? c.ToString() : null;
    }

    private static char? MapKeyToChar(KeyEventArgs e)
    {
        if (e.Key >= Key.A && e.Key <= Key.Z)
        {
            var c = (char)('a' + (e.Key - Key.A));
            return (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? char.ToUpperInvariant(c) : c;
        }
        if (e.Key >= Key.D0 && e.Key <= Key.D9)
        {
            var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            return e.Key switch
            {
                Key.D1 => shift ? '!' : '1',
                Key.D2 => shift ? '@' : '2',
                Key.D3 => shift ? '#' : '3',
                Key.D4 => shift ? '$' : '4',
                Key.D5 => shift ? '%' : '5',
                Key.D6 => shift ? '^' : '6',
                Key.D7 => shift ? '&' : '7',
                Key.D8 => shift ? '*' : '8',
                Key.D9 => shift ? '(' : '9',
                Key.D0 => shift ? ')' : '0',
                _ => null
            };
        }
        if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
            return (char)('0' + (e.Key - Key.NumPad0));
        return e.Key switch
        {
            Key.OemComma => (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? '<' : ',',
            Key.OemPeriod => (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? '>' : '.',
            Key.OemMinus => (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? '_' : '-',
            Key.OemPlus => (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? '+' : '=',
            Key.OemBackslash => (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? '|' : '\\',
            Key.Oem1 => (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? ':' : ';',
            Key.Oem2 => (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? '?' : '/',
            Key.Oem3 => (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? '~' : '`',
            Key.Oem4 => (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? '{' : '[',
            Key.Oem5 => (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? '}' : ']',
            Key.Oem6 => (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? '"' : '\'',
            _ => null
        };
    }
}
