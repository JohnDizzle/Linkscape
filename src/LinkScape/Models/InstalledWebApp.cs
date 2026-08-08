namespace LinkScape.Models;

public sealed record InstalledWebApp
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? ShortName { get; init; }
    public required string Origin { get; init; }
    public required string StartUrl { get; init; }
    public required string Scope { get; init; }
    public string? ManifestUrl { get; init; }
    public string? IconUrl { get; init; }
    public string? LocalIconPath { get; init; }
    public string? ThemeColor { get; init; }
    public string DisplayMode { get; init; } = "standalone";
    public DateTimeOffset InstalledAt { get; init; }
}
