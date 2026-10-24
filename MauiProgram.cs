using ExpressApp.Auth;
using ExpressApp.Auth.Logging;
using ExpressApp.Auth.Qr;
using ExpressApp.Auth.Services;
using ExpressApp.Settings;
using Microsoft.Extensions.Logging;

namespace ExpressApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // DI-сервисы модуля настроек
        builder.Services.AddSingleton<ISettingsStore, FileSettingsStore>();

        // DI-сервисы модуля аутентификации
        builder.Services.AddSingleton<IAuthStateStore, PreferencesAuthStateStore>();
        builder.Services.AddTransient<IClientCrypto, Ed25519ClientCrypto>();

        // Встроенный контейнер MS DI не умеет разрешать Func<T> сам — фабрика
        // регистрируется вручную, чтобы QrAuthService создавал свежую пару ключей
        // и новые registration_* на каждый QR-код.
        builder.Services.AddSingleton<Func<IClientCrypto>>(sp =>
            () => ActivatorUtilities.CreateInstance<Ed25519ClientCrypto>(sp));

        builder.Services.AddSingleton<IDeviceInformation, DeviceInformation>();
        builder.Services.AddSingleton<IQrCodeRenderer, QrCodeRenderer>();

        // HttpClient с увеличенным таймаутом для long-polling (до 120 с на запрос)
        builder.Services.AddHttpClient<IAuthApiService, AuthApiService>(client =>
        {
            client.BaseAddress = new Uri("https://ru.public.express");
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        builder.Services.AddTransient<QrAuthService>();

        // Страницы и Shell resolving через DI (см. App.CreateWindow)
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<AppShell>();

// Логирование в консоль (Debug-провайдер в unpackaged-сборке
        // пишет только в отладчик, поэтому добавляем консольный провайдер)
        builder.Logging.AddProvider(new ConsoleLoggerProvider());
        builder.Logging.SetMinimumLevel(LogLevel.Debug);

        var app = builder.Build();

        // Статический доступ к контейнеру нужен App.CreateWindow, где DI не внедряется
        Services = app.Services;
        return app;
    }

    /// <summary>Построенный контейнер услуг приложения.</summary>
    public static IServiceProvider Services { get; private set; } = null!;
}
