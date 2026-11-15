using Elocaro.Application.Admin;
using Elocaro.Application.Tax;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Tax;

/// <summary>Registrations of the Tax module.</summary>
internal static class TaxRegistration
{
    internal static IServiceCollection AddTaxModule(this IServiceCollection services)
    {
        services.AddScoped<IOptionUsageSource, Tax.TaxOptionUsage>();
        services.AddScoped<IDepreciationService, DepreciationService>();
        services.AddScoped<ILoanService, LoanService>();
        services.AddScoped<IMaintenanceService, MaintenanceService>();
        services.AddScoped<IAnlageVService, AnlageVService>();
        services.AddScoped<IDatevExport, DatevExportService>();

        return services;
    }
}
