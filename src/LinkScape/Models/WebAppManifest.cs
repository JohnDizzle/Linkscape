using System.Text.Json.Serialization;

namespace LinkScape.Models;

public sealed class WebAppManifest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("short_name")]
    public string? ShortName { get; set; }

    [JsonPropertyName("start_url")]
    public string? StartUrl { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("display")]
    public string? Display { get; set; }

    [JsonPropertyName("theme_color")]
    public string? ThemeColor { get; set; }

    [JsonPropertyName("background_color")]
    public string? BackgroundColor { get; set; }

    [JsonPropertyName("icons")]
    public List<WebAppManifestIcon> Icons { get; set; } = [];
}

public sealed class WebAppManifestIcon
{
    [JsonPropertyName("src")]
    public string? Src { get; set; }

    [JsonPropertyName("sizes")]
    public string? Sizes { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("purpose")]
    public string? Purpose { get; set; }
}
