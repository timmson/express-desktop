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
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
