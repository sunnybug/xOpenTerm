using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace xOpenTerm.Controls;

/// <summary>
/// 支持内联编辑的文本框控件，捕获所有编辑键防止被父控件拦截。
/// 支持：Enter(保存)、Escape(取消)、Delete、Insert、Backspace、Home、End、方向键、Ctrl组合键等。
/// </summary>
public class InlineEditTextBox : System.Windows.Controls.TextBox
{
    private bool _isEndingEdit;

    public InlineEditTextBox()
    {
        // 确保编辑键不被父控件拦截
        PreviewKeyDown += OnPreviewKeyDown;
        KeyDown += OnKeyDown;
        LostFocus += OnLostFocus;
    }

    /// <summary>当用户按下Enter时触发，参数为true表示提交修改</summary>
    public event EventHandler<bool>? EditEnded;

    /// <summary>开始编辑时调用，自动聚焦并将光标置于文本末尾</summary>
    public void BeginEdit()
    {
        Focus();
        CaretIndex = Text?.Length ?? 0;
    }

    /// <summary>结束编辑</summary>
    public void EndEdit(bool commit)
    {
        if (_isEndingEdit) return;
        _isEndingEdit = true;

        PreviewKeyDown -= OnPreviewKeyDown;
        KeyDown -= OnKeyDown;
        LostFocus -= OnLostFocus;

        EditEnded?.Invoke(this, commit);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 这些键需要被TextBox处理，不能被父控件（如TreeView）拦截
        switch (e.Key)
        {
            case Key.Delete:
            case Key.Insert:
            case Key.Home:
            case Key.End:
            case Key.Left:
            case Key.Right:
            case Key.Up:
            case Key.Down:
            case Key.PageUp:
            case Key.PageDown:
            case Key.Tab:
                // 让这些键只在TextBox内部处理
                e.Handled = true;
                // 手动触发相应的编辑操作
                HandleEditKey(e.Key);
                break;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                EndEdit(true);
                break;
            case Key.Escape:
                e.Handled = true;
                EndEdit(false);
                break;
            default:
                // 其他所有键都让TextBox正常处理
                e.Handled = false;
                break;
        }
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        EndEdit(true);
    }

    private void HandleEditKey(Key key)
    {
        var caretIndex = CaretIndex;
        var text = Text ?? "";

        switch (key)
        {
            case Key.Delete:
                if (caretIndex < text.Length)
                {
                    Text = text.Remove(caretIndex, 1);
                    CaretIndex = caretIndex;
                }
                break;
            case Key.Insert:
                // 切换插入/覆盖模式（WPF TextBox默认不支持覆盖模式，这里仅占位）
                break;
            case Key.Home:
                CaretIndex = 0;
                break;
            case Key.End:
                CaretIndex = text.Length;
                break;
            case Key.Left:
                if (caretIndex > 0)
                    CaretIndex = caretIndex - 1;
                break;
            case Key.Right:
                if (caretIndex < text.Length)
                    CaretIndex = caretIndex + 1;
                break;
        }
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        // 确保文本输入正常工作
        base.OnPreviewTextInput(e);
    }
}
