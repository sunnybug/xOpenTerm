using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace xOpenTerm;

/// <summary>关于弹窗：版本号、作者、GitHub 链接</summary>
public partial class AboutWindow : Window
{
    private const string GitHubUrl = "https://github.com/sunnybug/xOpenTerm";

    public AboutWindow(Window? owner)
    {
        InitializeComponent();
        Owner = owner;
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "—";
        AuthorText.Text = "sunnybug";
        GitHubLink.NavigateUri = new Uri(GitHubUrl);
    }

    private void GitHubLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo { FileName = e.Uri.AbsoluteUri, UseShellExecute = true }); } catch { }
        e.Handled = true;
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e) => Close();
}
