using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using XieBox.Contracts;

namespace XieBox.Services;
public sealed class ApiException(string message, HttpStatusCode status) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}
public sealed class PlatformApi(Uri baseUri) : IDisposable
{
    private readonly HttpClient _http = new() { BaseAddress = Validate(baseUri), Timeout = TimeSpan.FromSeconds(25) };
    private static Uri Validate(Uri uri) => uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback ? uri : throw new InvalidOperationException("后台地址必须使用 HTTPS，本地开发可以使用回环地址。");
    public async Task<T> SendAsync<T>(string path, HttpMethod method, object? body = null, string? token = null, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, "api/v1/" + path);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            ApiMessage? error = null;
            try { error = await response.Content.ReadFromJsonAsync<ApiMessage>(cancellationToken); } catch (System.Text.Json.JsonException) { }
            throw new ApiException(error?.Message ?? response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "登录未成功或已失效，请检查用户名和密码。",
                HttpStatusCode.Forbidden => "当前账号没有这项权限。",
                HttpStatusCode.TooManyRequests => "操作有些频繁，请稍后再试。",
                _ => "服务暂时不可用，请稍后再试。"
            }, response.StatusCode);
        }
        if (response.StatusCode == HttpStatusCode.NoContent) return default!;
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken) ?? throw new ApiException("服务返回了空内容。", response.StatusCode);
    }
    public void Dispose() => _http.Dispose();
}
