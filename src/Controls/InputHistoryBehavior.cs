using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using xOpenTerm.Services;

namespace xOpenTerm.Controls;

/// <summary>为 TextBox 提供输入历史下拉：附加属性 Key 指定字段键，输入时过滤历史并可点击填入。</summary>
public static class InputHistoryBehavior
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key",
        typeof(string),
        typeof(InputHistoryBehavior),
        new PropertyMetadata(null, OnKeyChanged));

    public static string? GetKey(System.Windows.Controls.TextBox element) => (string?)element.GetValue(KeyProperty);
    public static void SetKey(System.Windows.Controls.TextBox element, string? value) => element.SetValue(KeyProperty, value);

    private static void OnKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Controls.TextBox tb) return;
        var key = e.NewValue as string;
        if (string.IsNullOrWhiteSpace(key))
        {
            Detach(tb);
            return;
        }
        Attach(tb, key);
    }

    private static readonly Dictionary<System.Windows.Controls.TextBox, object> _attached = new();

    private static void Attach(System.Windows.Controls.TextBox tb, string fieldKey)
    {
        if (_attached.ContainsKey(tb)) return;
        var popup = new System.Windows.Controls.Primitives.Popup
        {
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            PlacementTarget = tb,
            StaysOpen = false,
            AllowsTransparency = true
        };
        var listBox = new System.Windows.Controls.ListBox
        {
            MaxHeight = 200,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0, 2, 0, 2)
        };
        try
        {
            listBox.SetResourceReference(System.Windows.Controls.ListBox.BackgroundProperty, "BgInput");
            listBox.SetResourceReference(System.Windows.Controls.ListBox.ForegroundProperty, "TextPrimary");
            listBox.SetResourceReference(System.Windows.Controls.ListBox.BorderBrushProperty, "BorderBrush");
        }
        catch { /* 设计器或资源未加载时忽略 */ }
        var scrollViewer = new System.Windows.Controls.ScrollViewer
        {
            Content = listBox,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch
        };
        popup.Child = new System.Windows.Controls.Border
        {
            Child = scrollViewer,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4)
        };
        try
        {
            ((System.Windows.Controls.Border)popup.Child).SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "BgInput");
            ((System.Windows.Controls.Border)popup.Child).SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "BorderBrush");
        }
        catch { }

        void UpdatePopupWidth()
        {
            popup.Width = tb.ActualWidth > 0 ? tb.ActualWidth : tb.Width;
        }

        void RefreshFilter()
        {
            var service = InputHistoryService.GetInstance();
            var full = service.GetHistory(fieldKey);
            var filter = (tb.Text ?? "").Trim();
            var filtered = string.IsNullOrEmpty(filter)
                ? full.Take(20).ToList()
                : full.Where(s => s.Contains(filter, StringComparison.OrdinalIgnoreCase)).Take(20).ToList();
            listBox.ItemsSource = filtered;
            listBox.SelectedIndex = filtered.Count > 0 ? 0 : -1;
            if (filtered.Count > 0 && popup.IsOpen == false)
            {
                UpdatePopupWidth();
                popup.IsOpen = true;
            }
            else if (filtered.Count == 0)
                popup.IsOpen = false;
        }

        void ClosePopup()
        {
            popup.IsOpen = false;
        }

        void SelectItem(string? item)
        {
            if (item == null) return;
            tb.Text = item;
            tb.CaretIndex = item.Length;
            ClosePopup();
        }

        listBox.MouseLeftButtonUp += (_, _) =>
        {
            if (listBox.SelectedItem is string s)
                SelectItem(s);
        };
        listBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && listBox.SelectedItem is string s)
            {
                SelectItem(s);
                e.Handled = true;
            }
        };

        tb.TextChanged += (_, _) => RefreshFilter();
        tb.GotFocus += (_, _) =>
        {
            if ((tb.Text ?? "").Length == 0)
                RefreshFilter();
            else
                RefreshFilter();
        };
        tb.LostFocus += (_, _) =>
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(() =>
            {
                if (!popup.IsOpen) return;
                var focused = Keyboard.FocusedElement as DependencyObject;
                if (focused != null && IsDescendantOf((DependencyObject)popup.Child, focused))
                    return;
                ClosePopup();
            }, System.Windows.Threading.DispatcherPriority.Background);
        };
        tb.PreviewKeyDown += (_, e) =>
        {
            if (!popup.IsOpen) return;
            if (e.Key == Key.Escape) { ClosePopup(); e.Handled = true; }
            if (e.Key == Key.Down && listBox.Items.Count > 0)
            {
                var idx = listBox.SelectedIndex < 0 ? 0 : Math.Min(listBox.SelectedIndex + 1, listBox.Items.Count - 1);
                listBox.SelectedIndex = idx;
                listBox.ScrollIntoView(listBox.Items[idx]);
                e.Handled = true;
            }
            if (e.Key == Key.Up && listBox.Items.Count > 0)
            {
                var idx = listBox.SelectedIndex <= 0 ? 0 : listBox.SelectedIndex - 1;
                listBox.SelectedIndex = idx;
                listBox.ScrollIntoView(listBox.Items[idx]);
                e.Handled = true;
            }
            if (e.Key == Key.Enter && listBox.SelectedItem is string s)
            {
                SelectItem(s);
                e.Handled = true;
            }
        };
        tb.Loaded += (_, _) => UpdatePopupWidth();
        tb.SizeChanged += (_, _) => UpdatePopupWidth();

        popup.Opened += (_, _) => UpdatePopupWidth();

        _attached[tb] = new object();
        tb.Unloaded += (_, _) => Detach(tb);
    }

    private static void Detach(System.Windows.Controls.TextBox tb)
    {
        _attached.Remove(tb);
    }

    /// <summary>从窗口内收集所有设置了 InputHistory.Key 的 TextBox 的 (Key, Text)。</summary>
    public static IEnumerable<(string Key, string Text)> CollectKeysAndTexts(Window window)
    {
        if (window == null) yield break;
        foreach (var tb in FindVisualChildren<System.Windows.Controls.TextBox>(window))
        {
            var key = GetKey(tb);
            if (string.IsNullOrWhiteSpace(key)) continue;
            var text = tb.Text ?? "";
            yield return (key, text);
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) yield return t;
            foreach (var desc in FindVisualChildren<T>(child))
                yield return desc;
        }
    }

    private static bool IsDescendantOf(DependencyObject parent, DependencyObject? node)
    {
        while (node != null)
        {
            if (node == parent) return true;
            node = VisualTreeHelper.GetParent(node);
        }
        return false;
    }
}
