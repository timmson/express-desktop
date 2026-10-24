namespace ExpressApp.Auth.Models;

public sealed class RegisterRequest
{
    [JsonPropertyName("udid")]
    public string Udid { get; set; } = string.Empty;

    [JsonPropertyName("sign_pub_key")]
    public string SignPubKey { get; set; } = string.Empty;

    [JsonPropertyName("app_version")]
    public string AppVersion { get; set; } = "3.75.58";

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
