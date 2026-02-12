using Microsoft.Extensions.DependencyInjection;
using NvidiaVoiceAgent.Core.Models;
using NvidiaVoiceAgent.Core.Services;

namespace NvidiaVoiceAgent.Core;

/// <summary>
/// Extension methods for registering NvidiaVoiceAgent.Core services with DI.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds core voice agent services (ASR, Audio Processing) to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional action to configure ModelConfig.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddVoiceAgentCore(
        this IServiceCollection services,
        Action<ModelConfig>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        services.AddSingleton<IAsrService, AsrService>();
        services.AddSingleton<IAudioProcessor, AudioProcessor>();

        return services;
    }
}
