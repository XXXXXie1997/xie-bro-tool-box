using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using XieBox.Services;

namespace XieBox.Views;
public partial class AccountWindow : XieBox.Platform.UI.DialogWindow
{
    private readonly AccountService _accounts;
    private bool _busy;
    private bool _closed;
    private readonly CancellationTokenSource _lifetime = new();
    private string _mode = "login";
    private string? _challengeId;
    private DateTime _nextSendAt;
    private readonly System.Windows.Threading.DispatcherTimer _countdown = new() { Interval = TimeSpan.FromSeconds(1) };
    public AccountWindow(AccountService accounts, PlatformApi api)
    {
        InitializeComponent(); _accounts = accounts; FitContent();
        _countdown.Tick += (_, _) => UpdateSendButton();
        _countdown.Start(); UpdateIdentity();
    }
    private void UpdateIdentity()
    {
        var showAccount = _accounts.Current.IsAuthenticated && _mode != "reset";
        ModeTitle.Text = showAccount ? "我的账号" : _mode == "login" ? "登录 / 注册" : _mode == "register" ? "验证邮箱" : "找回密码";
        SignedInPanel.Visibility = showAccount ? Visibility.Visible : Visibility.Collapsed;
        FormPanel.Visibility = showAccount ? Visibility.Collapsed : Visibility.Visible;
        CurrentUsername.Text = _accounts.Current.Email ?? _accounts.Current.Username;
        PublicUid.Text = _accounts.Current.PublicId is { Length: 8 } uid ? "UID  " + uid : "UID 将在账号服务更新后显示";
        AccountAutoLogin.IsChecked = _accounts.AutoLogin;
        SessionHint.Text = !_accounts.AutoLogin ? "退出应用后需要重新登录。"
            : _accounts.SessionExpiresAt is { } expiry ? $"本机保持登录至 {expiry.ToLocalTime():MM-dd HH:mm}"
            : "仅在这台电脑上保持登录";
        Username.IsReadOnly = _mode == "register" || _accounts.Current.IsAuthenticated && _mode == "reset";
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (_busy || _closed) return;
        _busy = true; SetInputsEnabled(false); Feedback.Text = "";
        try { await action(); }
        catch (Exception e) when (e is ApiException or HttpRequestException or OperationCanceledException or IOException or CryptographicException)
        { if (!_closed) Feedback.Text = e is ApiException ? e.Message : "暂时无法完成操作，请检查网络、后台服务和本地存储权限。"; }
        finally { _busy = false; if (!_closed) SetInputsEnabled(true); }
    }
    private void SetInputsEnabled(bool enabled)
    {
        Submit.IsEnabled = SignOut.IsEnabled = Username.IsEnabled = Password.IsEnabled = Code.IsEnabled = SwitchMode.IsEnabled = ForgotPassword.IsEnabled = ChangePassword.IsEnabled = AccountAutoLogin.IsEnabled = LoginAutoLogin.IsEnabled = enabled;
        Password.IsEnabled = enabled && _mode != "register";
        UpdateSendButton();
    }
    private void UpdateSendButton()
    {
        var seconds = Math.Max(0, (int)Math.Ceiling((_nextSendAt - DateTime.UtcNow).TotalSeconds));
        SendCode.IsEnabled = !_busy && seconds == 0;
        SendCode.Content = seconds > 0 ? $"{seconds} 秒后重发" : "发送验证码";
    }
    private void SetMode(string mode)
    {
        _mode = mode; _challengeId = null; Code.Clear(); Password.Clear(); Feedback.Text = "";
        ModeTitle.Text = mode == "login" ? "登录 / 注册" : mode == "register" ? "验证邮箱" : "找回密码";
        IdentityLabel.Text = mode == "login" ? "邮箱或已有账号" : "邮箱";
        PasswordLabel.Text = mode == "reset" ? "新密码 · 至少 6 位" : "密码 · 至少 6 位";
        CodePanel.Visibility = mode == "login" ? Visibility.Collapsed : Visibility.Visible;
        Submit.Content = mode == "login" ? "继续" : mode == "register" ? "验证并注册" : "重置密码";
        SwitchMode.Content = mode == "register" ? "更换邮箱" : "返回";
        SwitchMode.Visibility = mode == "login" ? Visibility.Collapsed : Visibility.Visible;
        ForgotPassword.Visibility = mode == "login" ? Visibility.Visible : Visibility.Collapsed;
        LoginAutoLogin.Visibility = mode == "reset" ? Visibility.Collapsed : Visibility.Visible;
        UpdateIdentity();
    }
    private void SwitchMode_Click(object sender, RoutedEventArgs e) => SetMode("login");
    private void ForgotPassword_Click(object sender, RoutedEventArgs e) => SetMode("reset");
    private void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        Username.Text = _accounts.Current.Email ?? "";
        SetMode("reset");
    }
    private async void AutoLogin_Click(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        _accounts.SetAutoLogin(AccountAutoLogin.IsChecked == true);
        UpdateIdentity();
        return Task.CompletedTask;
    });
    private void IdentityChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    { _challengeId = null; Code?.Clear(); }
    private void PasswordChanged(object sender, RoutedEventArgs e)
    { if (_mode == "register") { _challengeId = null; Code?.Clear(); } }
    private async void SendCode_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(Username.Text) || !Username.Text.Contains('@'))
        { Feedback.Text = "请输入有效邮箱地址。"; return; }
        if (_mode == "register" && Password.Password.Length < 6)
        { Feedback.Text = "请先设置至少 6 位密码。"; return; }
        var response = _mode == "register"
            ? await _accounts.RegisterAsync(Username.Text.Trim(), Password.Password, _lifetime.Token)
            : await _accounts.ForgotPasswordAsync(Username.Text.Trim(), _lifetime.Token);
        if (_closed) return;
        _challengeId = response.ChallengeId;
        _nextSendAt = DateTime.UtcNow.AddSeconds(response.RetryAfterSeconds);
        Feedback.Text = response.Message; Code.Focus();
    });
    private async void Submit_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(Username.Text) || Password.Password.Length < 6)
        { Feedback.Text = "请输入账号和至少 6 位密码。"; return; }
        if (_mode != "reset") _accounts.SetAutoLogin(LoginAutoLogin.IsChecked == true);
        if (_mode == "login")
        {
            var response = await _accounts.ContinueAsync(Username.Text.Trim(), Password.Password, _lifetime.Token);
            if (_closed) return;
            if (response.Step == "verify_email" && response.Challenge is { } challenge)
            {
                var password = Password.Password;
                SetMode("register");
                Password.Password = password;
                _challengeId = challenge.ChallengeId;
                _nextSendAt = DateTime.UtcNow.AddSeconds(challenge.RetryAfterSeconds);
                Feedback.Text = "验证码已发送，请查收邮箱。";
                Code.Focus();
                return;
            }
        }
        else
        {
            if (_challengeId is null || Code.Text.Length != 6)
            { Feedback.Text = "请先获取并填写 6 位邮箱验证码。"; return; }
            if (_mode == "register") await _accounts.VerifyRegistrationAsync(_challengeId, Code.Text, _lifetime.Token);
            else
            {
                await _accounts.ResetPasswordAsync(_challengeId, Code.Text, Password.Password, _lifetime.Token);
                if (_accounts.Current.IsAuthenticated)
                {
                    try { await _accounts.SignOutAsync(); }
                    catch (Exception error) when (error is ApiException or HttpRequestException or OperationCanceledException) { /* Reset already revoked every session. */ }
                }
                if (!_closed) { SetMode("login"); Feedback.Text = "密码已重置，请使用新密码登录。"; }
                return;
            }
        }
        if (!_closed) { Password.Clear(); Close(); }
    });
    private async void SignOut_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        try { await _accounts.SignOutAsync(); } finally { if (!_closed) UpdateIdentity(); }
        if (!_closed) Feedback.Text = "已退出，免费工具仍可使用。";
    });

    private void Window_Closed(object? sender, EventArgs e) { _closed = true; _countdown.Stop(); _lifetime.Cancel(); _lifetime.Dispose(); Password.Clear(); Code.Clear(); }
}
