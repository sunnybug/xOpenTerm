using System;
using System.ComponentModel;
using System.Windows;

namespace xOpenTerm;

/// <summary>腾讯云同步进度窗口：显示进度并支持取消。</summary>
public partial class TencentCloudSyncWindow : Window
{
    private readonly Action? _onCancel;
    private bool _completed;

    public TencentCloudSyncWindow(Action? onCancel)
    {
        InitializeComponent();
        _onCancel = onCancel;
    }

    public void ReportProgress(string message, int current, int total)
    {
        if (StatusText == null) return;
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = message;
            if (total > 0 && ProgressBar != null)
            {
                ProgressBar.Maximum = total;
                ProgressBar.Value = current;
            }
        });
    }

    public void ReportResult(string detailMessage, bool success)
    {
        _completed = true;
        Dispatcher.Invoke(() =>
        {
            if (DetailText != null)
            {
                DetailText.Text = detailMessage;
                DetailText.Visibility = Visibility.Visible;
            }
            if (StatusText != null)
                StatusText.Text = success ? "同步完成" : "同步结束";
            if (CancelBtn != null)
                CancelBtn.Content = "关闭";
        });
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_completed)
            Close();
        else
            _onCancel?.Invoke();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (!_completed)
            _onCancel?.Invoke();
    }
}
