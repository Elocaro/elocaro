using Elocaro.Application.Deadlines;
using Elocaro.Application.Prospects;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Prospects;

/// <summary>Registrations of the Prospects module.</summary>
internal static class ProspectsRegistration
{
    internal static IServiceCollection AddProspectsModule(this IServiceCollection services)
    {
        services.AddScoped<IProspects, ProspectService>();
        services.AddScoped<ISelfDisclosures, SelfDisclosureService>();
        services.AddScoped<IExposes, ExposeService>();
        services.AddScoped<IVacancies, VacancyService>();
        services.AddScoped<IViewings, ViewingService>();
        services.AddScoped<IDeadlineSource, ViewingDeadlineSource>();
        services.AddScoped<IDeadlineSource, VacancyDeadlineSource>();
        services.AddScoped<ProspectRetention>();
        return services;
    }
}
