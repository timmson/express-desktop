using ExpressApp.Auth;
using ExpressApp.Auth.Models;
using ExpressApp.Auth.Qr;
using ExpressApp.Auth.Services;
using ExpressApp.Settings;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ExpressApp.Tests.Auth;

public sealed class QrAuthServiceSessionPersistenceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsPath;

    public QrAuthServiceSessionPersistenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"express-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _settingsPath = Path.Combine(_tempDir, "settings.json");
        Environment.SetEnvironmentVariable("EXPRESS_SETTINGS_PATH", _settingsPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
        Environment.SetEnvironmentVariable("EXPRESS_SETTINGS_PATH", null);
    }

    [Fact]
    public async Task StartQrAuthAsync_PersistsSessionKeys_OnSuccessfulAuth()
    {
        // Arrange — создаём реальное состояние и криптографию
        var state = new AuthState(
            Udid: "test-udid",
            PublicKeyBase64: "test-pub-key",
            PrivateKeySeedBase64: null,
            RegistrationId: null,
            RegistrationToken: null,
            AccessToken: null,
            TokenExpiresAt: null);

        var store = new MemoryAuthStateStore();
        store.Save(state);

        var crypto = new TestClientCrypto(state);

        var expectedResponse = new QrAuthResponse
        {
            QrRequestId = "test-request-id",
            Payload = "test-payload",
            RegistrationData = "encrypted-registration-blob",
            CtsRegistrationToken = "cts-token-abc-123",
            RtsRegistrationToken = "rts-token-xyz-789",
            Status = "authorized",
            RegistrationId = "confirmed-reg-id"
        };

        var apiMock = new Mock<IAuthApiService>();
        apiMock.Setup(a => a.RequestQrAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<SignedRequestHeaders>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var deviceInfoMock = new Mock<IDeviceInformation>();
        deviceInfoMock.Setup(d => d.Apply(It.IsAny<QrAuthRequest>()))
            .Returns(new QrAuthRequest
            {
                AppVersion = "1.0.0",
                Platform = "windows",
                PlatformPackageId = "ru.unlimitedtech.express.desktop"
            });

        var qrRendererMock = new Mock<IQrCodeRenderer>();
        qrRendererMock.Setup(r => r.Render(It.IsAny<string>()))
            .Returns(new QrSvg("<svg/>", 25, 1, true, Array.Empty<bool>()));

        var loggerMock = new Mock<ILogger<QrAuthService>>();
        // FileSettingsStore требует ILogger<FileSettingsStore> — отдельный мок.
        var settingsLoggerMock = new Mock<ILogger<FileSettingsStore>>();

        var authService = new QrAuthService(
            cryptoFactory: () => crypto,
            api: apiMock.Object,
            deviceInfo: deviceInfoMock.Object,
            qrRenderer: qrRendererMock.Object,
            store: store,
            settings: new FileSettingsStore(settingsLoggerMock.Object),
            logger: loggerMock.Object);

        var tcs = new TaskCompletionSource<bool>();
        authService.StateChanged += (_, _) =>
        {
            if (authService.Status == AuthStatus.Authorized)
                tcs.TrySetResult(true);
        };

        // Act — StartQrAuthAsync запускает ожидание в фоне и сразу возвращается.
        _ = authService.StartQrAuthAsync();
        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));

        // Assert
        Assert.True(completedTask == tcs.Task, "Фоновая задача авторизации не завершилась за 10 секунд");
        Assert.Equal(AuthStatus.Authorized, authService.Status);
        Assert.Null(authService.Error);

        var saved = new FileSettingsStore(settingsLoggerMock.Object).Load();
        Assert.NotNull(saved.Registration);
        Assert.Equal("encrypted-registration-blob", saved.Registration.RegistrationData);
        Assert.Equal("cts-token-abc-123", saved.Registration.CtsRegistrationToken);
        Assert.Equal("rts-token-xyz-789", saved.Registration.RtsRegistrationToken);
        Assert.Equal("confirmed-reg-id", saved.Registration.RegistrationId);

        // Проверим, что файл реально записан на диск
        Assert.True(File.Exists(_settingsPath), "Файл настроек не был создан");
        var jsonContent = File.ReadAllText(_settingsPath);
        Assert.Contains("cts-token-abc-123", jsonContent);
        Assert.Contains("rts-token-xyz-789", jsonContent);
        Assert.Contains("encrypted-registration-blob", jsonContent);
    }
}
