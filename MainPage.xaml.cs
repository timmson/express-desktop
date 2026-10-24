using ExpressApp.Auth.Qr;
using ExpressApp.Auth.Services;
using Microsoft.Extensions.Logging;

namespace ExpressApp;

public partial class MainPage : ContentPage
{
    private readonly QrAuthService _authService;
    private readonly ILogger<MainPage> _logger;
    private readonly QrDrawable _qrDrawable = new();
    private CancellationTokenSource? _authCts;

    public MainPage(QrAuthService authService, ILogger<MainPage> logger)
    {
        InitializeComponent();

        _authService = authService;
        _logger = logger;

        QrView.Drawable = _qrDrawable;
        _authService.StateChanged += OnAuthServiceStateChanged;

        _ = CheckSessionOnLoadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _authService.StateChanged -= OnAuthServiceStateChanged;
    }

    private async Task CheckSessionOnLoadAsync()
    {
        try
        {
            await _authService.CheckSessionAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при проверке сессии");
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                StatusLabel.Text = "Ошибка инициализации";
                StatusLabel.TextColor = Colors.Red;
            });
        }
    }

    private void OnAuthServiceStateChanged(object? sender, EventArgs e)
        => MainThread.BeginInvokeOnMainThread(UpdateUi);

    private async void OnStartQrClicked(object sender, EventArgs e)
    {
        _authCts?.Cancel();
        _authCts = new CancellationTokenSource();

        StartQrButton.IsEnabled = false;

        try
        {
            await _authService.StartQrAuthAsync(_authCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при запросе QR-кода");
        }
        finally
        {
            StartQrButton.IsEnabled = true;
        }
    }

    private void OnResetClicked(object sender, EventArgs e)
    {
        _authCts?.Cancel();
        _authCts = null;

        _authService.Reset();
        _qrDrawable.Set(null);
        QrView.Invalidate();
        UpdateUi();
    }

    private void UpdateUi()
    {
        var qrData = _authService.QrData;
        var status = _authService.Status;

        StatusLabel.Text = status switch
        {
            AuthStatus.Unknown => "Не авторизованы. Нажмите «Показать QR-код».",
            AuthStatus.Initializing => "Инициализация...",
            AuthStatus.RequestingQr => "Ожидание подтверждения сервером...",
            AuthStatus.WaitingForScan => "Ожидание сканирования QR-кода...",
            AuthStatus.Authorized => "Авторизован",
            AuthStatus.Error => "Ошибка авторизации",
            AuthStatus.LoggedOut => "Сессия не активна",
            _ => "Неизвестный статус"
        };

        StatusLabel.TextColor = status switch
        {
            AuthStatus.WaitingForScan or AuthStatus.Authorized => Colors.Green,
            AuthStatus.Error => Colors.Red,
            _ => Colors.Blue
        };

        var showQr = qrData is not null && status is AuthStatus.WaitingForScan or AuthStatus.RequestingQr;

        QrBorder.IsVisible = showQr;
        QrHintLabel.IsVisible = showQr;

        if (showQr && qrData is not null)
        {
            DrawQr(qrData.Payload);
        }

        QrDataBorder.IsVisible = qrData is not null;
        if (qrData is not null)
        {
            QrPayloadLabel.Text = qrData.Payload;
            QrExpiresLabel.Text = qrData.ExpiresAt is { } expires
                ? $"Действителен до: {expires:yyyy-MM-dd HH:mm:ss} · статус: {qrData.Status ?? "—"}"
                : "Срок действия: уточняется сервером";
        }

        // Таймер обратного отсчёта
        if (showQr && qrData is not null && qrData.ExpiresAt is { } expiresAt)
        {
            var remaining = qrData.RemainingSeconds;
            if (remaining > 0)
            {
                var min = remaining / 60;
                var sec = remaining % 60;
                CountdownLabel.Text = $"Осталось: {min:00}:{sec:00}";
                CountdownLabel.IsVisible = true;
            }
            else
            {
                CountdownLabel.Text = "QR-код истёк";
                CountdownLabel.IsVisible = true;
            }
        }
        else
        {
            CountdownLabel.IsVisible = false;
        }

        if (_authService.Error is { } error)
        {
            StatusLabel.Text = $"Ошибка: {error.Message}";
            StatusLabel.TextColor = Colors.Red;
        }
    }

    private void DrawQr(string payload)
    {
        try
        {
            _qrDrawable.Set(QrRenderer.Render(payload));
            QrView.Invalidate();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось отрисовать QR-код");
            QrBorder.IsVisible = false;
        }
    }

    private static IQrCodeRenderer QrRenderer { get; } = new QrCodeRenderer();
}
