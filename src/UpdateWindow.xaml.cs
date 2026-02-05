using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Windows;

namespace xOpenTerm;

/// <summary>从 GitHub Releases 检查更新</summary>
public partial class UpdateWindow : Window
{
    private const string GitHubReleasesUrl = "https://api.github.com/repos/sunnybug/xOpenTerm/releases/latest";
    private string? _latestTag;
    private string? _releaseUrl;

    public UpdateWindow(Window? owner)
    {
        InitializeComponent();
        Owner = owner;
        Loaded += (_, _) => CheckUpdate();
    }

    private static string GetCurrentVersion()
    {
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "0.0.0";
    }

    private static int[] ParseVersion(string s)
    {
        var cleaned = s.TrimStart('v', 'V').Split('-')[0];
        return cleaned.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
    }

    private static bool IsNewer(string latest, string current)
    {
        var a = ParseVersion(latest);
        var b = ParseVersion(current);
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var na = i < a.Length ? a[i] : 0;
            var nb = i < b.Length ? b[i] : 0;
            if (na > nb) return true;
            if (na < nb) return false;
        }
        return false;
    }

    private async void CheckUpdate()
    {
        StatusText.Text = "正在从 GitHub 获取最新版本…";
        OpenDownloadBtn.Visibility = Visibility.Collapsed;
        RetryBtn.Visibility = Visibility.Collapsed;
        DetailText.Visibility = Visibility.Collapsed;
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "xOpenTerm");
            var resp = await client.GetAsync(GitHubReleasesUrl);
            if (!resp.IsSuccessStatusCode)
            {
                StatusText.Text = "检查更新失败";
                DetailText.Text = resp.StatusCode == System.Net.HttpStatusCode.NotFound ? "暂无发布版本或仓库不可访问" : $"请求失败: {resp.StatusCode}";
                DetailText.Visibility = Visibility.Visible;
                RetryBtn.Visibility = Visibility.Visible;
                return;
            }
            var json = await resp.Content.ReadAsStringAsync();
            _releaseUrl = System.Text.RegularExpressions.Regex.Match(json, @"""html_url""\s*:\s*""([^""]+)""").Groups[1].Value;
            _latestTag = System.Text.RegularExpressions.Regex.Match(json, @"""tag_name""\s*:\s*""([^""]+)""").Groups[1].Value;
            var cur = GetCurrentVersion();
            var latestVer = _latestTag.TrimStart('v', 'V');
            if (IsNewer(latestVer, cur))
            {
                StatusText.Text = $"发现新版本：{_latestTag}";
                DetailText.Text = $"当前版本：{cur}";
                DetailText.Visibility = Visibility.Visible;
                OpenDownloadBtn.Visibility = Visibility.Visible;
            }
            else
            {
                StatusText.Text = "已是最新版本";
                DetailText.Text = $"当前版本：{cur}";
                DetailText.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "检查更新失败";
            DetailText.Text = ex.Message;
            DetailText.Visibility = Visibility.Visible;
            RetryBtn.Visibility = Visibility.Visible;
        }
    }

    private void OpenDownloadBtn_Click(object sender, RoutedEventArgs e)
    {
        var url = _releaseUrl ?? "https://github.com/sunnybug/xOpenTerm/releases";
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { }
    }

    private void RetryBtn_Click(object sender, RoutedEventArgs e) => CheckUpdate();

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}
