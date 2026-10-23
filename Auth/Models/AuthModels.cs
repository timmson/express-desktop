namespace ExpressApp.Auth.Models;

public sealed class DeviceMeta
{
    [JsonPropertyName("pushes")]
    public bool Pushes { get; set; } = false;

    [JsonPropertyName("timezone")]
    public string Timezone { get; set; } = "Europe/Moscow";

    [JsonPropertyName("permissions")]
    public DevicePermissions? Permissions { get; set; } = new();
}

public sealed class DevicePermissions
{
    [JsonPropertyName("notifications")]
    public bool Notifications { get; set; } = false;
}

public sealed class RegisterResponse
{
    [JsonPropertyName("registration_id")]
    public string RegistrationId { get; set; } = string.Empty;

    [JsonPropertyName("registration_token")]
    public string RegistrationToken { get; set; } = string.Empty;

    [JsonPropertyName("server_time")]
    public long ServerTime { get; set; }
}

public sealed class QrAuthRequest
{
    [JsonPropertyName("registration_id")]
    public string RegistrationId { get; set; } = string.Empty;

    [JsonPropertyName("registration_token")]
    public string RegistrationToken { get; set; } = string.Empty;

    [JsonPropertyName("sign_pub_key")]
    public string SignPubKey { get; set; } = string.Empty;

    [JsonPropertyName("udid")]
    public string Udid { get; set; } = string.Empty;

    [JsonPropertyName("app_version")]
    public string AppVersion { get; set; } = "1.0.0";

    [JsonPropertyName("device")]
    public string Device { get; set; } = "eXpress Desktop";

    [JsonPropertyName("device_software")]
    public string DeviceSoftware { get; set; } = string.Empty;

    [JsonPropertyName("device_hostname")]
    public string? DeviceHostname { get; set; } = null;

    [JsonPropertyName("device_meta")]
    public DeviceMeta? DeviceMeta { get; set; } = null;

    [JsonPropertyName("locale")]
    public string Locale { get; set; } = "ru";

    [JsonPropertyName("manufacturer")]
    public string Manufacturer { get; set; } = "Microsoft";

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "windows";

    [JsonPropertyName("platform_package_id")]
    public string PlatformPackageId { get; set; } = "ru.unlimitedtech.express.desktop";
}

public sealed class QrAuthResponse
{
    [JsonPropertyName("qr_request_id")]
    public string QrRequestId { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public string Payload { get; set; } = string.Empty;

    [JsonPropertyName("expires_at")]
    public DateTime? ExpiresAt { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("registration_id")]
    public string? RegistrationId { get; set; }

    /// <summary>Зашифрованный blob данных регистрации (base64), выдаётся сервером.</summary>
    [JsonPropertyName("registration_data")]
    public string? RegistrationData { get; set; }

    /// <summary>Токен CTS-сервиса, привязанный к подтверждённой регистрации.</summary>
    [JsonPropertyName("cts_registration_token")]
    public string? CtsRegistrationToken { get; set; }

    /// <summary>Токен RTS-сервиса, привязанный к подтверждённой регистрации.</summary>
    [JsonPropertyName("rts_registration_token")]
    public string? RtsRegistrationToken { get; set; }
}

public sealed class TokenActiveResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("rts_access_token")]
    public string RtsAccessToken { get; set; } = string.Empty;

    [JsonPropertyName("cts_access_token")]
    public string CtsAccessToken { get; set; } = string.Empty;

    [JsonPropertyName("rts_server_pub_key")]
    public string RtsServerPubKey { get; set; } = string.Empty;

    [JsonPropertyName("activated_at")]
    public DateTime? ActivatedAt { get; set; }

    [JsonPropertyName("last_active_at")]
    public DateTime? LastActiveAt { get; set; }

    [JsonPropertyName("app_version")]
    public string AppVersion { get; set; } = string.Empty;

    [JsonPropertyName("device")]
    public string Device { get; set; } = string.Empty;

    [JsonPropertyName("device_software")]
    public string DeviceSoftware { get; set; } = string.Empty;

    [JsonPropertyName("cts_host")]
    public string? CtsHost { get; set; } = null;

    [JsonPropertyName("pushes")]
    public bool Pushes { get; set; }

    [JsonPropertyName("blocked_reason")]
    public string? BlockedReason { get; set; } = null;
}

public sealed class ErrorResponse
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;

    [JsonPropertyName("error_code")]
    public string ErrorCode { get; set; } = string.Empty;

    [JsonPropertyName("details")]
    public object? Details { get; set; } = null;
}
