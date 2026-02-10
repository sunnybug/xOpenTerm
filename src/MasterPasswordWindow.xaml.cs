using System.Windows;
using System.Windows.Controls;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>
/// 主密码对话框：三种模式。
/// 设置模式：输入主密码 + 确认，确定后通过 <see cref="ResultPassword"/> 返回密码（由调用方派生密钥并保存盐与验证码）。
/// 输入模式：仅输入主密码，确定时在本窗口内校验并设置 <see cref="SecretService"/> 会话密钥，不返回密码。
/// 验证模式：仅校验主密码（如清除主密码前确认身份），校验通过返回 DialogResult true，不设置会话密钥。
/// </summary>
public partial class MasterPasswordWindow : Window
{
    private readonly bool _isSetMode;
    private readonly bool _verifyOnly;
    private readonly byte[]? _salt;
    private readonly byte[]? _verifier;

    /// <summary>设置模式下确定时返回的主密码（仅用于派生密钥并保存，调用方使用后应清除）。</summary>
    public string? ResultPassword { get; private set; }

    /// <summary>用户是否点击了「不再提醒」：设置模式下表示不再询问主密码并继续运行；输入模式下等同取消。</summary>
    public bool DontRemindAgain { get; private set; }

    /// <param name="isSetMode">true=设置主密码（需确认），false=输入主密码（校验并设置会话密钥）或验证模式（仅校验）</param>
    /// <param name="salt">输入/验证模式下的盐（Base64 解码后的字节）</param>
    /// <param name="verifier">输入/验证模式下的验证码（Base64 解码后的字节）</param>
    /// <param name="verifyOnly">为 true 时仅校验主密码（如清除前确认），不设置会话密钥、不保存密钥文件；仅当 isSetMode 为 false 时有效。</param>
    public MasterPasswordWindow(bool isSetMode, byte[]? salt, byte[]? verifier, bool verifyOnly = false)
    {
        InitializeComponent();
        _isSetMode = isSetMode;
        _verifyOnly = verifyOnly;
        _salt = salt;
        _verifier = verifier;

        if (_isSetMode)
        {
            Title = "设置主密码";
            TitleText.Text = "设置主密码";
            HintText.Text = "主密码将用于加密配置中的密码与密钥。请牢记主密码，忘记将无法解密已有配置。";
            ConfirmPanel.Visibility = Visibility.Visible;
            DontRemindBtn.Visibility = Visibility.Visible;
        }
        else if (_verifyOnly)
        {
            Title = "确认主密码";
            TitleText.Text = "确认主密码以清除";
            HintText.Text = "请输入当前主密码以确认清除操作。";
            ConfirmPanel.Visibility = Visibility.Collapsed;
            DontRemindBtn.Visibility = Visibility.Collapsed;
        }
        else
        {
            Title = "请输入主密码";
            TitleText.Text = "请输入主密码";
            HintText.Text = "请输入您设置的主密码以解密配置。";
            ConfirmPanel.Visibility = Visibility.Collapsed;
            DontRemindBtn.Visibility = Visibility.Visible;
        }
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        var password = PasswordBox.Password ?? "";

        if (_isSetMode)
        {
            var confirm = ConfirmBox.Password ?? "";
            if (password.Length < 6)
            {
                MessageBox.Show("主密码至少需要 6 个字符。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (password != confirm)
            {
                MessageBox.Show("两次输入的主密码不一致，请重新输入。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ResultPassword = password;
            DialogResult = true;
            Close();
            return;
        }

        // 输入模式或验证模式：校验主密码
        if (_salt == null || _verifier == null || !MasterPasswordService.VerifyPassword(password, _salt, _verifier))
        {
            MessageBox.Show("主密码错误，请重试。", "xOpenTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        // 验证模式：仅校验通过即可，不设置会话密钥、不保存文件
        if (_verifyOnly)
        {
            DialogResult = true;
            Close();
            return;
        }
        // 输入模式：设置会话密钥并保存到本地文件以便下次启动无需输入
        var key = MasterPasswordService.DeriveKey(password, _salt);
        SecretService.SetSessionMasterKey(key);
        MasterPasswordService.SaveKeyToFile(key);
        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void DontRemindBtn_Click(object sender, RoutedEventArgs e)
    {
        DontRemindAgain = true;
        DialogResult = false;
        Close();
    }
}
