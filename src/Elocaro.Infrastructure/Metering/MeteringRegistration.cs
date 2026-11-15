using Elocaro.Application.Metering;
using Elocaro.Application.Todos;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Metering;

/// <summary>Registrations of the Metering module.</summary>
internal static class MeteringRegistration
{
    internal static IServiceCollection AddMeteringModule(this IServiceCollection services)
    {
        services.AddScoped<IMeterService, MeterService>();
        services.AddScoped<IReadingReports, ReadingReports>();
        services.AddScoped<ITodoSource, ReadingReportTodos>();
        services.AddScoped<ReadingReminders>();
        services.AddScoped<IMeterImport, MeterImport>();
        services.AddScoped<IConsumptionInformation, ConsumptionInformation>();
        services.AddScoped<ConsumptionNotifications>();
        services.AddScoped<ITodoSource, CalibrationDueTodos>();
        services.AddScoped<ITodoSource, RemoteReadingTodos>();

        return services;
    }
}
