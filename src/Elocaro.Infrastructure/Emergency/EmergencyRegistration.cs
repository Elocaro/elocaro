using Elocaro.Application.Emergency;
using Elocaro.Application.Todos;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Emergency;

/// <summary>Registrations of the Emergency module.</summary>
internal static class EmergencyRegistration
{
    internal static IServiceCollection AddEmergencyModule(this IServiceCollection services)
    {
        services.AddScoped<Emergency.EmergencyFolderContent>();
        services.AddScoped<IEmergencyFolder, Emergency.EmergencyFolderService>();
        services.AddScoped<ITodoSource, Emergency.EmergencyFolderTodos>();

        return services;
    }
}
