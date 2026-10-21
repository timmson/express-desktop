using ExpressApp.Auth;
using ExpressApp.Auth.Qr;
using ExpressApp.Auth.Services;
using Microsoft.Extensions.Logging;

namespace ExpressApp;

public partial class MainPage : ContentPage
{
    private readonly QrAuthService _authService;
    private readonly ILogger<MainPage> _logger;
    private CancellationTokenSource? _pollCts;

    public MainPage(QrAuthService authService, ILogger<MainPage> logger)
    {
        InitializeComponent();

        _authService = authService;
        _logger = logger;

        // Запуск проверки сессии при старте страницы
        _ = CheckSessionOnLoadAsync();
    }

    private async Task CheckSessionOnLoadAsync()
    {
        try
        {
            StatusLabel.Text = "Проверка сессии...";
            await _authService.CheckSessionAsync();
            UpdateUi();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при проверке сессии");
            StatusLabel.Text = "Ошибка инициализации";
            StatusLabel.TextColor = Colors.Red;
        }
    }

    private async void OnStartQrClicked(object sender, EventArgs e)
    {
        // Отменяем предыдущий процесс
        _pollCts?.Cancel();
        _pollCts = null;

        StartQrButton.IsEnabled = false;
        ResetButton.IsEnabled = false;
        StatusLabel.Text = "Запрос QR-кода...";
        StatusLabel.TextColor = Colors.Blue;

        try
        {
            await _authService.StartQrAuthAsync();
            UpdateUi();

            // Если статус WaitingForScan — показываем ASCII-превью QR
            if (_authService.Status == AuthStatus.WaitingForScan && _authService.QrData is { } qrData)
            {
                ShowQrPreview(qrData.Payload);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при запросе QR-кода");
            StatusLabel.Text = "Ошибка запроса QR";
            StatusLabel.TextColor = Colors.Red;
        }
        finally
        {
            StartQrButton.IsEnabled = true;
            ResetButton.IsEnabled = true;
        }
    }

    private void OnResetClicked(object sender, EventArgs e)
    {
        _authService.Reset();
        UpdateUi();
        StatusLabel.Text = "Авторизация сброшена";
        StatusLabel.TextColor = Colors.Gray;
    }

    private void UpdateUi()
    {
        QrDataBorder.IsVisible = false;
        QrPreviewBorder.IsVisible = false;

        // Обновляем статус
        StatusLabel.Text = _authService.Status switch
        {
            AuthStatus.Unknown => "Неизвестно. Нажмите «Запросить QR-код».",
            AuthStatus.Initializing => "Инициализация...",
            AuthStatus.Registering => "Регистрация клиента...",
            AuthStatus.RequestingQr => "Запрос QR-кода...",
            AuthStatus.WaitingForScan => "Ожидание сканирования QR-кода...",
            AuthStatus.Authorized => "Авторизован",
            AuthStatus.Error => "Ошибка авторизации",
            AuthStatus.LoggedOut => "Вы вышли из системы",
            _ => "Неизвестный статус"
        };

        StatusLabel.TextColor = _authService.Status switch
        {
            AuthStatus.WaitingForScan => Colors.Green,
            AuthStatus.Authorized => Colors.Green,
            AuthStatus.Error => Colors.Red,
            _ => Colors.Blue
        };

        // Показываем данные QR, если есть
        if (_authService.QrData is { } qrData)
        {
            QrDataBorder.IsVisible = true;
            QrPayloadLabel.Text = $"Payload: {qrData.Payload}";
            QrRequestIdLabel.Text = $"Request ID: {qrData.RequestId}";
            QrExpiresLabel.Text = $"Expires: {qrData.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "не установлен"}";
        }

        // Показываем ошибку, если есть
        if (_authService.Error is { } error)
        {
            StatusLabel.Text = $"Ошибка: {error.Message}";
            StatusLabel.TextColor = Colors.Red;
        }
    }

    private void ShowQrPreview(string payload)
    {
        var preview = QrCodeRenderer.ToConsolePreview(payload);
        QrPreviewLabel.Text = preview;
        QrPreviewBorder.IsVisible = true;
        _logger.LogInformation("ASCII-превью QR-кода ({Lines} строк):", preview.Split('\n').Length);
    }
}
