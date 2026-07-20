public static class OnlineReferenceService
{
    private const string ReactorRepositoryUrl = "https://github.com/microsoft/microsoft-ui-reactor";
    private const string ReactorSearchUrl = "https://github.com/microsoft/microsoft-ui-reactor/search";
    private const string ReactorNuGetUrl = "https://www.nuget.org/packages/Microsoft.UI.Reactor";

    public static ChatToolStatus GetStatus() =>
        new(
            "online.reference",
            true,
            "Online reference links are available when the device has internet access.");

    public static ChatToolResult BuildReactorReference(string query)
    {
        query = string.IsNullOrWhiteSpace(query) ? "Microsoft.UI.Reactor" : query.Trim();
        var searchUrl = BuildReactorSearchUrl(query);

        return new ChatToolResult(
            "online.reactor.search",
            true,
            $"Online Reactor reference:\nRepository: {ReactorRepositoryUrl}\nSearch: {searchUrl}\nNuGet: {ReactorNuGetUrl}");
    }

    public static string BuildReactorSearchUrl(string query)
    {
        var escapedQuery = Uri.EscapeDataString(string.IsNullOrWhiteSpace(query) ? "Microsoft.UI.Reactor" : query.Trim());
        return $"{ReactorSearchUrl}?q={escapedQuery}&type=code";
    }
}
