using System.Security.Cryptography;
using ExpressApp.Auth.Models;

namespace ExpressApp.Auth;

/// <summary>
/// Локальное состояние клиента: идентификаторы, ключи и выданная регистрация.
/// Заменяет IndexedDB authState из веб-клиента.
/// </summary>
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

    /// <summary>Признак живой сессии — по нему веб-клиент решает, слать ли token/active.</summary>
    public bool HasLiveSession =>
        !string.IsNullOrEmpty(AccessToken) && TokenExpiresAt is { } expires && expires > DateTimeOffset.UtcNow;
}

/// <summary>
/// Хранилище состояния авторизации.
/// Использует <see cref="Preferences"/>: SecureStorage на Windows требует
/// мастер-пароль для файла хранилища, что для прототипа избыточно.
/// </summary>
public interface IAuthStateStore
{
    AuthState Load();
    void Save(AuthState state);
    void Clear();
}

public sealed class PreferencesAuthStateStore : IAuthStateStore
{
    private const string UdidKey = "auth.udid";
    private const string PublicKeyKey = "auth.sign_pub_key";
    private const string PrivateKeyKey = "auth.sign_priv_seed";
    private const string RegistrationIdKey = "auth.registration_id";
    private const string RegistrationTokenKey = "auth.registration_token";
    private const string AccessTokenKey = "auth.access_token";
    private const string ExpiresAtKey = "auth.token_expires_at";

    public AuthState Load()
    {
        var udid = Preferences.Get(UdidKey, string.Empty);
        var pubKey = Preferences.Get(PublicKeyKey, string.Empty);
        var privKey = Preferences.Get(PrivateKeyKey, (string?)null);
        var regId = Preferences.Get(RegistrationIdKey, string.Empty);
        var regToken = Preferences.Get(RegistrationTokenKey, string.Empty);

        // Генерируем registration_id и registration_token при первом запуске
        if (string.IsNullOrEmpty(regId))
        {
            regId = Guid.NewGuid().ToString();
            Preferences.Set(RegistrationIdKey, regId);
        }

        if (string.IsNullOrEmpty(regToken))
        {
            regToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            Preferences.Set(RegistrationTokenKey, regToken);
        }

        return new AuthState(
            Udid: udid,
            PublicKeyBase64: pubKey,
            PrivateKeySeedBase64: privKey,
            RegistrationId: regId,
            RegistrationToken: regToken,
            AccessToken: Preferences.Get(AccessTokenKey, (string?)null),
            TokenExpiresAt: TryReadExpires());
    }

    public void Save(AuthState state)
    {
        Preferences.Set(UdidKey, state.Udid);
        Preferences.Set(PublicKeyKey, state.PublicKeyBase64);
        Preferences.Set(PrivateKeyKey, state.PrivateKeySeedBase64);
        Preferences.Set(RegistrationIdKey, state.RegistrationId);
        Preferences.Set(RegistrationTokenKey, state.RegistrationToken);
        Preferences.Set(AccessTokenKey, state.AccessToken);

        if (state.TokenExpiresAt is { } expires)
        {
            Preferences.Set(ExpiresAtKey, expires.ToUnixTimeSeconds());
        }
        else
        {
            Preferences.Remove(ExpiresAtKey);
        }
    }

    public void Clear()
    {
        foreach (var key in new[]
                 {
                     RegistrationIdKey, RegistrationTokenKey,
                     AccessTokenKey, ExpiresAtKey
                 })
        {
            Preferences.Remove(key);
        }
    }

    private static DateTimeOffset? TryReadExpires()
    {
        var raw = Preferences.Get(ExpiresAtKey, 0L);
        return raw > 0 ? DateTimeOffset.FromUnixTimeSeconds(raw) : null;
    }
}
