using Elocaro.Application.People;
using Elocaro.Application.Portal;
using Elocaro.Application.Todos;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Portal;

/// <summary>Registrations of the Portal module.</summary>
internal static class PortalRegistration
{
    internal static IServiceCollection AddPortalModule(this IServiceCollection services)
    {
        services.AddScoped<ITenantPortal, TenantPortal>();
        services.AddScoped<IContactChangeRequests, ContactChangeRequests>();
        services.AddScoped<ITodoSource, ContactChangeTodos>();
        services.AddScoped<DocumentNotifications>();
        services.AddScoped<IOwnerPortal, OwnerPortal>();

        return services;
    }
}
