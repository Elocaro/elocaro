using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elocaro.Infrastructure.Templates;

/// <summary>Registrations of the Templates module.</summary>
internal static class TemplatesRegistration
{
    internal static IServiceCollection AddTemplatesModule(this IServiceCollection services)
    {
        services.TryAddSingleton<ScribanRenderer>();

        return services;
    }
}
