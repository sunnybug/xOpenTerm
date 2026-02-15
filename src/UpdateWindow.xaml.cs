using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace xOpenTerm;

/// <summary>从 GitHub Releases 检查更新，支持应用内直接下载并更新</summary>
public partial class UpdateWindow : Window
{
    private const string GitHubReleasesUrl = "https://api.github.com/repos/sunnybug/xOpenTerm/releases/latest";
    private string? _latestTag;
    private string? _releaseUrl;
    private string? _zipDownloadUrl;

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

    private static string? ParseZipDownloadUrl(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var nameProp))
                    continue;
                var name = nameProp.GetString();
                if (string.IsNullOrEmpty(name) || !name.StartsWith("xOpenTerm-v", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (asset.TryGetProperty("browser_download_url", out var urlProp))
                    return urlProp.GetString();
            }
        }
        catch { /* ignore parse error */ }
        return null;
    }

    private async void CheckUpdate()
    {
        StatusText.Text = "正在从 GitHub 获取最新版本…";
        DirectUpdateBtn.Visibility = Visibility.Collapsed;
        OpenReleasePageBtn.Visibility = Visibility.Collapsed;
        RetryBtn.Visibility = Visibility.Collapsed;
        DetailText.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Collapsed;
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
            _zipDownloadUrl = ParseZipDownloadUrl(json);
            var cur = GetCurrentVersion();
            var latestVer = _latestTag.TrimStart('v', 'V');
            if (IsNewer(latestVer, cur))
            {
                StatusText.Text = $"发现新版本：{_latestTag}";
                DetailText.Text = $"当前版本：{cur}";
                DetailText.Visibility = Visibility.Visible;
                if (!string.IsNullOrEmpty(_zipDownloadUrl))
                    DirectUpdateBtn.Visibility = Visibility.Visible;
                OpenReleasePageBtn.Visibility = Visibility.Visible;
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

    private void OpenReleasePageBtn_Click(object sender, RoutedEventArgs e)
    {
        var url = _releaseUrl ?? "https://github.com/sunnybug/xOpenTerm/releases";
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { }
    }

    private async void DirectUpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_zipDownloadUrl) || string.IsNullOrEmpty(_latestTag))
            return;
        var tag = _latestTag.TrimStart('v', 'V');
        var tempRoot = Path.Combine(Path.GetTempPath(), "xOpenTerm-update");
        var zipPath = Path.Combine(tempRoot, $"xOpenTerm-v{tag}.zip");
        var extractedDir = Path.Combine(tempRoot, "extracted");
        DirectUpdateBtn.Visibility = Visibility.Collapsed;
        OpenReleasePageBtn.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        ProgressBar.Value = 0;
        ProgressText.Text = "0%";
        try
        {
            Directory.CreateDirectory(tempRoot);
            StatusText.Text = "正在下载更新包…";
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "xOpenTerm");
            using var resp = await client.GetAsync(_zipDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? 0L;
            await using (var stream = await resp.Content.ReadAsStreamAsync())
            await using (var file = File.Create(zipPath))
            {
                var buffer = new byte[81920];
                long read = 0;
                int count;
                while ((count = await stream.ReadAsync(buffer)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, count));
                    read += count;
                    var pct = total > 0 ? (int)(100 * read / total) : 0;
                    Dispatcher.Invoke(() =>
                    {
                        ProgressBar.Value = pct;
                        ProgressText.Text = total > 0 ? $"{pct}% ({read / 1024 / 1024} MB / {total / 1024 / 1024} MB)" : $"{read / 1024 / 1024} MB";
                    });
                }
            }
            StatusText.Text = "正在解压…";
            ProgressText.Text = "解压中…";
            if (Directory.Exists(extractedDir))
                Directory.Delete(extractedDir, true);
            ZipFile.ExtractToDirectory(zipPath, extractedDir);
            var targetDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var pid = Environment.ProcessId;
            var result = MessageBox.Show("更新包已就绪，是否立即退出并更新？", "直接更新", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                ProgressPanel.Visibility = Visibility.Collapsed;
                DirectUpdateBtn.Visibility = Visibility.Visible;
                OpenReleasePageBtn.Visibility = Visibility.Visible;
                return;
            }
            var scriptPath = Path.Combine(tempRoot, "apply.ps1");
            const string script = @"param([string]$targetDir,[string]$extractedDir,[int]$pidToWait)
while (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 200 }
Copy-Item -Path ""$extractedDir\*"" -Destination $targetDir -Recurse -Force
Start-Process -FilePath ""$targetDir\xOpenTerm.exe""
";
            await File.WriteAllTextAsync(scriptPath, script);
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                ArgumentList = { "-ExecutionPolicy", "Bypass", "-File", scriptPath, targetDir, extractedDir, pid.ToString() },
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            StatusText.Text = "更新失败";
            DetailText.Text = ex.Message;
            DetailText.Visibility = Visibility.Visible;
            ProgressPanel.Visibility = Visibility.Collapsed;
            DirectUpdateBtn.Visibility = Visibility.Visible;
            OpenReleasePageBtn.Visibility = Visibility.Visible;
        }
    }

    private void RetryBtn_Click(object sender, RoutedEventArgs e) => CheckUpdate();

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}
