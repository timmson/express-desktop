using System.Globalization;
using System.Runtime.InteropServices;
using ExpressApp.Auth.Models;

namespace ExpressApp.Auth;

/// <summary>
/// Метаданные окружения для полей register/qr-запроса.
/// Desktop-клиент отдаёт то, что доступено через MAUI Essentials.
/// </summary>
public interface IDeviceInformation
{
    RegisterRequest Apply(RegisterRequest request);
    QrAuthRequest Apply(QrAuthRequest request);
    DeviceMeta BuildMeta();
}

public sealed class DeviceInformation : IDeviceInformation
{
    public const string AppVersion = "1.0.0";
    public const string PlatformPackageId = "ru.unlimitedtech.express";

    public DeviceMeta BuildMeta() => new()
    {
        Pushes = false,
        Timezone = "Europe/Moscow",
        Permissions = new DevicePermissions { Notifications = false }
    };

    public RegisterRequest Apply(RegisterRequest request)
    {
        request.AppVersion = AppVersion;
        request.Device = "Chrome 155.0";
        request.DeviceSoftware = "Windows 11";
        request.DeviceHostname = null;
        request.DeviceMeta = BuildMeta();
        request.Locale = "ru";
        request.Manufacturer = "Google";
        request.Platform = "web";
        request.PlatformPackageId = PlatformPackageId;
        return request;
    }

    public QrAuthRequest Apply(QrAuthRequest request)
    {
        request.AppVersion = AppVersion;
        request.Device = "Chrome 155.0";
        request.DeviceSoftware = "Windows 11";
        request.DeviceHostname = null;
        request.DeviceMeta = BuildMeta();
        request.Locale = "ru";
        request.Manufacturer = "Google";
        request.Platform = "web";
        request.PlatformPackageId = PlatformPackageId;
        return request;
    }

    private static string NullToDefault(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
