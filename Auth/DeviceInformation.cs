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
        Timezone = ResolveTimezone(),
        Permissions = new DevicePermissions { Notifications = false }
    };

    public RegisterRequest Apply(RegisterRequest request)
    {
        request.AppVersion = AppVersion;
        request.Device = ResolveDevice();
        request.DeviceSoftware = ResolveSoftware();
        request.DeviceHostname = null;
        request.DeviceMeta = BuildMeta();
        request.Locale = ResolveLocale();
        request.Manufacturer = "Unknown";
        request.Platform = ResolvePlatform();
        request.PlatformPackageId = PlatformPackageId;
        return request;
    }

    public QrAuthRequest Apply(QrAuthRequest request)
    {
        request.AppVersion = AppVersion;
        request.Device = ResolveDevice();
        request.DeviceSoftware = ResolveSoftware();
        request.DeviceHostname = null;
        request.DeviceMeta = BuildMeta();
        request.Locale = ResolveLocale();
        request.Manufacturer = "Unknown";
        request.Platform = ResolvePlatform();
        request.PlatformPackageId = PlatformPackageId;
        return request;
    }

    private static string ResolveDevice()
    {
        var name = NullToDefault(DeviceInfo.Current.Name, "desktop");
        var model = NullToDefault(DeviceInfo.Current.Model, string.Empty);
        return string.IsNullOrEmpty(model) || model == name ? name : $"{name} ({model})";
    }

    private static string ResolveSoftware()
        => NullToDefault(DeviceInfo.Current.VersionString, "unknown");

    private static string ResolvePlatform()
    {
        return "web";
    }

    private static string ResolveLocale()
    {
        var culture = CultureInfo.CurrentUICulture.Name;
        return string.IsNullOrEmpty(culture) ? "ru" : culture.Split('-')[0];
    }

    private static string ResolveTimezone()
    {
        try
        {
            return TimeZoneInfo.Local.Id;
        }
        catch (Exception)
        {
            return "Europe/Moscow";
        }
    }

    private static string NullToDefault(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
