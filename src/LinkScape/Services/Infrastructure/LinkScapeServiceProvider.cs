using Microsoft.Extensions.DependencyInjection;

namespace LinkScape.Services.Infrastructure;

internal static class LinkScapeServiceProvider
{
    private static IServiceProvider? _serviceProvider;

    internal static void Initialize(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    internal static T GetRequiredService<T>()
        where T : notnull
    {
        if (_serviceProvider is null)
        {
            throw new InvalidOperationException("LinkScape services have not been initialized.");
        }

        return _serviceProvider.GetRequiredService<T>();
    }
}
