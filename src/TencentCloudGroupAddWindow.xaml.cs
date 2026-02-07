using System.Windows;
using System.Windows.Controls;

namespace xOpenTerm;

/// <summary>新增腾讯云组：输入组名与腾讯云 API 密钥。</summary>
public partial class TencentCloudGroupAddWindow : Window
{
    public string GroupName => NameBox?.Text?.Trim() ?? "腾讯云";
    public string SecretId => SecretIdBox?.Text?.Trim() ?? "";
    public string SecretKey => SecretKeyBox?.Password ?? "";

    public TencentCloudGroupAddWindow()
    {
        InitializeComponent();
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SecretId))
        {
            MessageBox.Show("请输入 SecretId。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
            SecretIdBox.Focus();
            return;
        }
        if (string.IsNullOrWhiteSpace(SecretKey))
        {
            MessageBox.Show("请输入 SecretKey。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
            SecretKeyBox.Focus();
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
