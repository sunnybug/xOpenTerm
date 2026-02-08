using System.Windows;
using System.Windows.Controls;

namespace xOpenTerm;

/// <summary>新增阿里云组：输入组名与阿里云 API 密钥。</summary>
public partial class AliCloudGroupAddWindow : Window
{
    public string GroupName => NameBox?.Text?.Trim() ?? "阿里云";
    public string AccessKeyId => AccessKeyIdBox?.Text?.Trim() ?? "";
    public string AccessKeySecret => AccessKeySecretBox?.Password ?? "";

    public AliCloudGroupAddWindow()
    {
        InitializeComponent();
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AccessKeyId))
        {
            MessageBox.Show("请输入 AccessKey ID。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
            AccessKeyIdBox.Focus();
            return;
        }
        if (string.IsNullOrWhiteSpace(AccessKeySecret))
        {
            MessageBox.Show("请输入 AccessKey Secret。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
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
