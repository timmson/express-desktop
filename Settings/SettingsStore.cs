using Microsoft.Extensions.Logging;

namespace ExpressApp.Settings;

/// <summary>
/// Настройки приложения, сохраняемые между запусками в ~/.express/settings.json.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Секция регистрации. null — значит приложение ещё не генерировало QR
    /// (или данные сброшены), поэтому в JSON блок просто не попадает.
    /// </summary>
    [JsonPropertyName("registration")]
    public RegistrationSettings? Registration { get; set; }

    /// <summary>Возвращает существующую секцию либо создаёт новую — для точечных правок.</summary>
    public RegistrationSettings EnsureRegistration() => Registration ??= new RegistrationSettings();
}

/// <summary>
/// Ключи регистрации: локально сгенерированные параметры QR и выданные сервером
/// токены сессии. Имена сохранены как в API и QR-payload, чтобы JSON-файл
/// сопоставлялся со спецификацией без пересказа схемы.
/// </summary>
public sealed class RegistrationSettings
{
    /// <summary>Момент генерации QR-payload, по которому оценивается свежесть ключей.</summary>
    [JsonPropertyName("generated_at")]
    public DateTimeOffset? GeneratedAt { get; set; }

    [JsonPropertyName("registration_id")]
    public string? RegistrationId { get; set; }

    [JsonPropertyName("registration_token")]
    public string? RegistrationToken { get; set; }

    /// <summary>registration_key — seed приватного ключа Ed25519 (base64).</summary>
    [JsonPropertyName("registration_key")]
    public string? RegistrationKey { get; set; }

    /// <summary>Зашифрованный blob данных регистрации из ответа сервера (base64).</summary>
    [JsonPropertyName("registration_data")]
    public string? RegistrationData { get; set; }

    /// <summary>CTS-токен, выданный сервером после подтверждения QR.</summary>
    [JsonPropertyName("cts_registration_token")]
    public string? CtsRegistrationToken { get; set; }

    /// <summary>RTS-токен, выданный сервером после подтверждения QR.</summary>
    [JsonPropertyName("rts_registration_token")]
    public string? RtsRegistrationToken { get; set; }
}

/// <summary>
/// Хранилище настроек приложения в файле JSON каталога домашней папки пользователя.
/// </summary>
public interface ISettingsStore
{
    /// <summary>Полный путь к файлу настроек.</summary>
    string FilePath { get; }

    AppSettings Load();
    void Save(AppSettings settings);

    /// <summary>Атомарное чтение-изменение-запись, чтобы не терять другие поля при точечных правках.</summary>
    void Update(Action<AppSettings> mutate);

    void ClearRegistration();
}

public sealed class FileSettingsStore : ISettingsStore
{
    // Все операции с файлом идут под одним замком: настройки читаются и из UI-потока,
    // и из фоновых задач ожидания сканирования QR.
    private static readonly object Gate = new();

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        // Base64-значения (token, key) содержат '+', '/' и '=' — relaxed-энкодер
        // оставляет их как есть, файл остаётся читаемым (см. Auth.JsonOptions).
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ILogger<FileSettingsStore> _logger;

    public FileSettingsStore(ILogger<FileSettingsStore> logger) => _logger = logger;

    public string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".express",
        "settings.json");

    public AppSettings Load()
    {
        lock (Gate)
        {
            return ReadUnchecked();
        }
    }

    public void Save(AppSettings settings)
    {
        lock (Gate)
        {
            WriteUnchecked(settings);
        }
    }

    public void Update(Action<AppSettings> mutate)
    {
        lock (Gate)
        {
            var settings = ReadUnchecked();
            mutate(settings);
            WriteUnchecked(settings);
        }
    }

    public void ClearRegistration() => Update(settings => settings.Registration = null);

    private AppSettings ReadUnchecked()
    {
        if (!File.Exists(FilePath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            // Повреждённый файл не должен ронять приложение — стартуем с чистых настроек.
            _logger.LogWarning(ex, "Не удалось прочитать {FilePath} — используем значения по умолчанию", FilePath);
            return new AppSettings();
        }
    }

    private void WriteUnchecked(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);

        // Запись через временный файл: при сбое питания/процесса остаётся либо
        // старая, либо новая целостная версия settings.json.
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, SerializerOptions));
        File.Move(tempPath, FilePath, overwrite: true);

        _logger.LogDebug("Настройки сохранены в {FilePath}", FilePath);
    }
}
