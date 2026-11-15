using Elocaro.Application.Documents;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Correspondence;

/// <summary>Registrations of the Correspondence module.</summary>
internal static class CorrespondenceRegistration
{
    internal static IServiceCollection AddCorrespondenceModule(this IServiceCollection services)
    {
        services.AddScoped<IDocumentReadiness, DocumentReadiness>();
        services.AddScoped<ILetters, Letters>();

        return services;
    }
}
