using Elocaro.Application.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Admin;

/// <summary>Registrations of the Admin module.</summary>
internal static class AdminRegistration
{
    internal static IServiceCollection AddAdminModule(this IServiceCollection services)
    {
        services.AddSingleton<IModuleState, ModuleStateService>();

        return services;
    }
}
