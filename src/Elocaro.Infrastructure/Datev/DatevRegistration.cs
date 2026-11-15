using Elocaro.Application.Datev;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Datev;

/// <summary>Registrations of the Datev module.</summary>
internal static class DatevRegistration
{
    internal static IServiceCollection AddDatevModule(this IServiceCollection services)
    {
        services.AddScoped<IDatevExport, DatevExportService>();

        return services;
    }
}
