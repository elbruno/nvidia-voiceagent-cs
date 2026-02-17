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
        
        // Register PersonaPlex as both IPersonaPlexService and ILlmService
        // This allows it to be used as a general LLM or as a specialized speech-to-speech service
        services.AddSingleton<PersonaPlexService>();
        services.AddSingleton<IPersonaPlexService>(sp => sp.GetRequiredService<PersonaPlexService>());
        services.AddSingleton<ILlmService>(sp => sp.GetRequiredService<PersonaPlexService>());

        return services;
    }
}
