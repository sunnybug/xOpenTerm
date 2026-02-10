using System.Windows;
using System.Windows.Controls;

namespace xOpenTerm;

/// <summary>新增金山云组：输入组名与金山云 AccessKey。</summary>
public partial class KingCloudGroupAddWindow : Window
{
    public string GroupName => NameBox?.Text?.Trim() ?? "金山云";
    public string AccessKeyId => AccessKeyIdBox?.Text?.Trim() ?? "";
    public string AccessKeySecret => AccessKeySecretBox?.Password ?? "";

    public KingCloudGroupAddWindow()
    {
        InitializeComponent();
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AccessKeyId))
        {
            MessageBox.Show("请输入 AccessKeyId。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
            AccessKeyIdBox.Focus();
            return;
        }
        if (string.IsNullOrWhiteSpace(AccessKeySecret))
        {
            MessageBox.Show("请输入 AccessKeySecret。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
            AccessKeySecretBox.Focus();
            return;
        }
        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
