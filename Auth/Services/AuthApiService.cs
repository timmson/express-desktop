using ExpressApp.Auth.Models;
using Microsoft.Extensions.Logging;

namespace ExpressApp.Auth.Services;

/// <summary>
/// HTTP-клиент методов QR-авторизации corp.express.
/// Все запросы подписываются (signature/nonce/digest); тело сериализуется
/// вызывающим кодом, чтобы digest совпадал с фактическими байтами запроса.
/// Каждый сетевой вызов логируется.
/// </summary>
public interface IAuthApiService
{
    Task<RegisterResponse> RegisterAsync(string path, byte[] body, SignedRequestHeaders auth);

    Task<QrAuthResponse> RequestQrAsync(string path, byte[] body, SignedRequestHeaders auth);

    Task<TokenActiveResponse?> CheckTokenActiveAsync(string accessToken, SignedRequestHeaders auth);
}

/// <summary>
/// Готовые значения заголовков подписи для одного запроса.
/// </summary>
public readonly record struct SignedRequestHeaders(string Signature, string Nonce, string Digest);

public sealed class AuthApiService : IAuthApiService
{
    public const string RegisterPath = "/api/v1/authentication/register";
    public const string QrRequestPath = "/api/v1/authentication/qr/mobile_to_web/request";
    public const string TokenActivePath = "/api/v1/authentication/token/active";

    private readonly HttpClient _http;
    private readonly ILogger<AuthApiService> _logger;

    public AuthApiService(HttpClient http, ILogger<AuthApiService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public Task<RegisterResponse> RegisterAsync(string path, byte[] body, SignedRequestHeaders auth)
        => SendAsync<RegisterResponse>(HttpMethod.Post, path, body, auth);

    public Task<QrAuthResponse> RequestQrAsync(string path, byte[] body, SignedRequestHeaders auth)
        => SendAsync<QrAuthResponse>(HttpMethod.Post, path, body, auth);

    public async Task<TokenActiveResponse?> CheckTokenActiveAsync(string accessToken, SignedRequestHeaders auth)
    {
        using var response = await SendRawAsync(HttpMethod.Get, TokenActivePath, null, auth, accessToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return null;
        }

        return await ReadJsonAsync<TokenActiveResponse>(response);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, byte[]? body, SignedRequestHeaders auth)
    {
        using var response = await SendRawAsync(method, path, body, auth, null);
        response.EnsureSuccessStatusCode();
        var result = await ReadJsonAsync<T>(response);
        return result ?? throw new InvalidOperationException($"Пустой ответ от {path}");
    }

    private async Task<HttpResponseMessage> SendRawAsync(
        HttpMethod method,
        string path,
        byte[]? body,
        SignedRequestHeaders auth,
        string? accessToken)
    {
        using var request = new HttpRequestMessage(method, path);

        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        }

        if (!string.IsNullOrEmpty(auth.Signature))
        {
            request.Headers.TryAddWithoutValidation("signature", auth.Signature);
            request.Headers.TryAddWithoutValidation("express-request-nonce", auth.Nonce);
            request.Headers.TryAddWithoutValidation("digest", auth.Digest);
        }

        if (!string.IsNullOrEmpty(accessToken))
        {
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        }

        LogRequest(request, body);

        var stopWatch = System.Diagnostics.Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            // Long-polling на mobile_to_web/request: соединение держится до
            // сканирования кода, поэтому общий timeout HttpClient снят (см. MauiProgram).
            response = await _http.SendAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "HTTP {Method} {Path} failed after {Elapsed}ms: {Error}",
                method.Method, path, stopWatch.ElapsedMilliseconds, ex.Message);
            throw;
        }

        stopWatch.Stop();
        await LogResponseAsync(response, stopWatch.ElapsedMilliseconds);
        return response;
    }

    private void LogRequest(HttpRequestMessage request, byte[]? body)
    {
        _logger.LogInformation("→ HTTP {Method} {Uri}", request.Method, request.RequestUri);

        foreach (var header in request.Headers)
        {
            _logger.LogDebug("   header {Name}: {Value}", header.Key, string.Join(", ", header.Value));
        }

        if (body is not null)
        {
            _logger.LogDebug("   body ({Length} bytes): {Body}",
                body.Length, System.Text.Encoding.UTF8.GetString(body));
        }
    }

    private async Task LogResponseAsync(HttpResponseMessage response, long elapsedMs)
    {
        var bodyText = await response.Content.ReadAsStringAsync();
        _logger.LogInformation("← HTTP {Status} {Uri} ({Elapsed}ms)",
            (int)response.StatusCode, response.RequestMessage?.RequestUri, elapsedMs);

        if (!string.IsNullOrEmpty(bodyText))
        {
            _logger.LogDebug("   response body: {Body}", bodyText);
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions.Default);
    }
}
