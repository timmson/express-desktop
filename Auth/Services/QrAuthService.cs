using ExpressApp.Auth.Models;
using ExpressApp.Auth.Qr;
using ExpressApp.Settings;
using Microsoft.Extensions.Logging;

namespace ExpressApp.Auth.Services;

/// <summary>
/// Главный сервис QR-авторизации. Управляет жизненным циклом:
/// генерация payload → показ QR → ожидание подтверждения (long-polling).
/// </summary>
public interface IAuthService
{
    /// <summary>Текущий статус авторизации.</summary>
    AuthStatus Status { get; }

    /// <summary>Данные для отображения QR-кода (null если не в процессе).</summary>
    QrData? QrData { get; }

    /// <summary>Ошибка (если возникла).</summary>
    Exception? Error { get; }

    /// <summary>Изменение статуса/QR/ошибки — подписка для UI.</summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Запуск процесса QR-авторизации: локальный payload + показ кода + ожидание.
    /// </summary>
    Task StartQrAuthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Проверка сохранённой сессии. Если токен жив — пропускаем экран QR.
    /// </summary>
    Task CheckSessionAsync();

    /// <summary>Отменить ожидание сканирования и очистить QR.</summary>
    void CancelQrAuth();

    /// <summary>Сбросить все данные авторизации.</summary>
    void Reset();
}

/// <summary>Статус авторизации.</summary>
public enum AuthStatus
{
    Unknown,
    Initializing,
    RequestingQr,
    WaitingForScan,
    Authorized,
    Error,
    LoggedOut
}

/// <summary>Данные, подлежащие кодированию в QR.</summary>
public record QrData(
    string Payload,
    string Svg,
    DateTime? ExpiresAt,
    string? Status);

public sealed class QrAuthService : IAuthService
{
    private readonly Func<IClientCrypto> _cryptoFactory;
    private readonly IAuthApiService _api;
    private readonly IDeviceInformation _deviceInfo;
    private readonly IQrCodeRenderer _qrRenderer;
    private readonly IAuthStateStore _store;
    private readonly ISettingsStore _settings;
    private readonly ILogger<QrAuthService> _logger;

    private AuthStatus _status = AuthStatus.Unknown;
    private QrData? _qrData;
    private Exception? _error;
    private CancellationTokenSource? _waitCts;
    private IClientCrypto? _crypto;

    public AuthStatus Status => _status;
    public QrData? QrData => _qrData;
    public Exception? Error => _error;

    public event EventHandler? StateChanged;

