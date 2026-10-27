using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XieBox.Contracts;

namespace XieBox.Services;
public sealed class AccountService(PlatformApi api, LocalStore store)
{
    private SessionResponse? _session;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string SessionPath => Path.Combine(store.Root, "session.dat");
    public UserContext Current { get; private set; } = UserContext.Guest;
    public bool AutoLogin { get; private set; }
    public DateTimeOffset? SessionExpiresAt => _session?.SessionExpiresAt;
    public void SetAutoLogin(bool enabled)
    {
        AutoLogin = enabled;
        if (enabled && Current.IsAuthenticated) Save();
        if (!enabled) { File.Delete(SessionPath); File.Delete(SessionPath + ".tmp"); }
    }
    public event EventHandler<UserContext>? Changed;
    public async Task InitializeAsync()
    {
        try
        {
            if (!File.Exists(SessionPath)) return;
            var bytes = ProtectedData.Unprotect(await File.ReadAllBytesAsync(SessionPath), null, DataProtectionScope.CurrentUser);
            _session = JsonSerializer.Deserialize<SessionResponse>(bytes, LocalStore.Json);
            if (_session is null) return;
            if (_session.SessionExpiresAt <= DateTimeOffset.UtcNow) { Clear(); return; }
            AutoLogin = true;
            var user = await SendAuthorizedAsync<UserContext>("me", HttpMethod.Get);
            SetCurrent(user);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { /* Remain a guest offline; preserve protected credentials for a later retry. */ }
        catch (Exception e) when (e is IOException or CryptographicException or JsonException or ApiException) { Clear(); }
    }
    public async Task SignInAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var session = await api.SendAsync<SessionResponse>("auth/login", HttpMethod.Post, new Credentials(username, password), cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        AcceptSession(session);
    }
    public async Task<AuthFlowResponse> ContinueAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var response = await api.SendAsync<AuthFlowResponse>("auth/continue", HttpMethod.Post, new Credentials(username, password), cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (response.Step == "signed_in" && response.Session is not null) AcceptSession(response.Session);
        else if (response.Step != "verify_email" || response.Challenge is null)
            throw new ApiException("账号服务返回了无效结果，请重试。", HttpStatusCode.BadGateway);
        return response;
    }
    private void AcceptSession(SessionResponse session)
    {
        _session = session;
        if (AutoLogin) Save();
        SetCurrent(session.User);
    }
    public Task<EmailChallengeResponse> RegisterAsync(string email, string password, CancellationToken ct = default) =>
        api.SendAsync<EmailChallengeResponse>("auth/register", HttpMethod.Post, new RegistrationRequest(email, password), cancellationToken: ct);
    public async Task VerifyRegistrationAsync(string id, string code, CancellationToken ct = default)
    {
        var response = await api.SendAsync<AuthFlowResponse>("auth/verify", HttpMethod.Post, new VerifyEmailRequest(id, code), cancellationToken: ct);
        ct.ThrowIfCancellationRequested();
        if (response.Step != "signed_in" || response.Session is null) throw new ApiException("账号验证未完成，请重试。", HttpStatusCode.BadGateway);
        AcceptSession(response.Session);
    }
    public Task<EmailChallengeResponse> ForgotPasswordAsync(string email, CancellationToken ct = default) =>
        api.SendAsync<EmailChallengeResponse>("auth/forgot-password", HttpMethod.Post, new EmailCodeRequest(email), cancellationToken: ct);
    public Task<ApiMessage> ResetPasswordAsync(string id, string code, string password, CancellationToken ct = default) =>
        api.SendAsync<ApiMessage>("auth/reset-password", HttpMethod.Post, new ResetPasswordRequest(id, code, password), cancellationToken: ct);
    public async Task SignOutAsync()
    {
        try { if (_session is not null) await SendAuthorizedAsync<object>("auth/logout", HttpMethod.Post); }
        finally { Clear(); }
    }
    private void SetCurrent(UserContext user) { Current = user; Changed?.Invoke(this, user); }
    private void Clear()
    {
        _session = null;
        AutoLogin = false;
        try { File.Delete(SessionPath); File.Delete(SessionPath + ".tmp"); } finally { SetCurrent(UserContext.Guest); }
    }
    private void Save()
    {
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_session, LocalStore.Json)), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(SessionPath + ".tmp", bytes);
        File.Move(SessionPath + ".tmp", SessionPath, true);
    }
    public async Task<T> SendAuthorizedAsync<T>(string path, HttpMethod method, object? body = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_session is null) throw new ApiException("请先登录。", HttpStatusCode.Unauthorized);
            try { return await api.SendAsync<T>(path, method, body, _session.AccessToken, cancellationToken); }
            catch (ApiException e) when (e.Status == HttpStatusCode.Unauthorized)
            {
                try
                {
                    _session = await api.SendAsync<SessionResponse>("auth/refresh", HttpMethod.Post, new RefreshRequest(_session.RefreshToken), cancellationToken: cancellationToken);
                    if (AutoLogin) Save();
                    SetCurrent(_session.User);
                }
                catch (ApiException) { Clear(); throw; }
                return await api.SendAsync<T>(path, method, body, _session.AccessToken, cancellationToken);
            }
        }
        finally { _gate.Release(); }
    }
}
