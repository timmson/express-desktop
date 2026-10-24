using System.Security.Cryptography;
using ExpressApp.Auth.Models;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace ExpressApp.Auth;

// ──────────────────────────────────────────────────────────────────
// Платформо-независимые контракты (дублируют те, что в проекте в
// файлах, зависящих от MAUI: AuthStateStore.cs, DeviceInformation.cs).
// ──────────────────────────────────────────────────────────────────

public sealed record AuthState(
    string Udid,
    string PublicKeyBase64,
    string? PrivateKeySeedBase64,
    string? RegistrationId,
    string? RegistrationToken,
    string? AccessToken,
    DateTimeOffset? TokenExpiresAt)
{
    public bool IsRegistered => !string.IsNullOrEmpty(RegistrationId) && !string.IsNullOrEmpty(RegistrationToken);
    public bool HasLiveSession =>
        !string.IsNullOrEmpty(AccessToken) && TokenExpiresAt is { } expires && expires > DateTimeOffset.UtcNow;
}

public interface IAuthStateStore
{
    AuthState Load();
    void Save(AuthState state);
    void Clear();
    void ClearAll();
}

public interface IDeviceInformation
{
    RegisterRequest Apply(RegisterRequest request);
    QrAuthRequest Apply(QrAuthRequest request);
    DeviceMeta BuildMeta();
}

/// <summary>In-memory замена PreferencesAuthStateStore.</summary>
public sealed class MemoryAuthStateStore : IAuthStateStore
{
    private static AuthState Empty() => new(
        Udid: string.Empty, PublicKeyBase64: string.Empty, PrivateKeySeedBase64: null,
        RegistrationId: null, RegistrationToken: null, AccessToken: null, TokenExpiresAt: null);

    private AuthState _state = Empty();

    public AuthState Load() => _state;
    public void Save(AuthState state) => _state = state;

    public void Clear() => _state = _state with
    {
        RegistrationId = null, RegistrationToken = null,
        AccessToken = null, TokenExpiresAt = null
    };

    public void ClearAll() => _state = Empty();
}

/// <summary>Замена DeviceInformation без DeviceInfo.Current.</summary>
public sealed class StubDeviceInformation : IDeviceInformation
{
    public DeviceMeta BuildMeta() => new()
    {
        Pushes = false, Timezone = "Europe/Moscow",
        Permissions = new DevicePermissions { Notifications = false }
    };

    public RegisterRequest Apply(RegisterRequest r)
    {
        r.Device = "test-desktop"; r.DeviceSoftware = "10.0";
        r.DeviceMeta = BuildMeta(); r.Platform = "windows";
        return r;
    }

    public QrAuthRequest Apply(QrAuthRequest r)
    {
        r.Device = "test-desktop"; r.DeviceSoftware = "10.0";
        r.DeviceMeta = BuildMeta(); r.Platform = "windows";
        return r;
    }
}
