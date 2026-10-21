namespace ExpressApp.Auth.Models;

/// <summary>
/// Содержимое QR-кода. Формируется и кодируется на стороне клиента,
/// сервер его не отдаёт (см. Specs/001-AUTH.MD, п. 4).
/// registration_key — seed приватного ключа Ed25519 (base64, 32 байта):
/// мобильное приложение получает им доступ к сессии после сканирования.
/// </summary>
public sealed class QrPayload
{
    public const int CurrentVersion = 1;

    [JsonPropertyName("registration_id")]
    public string RegistrationId { get; set; } = string.Empty;

    [JsonPropertyName("registration_token")]
    public string RegistrationToken { get; set; } = string.Empty;

    [JsonPropertyName("registration_key")]
    public string RegistrationKey { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;
}
