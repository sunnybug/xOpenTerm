using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace xOpenTerm;

/// <summary>金山云同步进度窗口：显示进度并支持取消。所有 UI 更新均通过 Dispatcher 切回 UI 线程。</summary>
public partial class KingCloudSyncWindow : Window
{
    private readonly Action? _onCancel;
    private bool _completed;

    public KingCloudSyncWindow(Action? onCancel)
    {
        InitializeComponent();
        _onCancel = onCancel;
    }

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
        ReportResult(detailMessage, success, null);
    }

    public void ReportResult(string summaryMessage, bool success, IReadOnlyList<string>? detailLines)
    {
        _completed = true;
        UpdateUi(() =>
        {
            if (DetailText != null)
            {
                DetailText.Text = summaryMessage;
                DetailText.Visibility = Visibility.Visible;
            }
            if (detailLines != null && detailLines.Count > 0 && DetailList != null && DetailListPanel != null)
            {
                DetailList.ItemsSource = detailLines.ToList();
                DetailListPanel.Visibility = Visibility.Visible;
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
