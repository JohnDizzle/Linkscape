using System.Text;
using System.Text.Json.Serialization;

namespace LinkScape.Models;

internal sealed record ActiveTabsPackage(
    [property: JsonPropertyName("version")] int Version = 1,
    [property: JsonPropertyName("source")] string? Source = null,
    [property: JsonPropertyName("mode")] string? Mode = null,
    [property: JsonPropertyName("selectedTabId")] string? SelectedTabId = null,
    [property: JsonPropertyName("selectedIndex")] int? SelectedIndex = null,
    [property: JsonPropertyName("saveState")] bool? SaveState = null,
    [property: JsonPropertyName("collectionName")] string? CollectionName = null,
    [property: JsonPropertyName("tabs")] ActiveTabItem[]? Tabs = null)
{
    public const string ReplaceMode = "replace";
    public const string AppendMode = "append";
    private const int MaxPackageTabs = 50;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public bool ShouldAppend =>
        string.Equals(Mode, AppendMode, StringComparison.OrdinalIgnoreCase);

    public bool ShouldSaveState => SaveState ?? true;

    public IReadOnlyList<ActiveTabItem> ValidTabs =>
        (Tabs ?? [])
        .Where(tab => !string.IsNullOrWhiteSpace(tab.Url))
        .Take(MaxPackageTabs)
        .ToArray();

    public static bool TryParse(string? json, out ActiveTabsPackage package, out string error)
    {
        package = new ActiveTabsPackage();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "An active tabs JSON package is required.";
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ActiveTabsPackage>(json, SerializerOptions);
            if (parsed?.ValidTabs.Count > 0)
            {
                package = parsed;
                return true;
            }

            error = "The active tabs package must include at least one tab with a URL.";
            return false;
        }
        catch (JsonException ex)
        {
            error = $"The active tabs package JSON is invalid: {ex.Message}";
            return false;
        }
    }

    public static bool TryDecodePayload(string? payload, out string json)
    {
        json = string.Empty;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var decoded = Uri.UnescapeDataString(payload.Trim());
        if (decoded.StartsWith('{') || decoded.StartsWith('['))
        {
            json = decoded;
            return true;
        }

        try
        {
            var base64 = decoded.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
            json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

internal sealed record ActiveTabItem(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("selected")] bool? Selected,
    [property: JsonPropertyName("favoriteId")] string? FavoriteId,
    [property: JsonPropertyName("isFavorite")] bool? IsFavorite,
    [property: JsonPropertyName("isHomeTab")] bool? IsHomeTab,
    [property: JsonPropertyName("visitedCount")] int? VisitedCount,
    [property: JsonPropertyName("order")] int? Order,
    [property: JsonPropertyName("scrollX")] double? ScrollX,
    [property: JsonPropertyName("scrollY")] double? ScrollY,
    [property: JsonPropertyName("isSleeping")] bool? IsSleeping);
