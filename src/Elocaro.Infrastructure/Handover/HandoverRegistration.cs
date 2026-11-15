using Elocaro.Application.Leases;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Handover;

/// <summary>Registrations of the Handover module.</summary>
internal static class HandoverRegistration
{
    internal static IServiceCollection AddHandoverModule(this IServiceCollection services)
    {
        services.AddScoped<IHandovers, Handovers>();

        return services;
    }
}
