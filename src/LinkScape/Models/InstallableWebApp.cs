namespace LinkScape.Models;

public sealed record InstallableWebApp
{
    public required string PageUrl { get; init; }
    public required string ManifestUrl { get; init; }
    public required string Name { get; init; }
    public string? ShortName { get; init; }
    public required string StartUrl { get; init; }
    public required string Scope { get; init; }
    public string? IconUrl { get; init; }
    public string? ThemeColor { get; init; }
    public string DisplayMode { get; init; } = "standalone";
}
