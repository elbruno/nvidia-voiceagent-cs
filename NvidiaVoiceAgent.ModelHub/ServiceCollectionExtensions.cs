using Microsoft.Extensions.DependencyInjection;

namespace NvidiaVoiceAgent.ModelHub;

/// <summary>
/// Extension methods for registering ModelHub services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add ModelHub services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure ModelHub options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddModelHub(
        this IServiceCollection services,
        Action<ModelHubOptions>? configure = null)
    {
        if (configure != null)
        {
            services.Configure(configure);
        }
        else
        {
            services.Configure<ModelHubOptions>(_ => { });
        }

        services.AddSingleton<IModelRegistry, ModelRegistry>();
        services.AddSingleton<IProgressReporter, ConsoleProgressReporter>();
        services.AddSingleton<IModelDownloadService, ModelDownloadService>();

        return services;
    }
}
