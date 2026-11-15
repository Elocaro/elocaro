using Elocaro.Application.Heating;
using Elocaro.Application.Metering;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Heating;

/// <summary>Registrations of the Heating module.</summary>
internal static class HeatingRegistration
{
    internal static IServiceCollection AddHeatingModule(this IServiceCollection services)
    {
        services.AddScoped<IHeatingSettings, HeatingSettingsService>();
        services.AddScoped<IHeatingReferences, HeatingReferences>();
        services.AddScoped<IMeterUsage, HeatingMeterUsage>();
        services.AddHttpClient<DegreeDayImporter>(http => http.Timeout = TimeSpan.FromSeconds(90));

        return services;
    }
}
