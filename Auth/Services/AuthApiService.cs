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
    Task<RegisterResponse> RegisterAsync(
        string path, byte[] body, SignedRequestHeaders auth, CancellationToken cancellationToken = default);

    Task<QrAuthResponse> RequestQrAsync(
        string path, byte[] body, SignedRequestHeaders auth, CancellationToken cancellationToken = default);

    Task<TokenActiveResponse?> CheckTokenActiveAsync(
        string accessToken, SignedRequestHeaders auth, CancellationToken cancellationToken = default);
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

    public Task<RegisterResponse> RegisterAsync(
        string path, byte[] body, SignedRequestHeaders auth, CancellationToken cancellationToken = default)
        => SendAsync<RegisterResponse>(HttpMethod.Post, path, body, auth, cancellationToken);

    public Task<QrAuthResponse> RequestQrAsync(
        string path, byte[] body, SignedRequestHeaders auth, CancellationToken cancellationToken = default)
        => SendWrappedAsync<QrAuthResponse>(HttpMethod.Post, path, body, auth, cancellationToken);

    public async Task<TokenActiveResponse?> CheckTokenActiveAsync(
        string accessToken, SignedRequestHeaders auth, CancellationToken cancellationToken = default)
    {
        using var response = await SendRawAsync(
            HttpMethod.Get, TokenActivePath, null, auth, accessToken, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return null;
        }

        return await ReadJsonAsync<TokenActiveResponse>(response);
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method, string path, byte[]? body, SignedRequestHeaders auth, CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(method, path, body, auth, null, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await ReadJsonAsync<T>(response);
        return result ?? throw new InvalidOperationException($"Пустой ответ от {path}");
    }

    /// <summary>
    /// То же, что <see cref="SendAsync{T}"/>, но дополнительно разбирает ответ
    /// в обёртке <c>{ "status": ..., "result": { ... } }</c>.
    /// Если обёртки нет, данные читаются из корня — так метод остаётся
    /// рабочим при любом варианте формата ответа.
    /// </summary>
    private async Task<T> SendWrappedAsync<T>(
        HttpMethod method, string path, byte[]? body, SignedRequestHeaders auth, CancellationToken cancellationToken)
        where T : class
    {
        using var response = await SendRawAsync(method, path, body, auth, null, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException($"Пустой ответ от {path}");
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        JsonElement source = root;
        var wrapped = false;

        // corp.express кладёт полезную нагрузку в result; status остаётся на уровне обёртки.
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("result", out var result)
            && result.ValueKind == JsonValueKind.Object)
        {
            source = result;
            wrapped = true;
        }

        var payload = source.Deserialize<T>(JsonOptions.Default)
                     ?? throw new InvalidOperationException($"Не удалось разобрать ответ от {path}");

        // status в обёртке относится к запросу, а не к объекту — если в result его нет,
        // берём верхнеуровневый, чтобы UI продолжал получать значение.
        if (wrapped && root.TryGetProperty("status", out var envelopeStatus)
            && envelopeStatus.ValueKind == JsonValueKind.String)
        {
            ApplyEnvelopeStatus(payload, envelopeStatus.GetString());
        }

        return payload;
    }

    private static void ApplyEnvelopeStatus<T>(T payload, string? status)
    {
        if (payload is QrAuthResponse qr && string.IsNullOrEmpty(qr.Status))
        {
            qr.Status = status;
        }
    }

    private async Task<HttpResponseMessage> SendRawAsync(
        HttpMethod method,
        string path,
        byte[]? body,
        SignedRequestHeaders auth,
        string? accessToken,
        CancellationToken cancellationToken)
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
            // Токен обязателен: без него отменённый цикл ждёт ответ сервера и
            // «оживает» спустя минуты, перетирая состояние нового запуска.
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Штатная отмена (сброс или повторный запуск), а не сетевой сбой.
            _logger.LogInformation(
                "HTTP {Method} {Path} отменён после {Elapsed}ms",
                method.Method, path, stopWatch.ElapsedMilliseconds);
            throw;
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