    public QrAuthService(
        Func<IClientCrypto> cryptoFactory,
        IAuthApiService api,
        IDeviceInformation deviceInfo,
        IQrCodeRenderer qrRenderer,
        IAuthStateStore store,
        ISettingsStore settings,
        ILogger<QrAuthService> logger)
    {
        _cryptoFactory = cryptoFactory;
        _api = api;
        _deviceInfo = deviceInfo;
        _qrRenderer = qrRenderer;
        _store = store;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Пункт 4 спецификации: QR формируется на клиенте из локальных значений,
    /// сервер его не возвращает.
    /// </summary>
    private string BuildQrPayload()
    {
        var crypto = _crypto ??= _cryptoFactory();
        var payload = new QrPayload
        {
            RegistrationId = crypto.RegistrationId,
            RegistrationToken = crypto.RegistrationToken,
            RegistrationKey = crypto.PrivateKeySeedBase64,
            Version = QrPayload.CurrentVersion
        };

        return JsonSerializer.Serialize(payload, JsonOptions.Default);
    }

    /// <summary>
    /// Фиксирует параметры текущей регистрации в ~/.express/settings.json.
    /// Делается именно при генерации QR: с этого момента кодом может воспользоваться
    /// мобильное приложение, и значения должны переживать перезапуск десктопа.
    /// </summary>
    private void PersistRegistrationSettings()
    {
        try
        {
            var crypto = _crypto ??= _cryptoFactory();
            _settings.Update(settings =>
            {
                var registration = settings.EnsureRegistration();
                registration.GeneratedAt = DateTimeOffset.UtcNow;
                registration.RegistrationId = crypto.RegistrationId;
                registration.RegistrationToken = crypto.RegistrationToken;
                registration.RegistrationKey = crypto.PrivateKeySeedBase64;
            });

            _logger.LogInformation("Параметры регистрации сохранены в {FilePath}", _settings.FilePath);
        }
        catch (Exception ex)
        {
            // Настройки — удобная копия состояния, а не условие входа:
            // сбой записи не должен срывать авторизацию.
            _logger.LogError(ex, "Не удалось сохранить параметры регистрации в {FilePath}", _settings.FilePath);
        }
    }

    /// <summary>
    /// Фиксирует в ~/.express/settings.json ключи сессии, полученные от сервера
    /// после подтверждения QR. Разные от локальных параметров регистрации: их
    /// невозможно восстановить без ответа сервера, поэтому они и сохраняются.
    /// </summary>
    private void PersistSessionKeys(QrAuthResponse response)
    {
        try
        {
            _settings.Update(settings =>
            {
                var registration = settings.EnsureRegistration();
                registration.RegistrationData = response.RegistrationData;
                registration.CtsRegistrationToken = response.CtsRegistrationToken;
                registration.RtsRegistrationToken = response.RtsRegistrationToken;

                // Сервер может вернуть подтверждённый registration_id — он и есть
                // итоговый, локальный мог быть черновиком.
                if (!string.IsNullOrEmpty(response.RegistrationId))
                {
                    registration.RegistrationId = response.RegistrationId;
                }
            });

            _logger.LogInformation("Ключи сессии сохранены в {FilePath}", _settings.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось сохранить ключи сессии в {FilePath}", _settings.FilePath);
        }
    }

    public Task StartQrAuthAsync(CancellationToken cancellationToken = default)
    {
        CancelQrAuth();
        _waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _waitCts.Token;

        try
        {
            _error = null;

            var payload = BuildQrPayload();
            var svg = _qrRenderer.Render(payload);

            PersistRegistrationSettings();

            _logger.LogInformation(
                "QR payload сформирован локально: {Modules}×{Modules} модулей (QR v{QrVersion}, payload v{PayloadVersion})",
                svg.Modules, svg.Modules, svg.Version, QrPayload.CurrentVersion);

            _qrData = new QrData(payload, svg.Markup, ExpiresAt: null, Status: null);
            SetStatus(AuthStatus.WaitingForScan);

            // Пункт 5: long-polling — соединение держится до сканирования кода.
            // Ответ сервера нужен только как подтверждение, содержимое QR не меняет.
            _ = Task.Run(() => WaitForConfirmationAsync(token), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при подготовке QR-кода");
            _error = ex;
            SetStatus(AuthStatus.Error);
        }

        return Task.CompletedTask;
    }

    private async Task WaitForConfirmationAsync(CancellationToken token)
    {
        try
        {
            SetStatus(AuthStatus.RequestingQr);

            var crypto = _crypto ??= _cryptoFactory();
            var request = CreateQrAuthRequest();
            var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions.Default);

            var digest = crypto.ComputeDigest(bodyBytes);
            var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var nonce = crypto.NewNonce();

            // keyId подписи совпадает с registration_id из тела запроса.
            var signature = crypto.BuildSignatureHeader(
                crypto.RegistrationId,
                "post " + AuthApiService.QrRequestPath,
                created,
                nonce,
                digest);

            var response = await _api.RequestQrAsync(
                AuthApiService.QrRequestPath,
                bodyBytes,
                new SignedRequestHeaders(signature, nonce, digest));

            token.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "Сервер подтвердил QR-запрос: requestId={RequestId} status={Status}",
                response.QrRequestId,
                response.Status);

            PersistSessionKeys(response);

            if (_qrData is { } current)
            {
                _qrData = current with
                {
                    ExpiresAt = response.ExpiresAt,
                    Status = response.Status
                };
            }

            SetStatus(AuthStatus.Authorized);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Ожидание сканирования отменено");
            _qrData = null;
            SetStatus(AuthStatus.LoggedOut);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при ожидании подтверждения QR-кода");
            _error = ex;
            SetStatus(AuthStatus.Error);
        }
    }

    public async Task CheckSessionAsync()
    {
        _logger.LogInformation("Проверка текущей сессии");

        var state = _store.Load();
        if (!state.HasLiveSession)
        {
            _logger.LogInformation("Сохранённой активной сессии нет — показываем экран QR- входа");
            SetStatus(AuthStatus.LoggedOut);
            return;
        }

        try
        {
            var crypto = _crypto ??= _cryptoFactory();
            var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var nonce = crypto.NewNonce();
            var digest = crypto.ComputeDigest(ReadOnlySpan<byte>.Empty);
            var signature = crypto.BuildSignatureHeader(
                crypto.RegistrationId,
                "get " + AuthApiService.TokenActivePath,
                created,
                nonce,
                digest);

            var active = await _api.CheckTokenActiveAsync(
                state.AccessToken!,
                new SignedRequestHeaders(signature, nonce, digest));

            SetStatus(active is null ? AuthStatus.LoggedOut : AuthStatus.Authorized);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка проверки сессии");
            _error = ex;
            SetStatus(AuthStatus.Error);
        }
    }

    public void CancelQrAuth()
    {
        _waitCts?.Cancel();
        _waitCts?.Dispose();
        _waitCts = null;
        _qrData = null;
    }

    public void Reset()
    {
        CancelQrAuth();
        _crypto = null;
        _store.ClearAll();
        _settings.ClearRegistration();
        _error = null;
        SetStatus(AuthStatus.Unknown);
        _logger.LogInformation("Данные авторизации очищены");
    }

    private void SetStatus(AuthStatus status)
    {
        _status = status;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private QrAuthRequest CreateQrAuthRequest()
    {
        var crypto = _crypto ??= _cryptoFactory();
        var request = new QrAuthRequest
        {
            RegistrationId = crypto.RegistrationId,
            RegistrationToken = crypto.RegistrationToken,
            SignPubKey = crypto.PublicKeyBase64,
            Udid = crypto.Udid
        };

        return _deviceInfo.Apply(request);
    }
}
