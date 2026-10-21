namespace ExpressApp.Auth;

/// <summary>
/// Единые настройки JSON для всех DTO модуля авторизации.
/// Сервер использует snake_case, поэтому свойства маппятся через JsonPropertyName;
/// опции лишь делают разбор устойчивым к регистру и лишним полям.
/// </summary>
internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Base64-значения (registration_token, ключи) содержат '+', '/' и '='.
        // Дефолтный энкодер экранирует их до \u002B, из-за чего payload QR и тело
        // запроса расходится с эталонным веб-клиентом и растёт в длине.
        // HTML-контекст здесь отсутствует (JSON уходит в QR и в HTTP-тело),
        // поэтому relaxed-энкодер безопасен.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
