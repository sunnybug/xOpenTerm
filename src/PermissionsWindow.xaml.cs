using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;

namespace xOpenTerm;

/// <summary>编辑远程文件/目录的 Unix 权限（rwxrwxrwx / 八进制）。</summary>
public partial class PermissionsWindow : Window
{
    private bool _updating;

    /// <summary>用户点击 Apply 后的权限模式（0–777），未应用则为 null。</summary>
    public int? ResultMode { get; private set; }

    public PermissionsWindow(string fileName, int initialMode)
    {
        InitializeComponent();
        FileLabel.Text = "Permissions for \"" + (fileName ?? "") + "\"";
        initialMode = (initialMode & 0x1FF) % 1000;
        if (initialMode > 777) initialMode = 644;
        SetMode(initialMode);
    }

    private void SetMode(int mode)
    {
        _updating = true;
        try
        {
            int u = (mode >> 6) & 7, g = (mode >> 3) & 7, o = mode & 7;
            UserRead.IsChecked = (u & 4) != 0;
            UserWrite.IsChecked = (u & 2) != 0;
            UserExecute.IsChecked = (u & 1) != 0;
            GroupRead.IsChecked = (g & 4) != 0;
            GroupWrite.IsChecked = (g & 2) != 0;
            GroupExecute.IsChecked = (g & 1) != 0;
            OtherRead.IsChecked = (o & 4) != 0;
            OtherWrite.IsChecked = (o & 2) != 0;
            OtherExecute.IsChecked = (o & 1) != 0;
            PermSymbolBox.Text = ToSymbolic(mode);
            OctalBox.Text = mode.ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _updating = false;
        }
    }

    private int GetModeFromCheckboxes()
    {
        int u = (UserRead.IsChecked == true ? 4 : 0) + (UserWrite.IsChecked == true ? 2 : 0) + (UserExecute.IsChecked == true ? 1 : 0);
        int g = (GroupRead.IsChecked == true ? 4 : 0) + (GroupWrite.IsChecked == true ? 2 : 0) + (GroupExecute.IsChecked == true ? 1 : 0);
        int o = (OtherRead.IsChecked == true ? 4 : 0) + (OtherWrite.IsChecked == true ? 2 : 0) + (OtherExecute.IsChecked == true ? 1 : 0);
        return u * 64 + g * 8 + o;
    }

    private static string ToSymbolic(int mode)
    {
        char P(int m, int bit) => (m & bit) != 0 ? (bit == 4 ? 'r' : bit == 2 ? 'w' : 'x') : '-';
        int u = (mode >> 6) & 7, g = (mode >> 3) & 7, o = mode & 7;
        return new string(new[]
        {
            P(u, 4), P(u, 2), P(u, 1),
            P(g, 4), P(g, 2), P(g, 1),
            P(o, 4), P(o, 2), P(o, 1)
        });
    }

    private static int? ParseSymbolic(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Length != 9) return null;
        int M(char c, int bit) => (c == 'r' || c == 'w' || c == 'x') ? bit : 0;
        int part(int offset)
        {
            char a = s[offset], b = s[offset + 1], c = s[offset + 2];
            return M(a, 4) + M(b, 2) + M(c, 1);
        }
        if (!Regex.IsMatch(s, "^[rwx-]{9}$", RegexOptions.IgnoreCase)) return null;
        return part(0) * 64 + part(3) * 8 + part(6);
    }

    private void CheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        SetMode(GetModeFromCheckboxes());
    }

    private void PermSymbolBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_updating) return;
        var m = ParseSymbolic(PermSymbolBox.Text);
        if (m.HasValue) SetMode(m.Value);
    }

    private void OctalBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_updating) return;
        if (int.TryParse(OctalBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 0 && n <= 777)
            SetMode(n);
    }

    private void ApplyBtn_Click(object sender, RoutedEventArgs e)
    {
        ResultMode = GetModeFromCheckboxes();
        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
