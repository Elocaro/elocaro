using Elocaro.Application.Legal;
using Elocaro.Application.Todos;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Legal;

/// <summary>Registrations of the Legal module.</summary>
internal static class LegalRegistration
{
    internal static IServiceCollection AddLegalModule(this IServiceCollection services)
    {
        services.AddScoped<ILegalReferences, Legal.LegalReferences>();
        services.AddScoped<ITodoSource, Legal.LegalReferenceTodos>();

        return services;
    }
}
