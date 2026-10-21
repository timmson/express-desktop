using System.Text;
using ExpressApp.Auth.Models;
using ExpressApp.Auth.Qr;
using Microsoft.Extensions.Logging;

namespace ExpressApp.Auth.Services;

/// <summary>
/// Главный сервис QR-авторизации. Управляет жизненным циклом:
/// регистрация → запрос QR → ожидание подтверждения.
/// </summary>
public interface IAuthService
{
    /// <summary>Текущий статус авторизации.</summary>
    AuthStatus Status { get; }

    /// <summary>Данные для отображения QR-кода (null если не в процессе).</summary>
    QrData? QrData { get; }

    /// <summary>Ошибка (если возникла).</summary>
    Exception? Error { get; }

    /// <summary>
    /// Запуск процесса QR-авторизации: регистрация + запрос кода.
    /// </summary>
    Task StartQrAuthAsync();

    /// <summary>
    /// Проверка сохранённой сессии. Если токен жив — пропускаем экран QR.
    /// </summary>
    Task CheckSessionAsync();

    /// <summary>Сбросить все данные авторизации.</summary>
    void Reset();
}

/// <summary>Статус авторизации.</summary>
public enum AuthStatus
{
    Unknown,
    Initializing,
    Registering,
    RequestingQr,
    WaitingForScan,
    Authorized,
    Error,
    LoggedOut
}

/// <summary>Данные, подлежащие кодированию в QR.</summary>
public record QrData(
    string Payload,
    string RequestId,
    DateTime? ExpiresAt,
    string? Status);

public sealed class QrAuthService : IAuthService
{
    private readonly IClientCrypto _crypto;
    private readonly IAuthApiService _api;
    private readonly IDeviceInformation _deviceInfo;
    private readonly IQrCodeRenderer _qrRenderer;
    private readonly ILogger<QrAuthService> _logger;
    private AuthStatus _status = AuthStatus.Unknown;
    private QrData? _qrData;
    private Exception? _error;

    public AuthStatus Status => _status;
    public QrData? QrData => _qrData;
    public Exception? Error => _error;

    public QrAuthService(
        IClientCrypto crypto,
        IAuthApiService api,
        IDeviceInformation deviceInfo,
        IQrCodeRenderer qrRenderer,
        ILogger<QrAuthService> logger)
    {
        _crypto = crypto;
        _api = api;
        _deviceInfo = deviceInfo;
        _qrRenderer = qrRenderer;
        _logger = logger;
    }

    public async Task StartQrAuthAsync()
    {
        try
        {
            _status = AuthStatus.Registering;
            _logger.LogInformation("Регистрация клиента");

            var registerRequest = CreateRegisterRequest();
            var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(registerRequest, JsonOptions.Default);
            var digest = _crypto.ComputeDigest(bodyBytes);
            var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var nonce = _crypto.NewNonce();
            var signature = _crypto.BuildSignatureHeader(
                _crypto.Udid,
                "post /api/v1/authentication/register",
                created,
                nonce,
                digest);

            var registrationId = Guid.NewGuid().ToString();    
            var registrationToken = Guid.NewGuid().ToString();
            var qrRequest = CreateQrAuthRequest(registrationId, registrationToken);
            var qrBodyBytes = JsonSerializer.SerializeToUtf8Bytes(qrRequest, JsonOptions.Default);
            var qrDigest = _crypto.ComputeDigest(qrBodyBytes);

            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            nonce = _crypto.NewNonce();
            signature = _crypto.BuildSignatureHeader(
                _crypto.Udid,
                "post /api/v1/authentication/qr/mobile_to_web/request",
                created,
                nonce,
                qrDigest);

            var qrResponse = await _api.RequestQrAsync(
                AuthApiService.QrRequestPath,
                qrBodyBytes,
                new SignedRequestHeaders(signature, nonce, qrDigest));

            _logger.LogInformation(
                "QR-код запрошен: requestId={RequestId} status={Status}",
                qrResponse.QrRequestId,
                qrResponse.Status);

            _qrData = new QrData(
                qrResponse.Payload,
                qrResponse.QrRequestId,
                qrResponse.ExpiresAt,
                qrResponse.Status);

            _status = AuthStatus.WaitingForScan;
            _logger.LogInformation(
                "Ожидание сканирования QR-кода. Payload: {Payload}",
                qrResponse.Payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при запросе QR-кода");
            _error = ex;
            _status = AuthStatus.Error;
        }
    }

    public Task CheckSessionAsync()
    {
        _logger.LogInformation("Проверка текущей сессии");
        // TODO: если есть сохранённый токен, проверяем через /token/active
        _status = AuthStatus.LoggedOut;
        return Task.CompletedTask;
    }

    public void Reset()
    {
        _status = AuthStatus.Unknown;
        _qrData = null;
        _error = null;
        _logger.LogInformation("Данные авторизации очищены");
    }

    private RegisterRequest CreateRegisterRequest()
    {
        var request = new RegisterRequest
        {
            Udid = _crypto.Udid,
            SignPubKey = _crypto.PublicKeyBase64
        };
        return _deviceInfo.Apply(request);
    }

    private QrAuthRequest CreateQrAuthRequest(string registrationId, string registrationToken)
    {
        var request = new QrAuthRequest
        {
            RegistrationId = registrationId,
            RegistrationToken = registrationToken,
            SignPubKey = _crypto.PublicKeyBase64,
            Udid = _crypto.Udid
        };
        return _deviceInfo.Apply(request);
    }
}
