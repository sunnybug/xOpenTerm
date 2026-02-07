using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace xOpenTerm;

/// <summary>腾讯云同步进度窗口：显示进度并支持取消。所有 UI 更新均通过 Dispatcher 切回 UI 线程。</summary>
public partial class TencentCloudSyncWindow : Window
{
    private readonly Action? _onCancel;
    private bool _completed;

    public TencentCloudSyncWindow(Action? onCancel)
    {
        InitializeComponent();
        _onCancel = onCancel;
    }

    /// <summary>在拥有此窗口的 UI 线程上执行 action；若当前非 UI 线程则同步 Invoke，确保不跨线程访问 UI。</summary>
    private void UpdateUi(Action action)
    {
        var dispatcher = Dispatcher ?? Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(DispatcherPriority.Normal, action);
    }

    public void ReportProgress(string message, int current, int total)
    {
        UpdateUi(() =>
        {
            if (StatusText != null)
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
        UpdateUi(() =>
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
