using Elocaro.Application.Admin;
using Elocaro.Application.Audit;
using Elocaro.Application.Finance;
using Elocaro.Application.People;
using Elocaro.Application.Todos;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Finance;

/// <summary>Registrations of the Finance module.</summary>
internal static class FinanceRegistration
{
    internal static IServiceCollection AddFinanceModule(this IServiceCollection services)
    {
        services.AddScoped<IOptionUsageSource, Finance.FinanceOptionUsage>();
        services.AddScoped<IRentReductions, Finance.RentReductions>();
        services.AddScoped<ITenantAccounts, TenantAccounts>();
        services.AddScoped<IFinanceSettings, FinanceSettingsService>();
        services.AddScoped<ITodoSource, FinanceTodos>();
        services.AddScoped<IDunning, DunningService>();
        services.AddScoped<IUndoCompensation, ChargeCompensation>();
        services.AddScoped<IUndoCompensation, PaymentCompensation>();
        services.AddScoped<ITodoSource, DunningTodos>();
        services.AddScoped<IPersonalDataSource, DunningPersonalData>();
        services.AddScoped<IPersonalDataSource, TenantAccountPersonalData>();

        return services;
    }
}
