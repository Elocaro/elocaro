using Elocaro.Application.Setup;
using Elocaro.Application.Todos;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Setup;

/// <summary>Registrations of the Setup module.</summary>
internal static class SetupRegistration
{
    internal static IServiceCollection AddSetupModule(this IServiceCollection services)
    {
        services.AddScoped<ITodoSource, Setup.SetupChecklistTodos>();
        services.AddScoped<IDataImport, Setup.DataImport>();
        services.AddScoped<IDemoData, Setup.DemoData>();

        return services;
    }
}
