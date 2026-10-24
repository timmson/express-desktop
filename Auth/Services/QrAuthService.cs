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
    string? Status)
{
    /// <summary>Оставшееся время валидности QR-кода в секундах (−1 если сервер ещё не сообщил срок).</summary>
    public int RemainingSeconds => ExpiresAt is { } exp
        ? Math.Max(0, (int)Math.Round((exp - DateTime.UtcNow).TotalSeconds))
        : -1;
}

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
    private CancellationTokenSource? _timerCts;
    private IClientCrypto? _crypto;

    // Счётчик запусков ожидания. Ответ long-polling приходит через минуты после
    // отмены, поэтому цикл сверяет свой номер с текущим и не трогает состояние,
    // если его уже сменил более новый запуск.
    private int _generation;

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

    /// <summary>
    /// Время жизни QR-кода в секундах. Используется для обратного отсчёта на клиенте
    /// до тех пор, пока сервер не сообщит актуальный ExpiresAt после сканирования.
    /// </summary>
    private const int QrLifetimeSeconds = 120;

    public Task StartQrAuthAsync(CancellationToken cancellationToken = default)
    {
        // Отмена заодно инвалидирует все ранее запущенные циклы ожидания.
        CancelQrAuth();
        _waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _waitCts.Token;
        var generation = _generation;
        try
        {
            _error = null;

            var payload = BuildQrPayload();
            var svg = _qrRenderer.Render(payload);
            var expiresAt = DateTime.UtcNow.AddSeconds(QrLifetimeSeconds);

            PersistRegistrationSettings();

            _logger.LogInformation(
                "QR payload сформирован локально: {Modules}×{Modules} модулей (QR v{QrVersion}, payload v{PayloadVersion})",
                svg.Modules, svg.Modules, svg.Version, QrPayload.CurrentVersion);

            _qrData = new QrData(payload, svg.Markup, ExpiresAt: expiresAt, Status: null);
            SetStatus(AuthStatus.WaitingForScan);

            // Запускаем таймер обратного отсчёта
            _timerCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _ = StartCountdownTimerAsync(_timerCts.Token);

            // Пункт 5: long-polling — соединение держится до сканирования кода.
            // Ответ сервера нужен только как подтверждение, содержимое QR не меняет.
            _ = Task.Run(() => WaitForConfirmationAsync(token, generation), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при подготовке QR-кода");
            _error = ex;
            SetStatus(AuthStatus.Error);
        }

        return Task.CompletedTask;
    }

    private async Task StartCountdownTimerAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
                await Task.Delay(1000, token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task WaitForConfirmationAsync(CancellationToken token, int generation)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                SetStatus(AuthStatus.RequestingQr, generation);

                // Каждый цикл создаём свежие ключи и payload
                _crypto = _cryptoFactory();
                var request = CreateQrAuthRequest();
                var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions.Default);

                var digest = _crypto.ComputeDigest(bodyBytes);
                var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var nonce = _crypto.NewNonce();

                var signature = _crypto.BuildSignatureHeader(
                    _crypto.RegistrationId,
                    "post " + AuthApiService.QrRequestPath,
                    created,
                    nonce,
                    digest);

                var response = await _api.RequestQrAsync(
                    AuthApiService.QrRequestPath,
                    bodyBytes,
                    new SignedRequestHeaders(signature, nonce, digest),
                    token);

                _logger.LogInformation(
                    "Сервер подтвердил QR-запрос: requestId={RequestId} status={Status}",
                    response.QrRequestId,
                    response.Status);

                // Ключи сессии пишем до проверки отмены: их выдал сервер, и
                // восстановить их без ответа невозможно, даже если экран уже закрыли.
                PersistSessionKeys(response);

                if (!IsCurrent(generation))
                {
                    _logger.LogWarning(
                        "Ответ сервера получен, но ожидание уже отменено — статус не меняем (поколение {Generation})",
                        generation);
                    break;
                }

                if (_qrData is { } current)
                {
                    _qrData = current with
                    {
                        ExpiresAt = response.ExpiresAt,
                        Status = response.Status
                    };
                }

                SetStatus(AuthStatus.Authorized, generation);
                break;
            }
            catch (OperationCanceledException)
            {
                if (!IsCurrent(generation))
                {
                    _logger.LogDebug("Устаревший цикл ожидания остановлен (поколение {Generation})", generation);
                    break;
                }

                _logger.LogInformation("Ожидание сканирования отменено");
                _qrData = null;
                SetStatus(AuthStatus.LoggedOut, generation);
                break;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
            {
                if (!IsCurrent(generation))
                {
                    break;
                }

                // 408 — запрос истёк, пересоздаём QR с новыми параметрами
                _logger.LogWarning("Сервер вернул 408 — пересоздаём QR-код с новыми параметрами");

                // Генерируем новый payload и показываем новый QR
                var payload = BuildQrPayload();
                var svg = _qrRenderer.Render(payload);
                var expiresAt = DateTime.UtcNow.AddSeconds(QrLifetimeSeconds);
                PersistRegistrationSettings();

                _logger.LogInformation(
                    "Пересоздан QR payload (повторная попытка): {Modules}×{Modules} модулей",
                    svg.Modules, svg.Modules);

                _qrData = new QrData(payload, svg.Markup, ExpiresAt: expiresAt, Status: null);
                SetStatus(AuthStatus.WaitingForScan, generation);

                // Даем UI обновиться перед следующей попыткой
                await Task.Delay(500, token);
                continue;
            }
            catch (Exception ex)
            {
                if (!IsCurrent(generation))
                {
                    _logger.LogDebug(
                        "Устаревший цикл ожидания завершён с ошибкой (поколение {Generation}): {Error}",
                        generation, ex.Message);
                    break;
                }

                _logger.LogError(ex, "Ошибка при ожидании подтверждения QR-кода");
                _error = ex;
                SetStatus(AuthStatus.Error);
                break;
            }
        }
    }

    /// <summary>True, если цикл ещё актуален: его не отменили запуском нового QR или сбросом.</summary>
    private bool IsCurrent(int generation) => generation == _generation;

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
        // Инкремент до отмены: фоновый цикл по номеру поймёт, что он устарел,
        // даже если ответ сервера придёт позже.
        _generation++;

        _timerCts?.Cancel();
        _timerCts?.Dispose();
        _timerCts = null;

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

    /// <summary>
    /// Смена статуса от имени конкретного цикла ожидания: устаревший цикл
    /// (его уже отменили) не должен переводить UI в своё состояние.
    /// </summary>
    private void SetStatus(AuthStatus status, int generation)
    {
        if (!IsCurrent(generation))
        {
            return;
        }

        SetStatus(status);
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
