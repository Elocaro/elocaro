using Elocaro.Application.Admin;
using Elocaro.Application.Documents;
using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Application.Todos;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Leases;

/// <summary>Registrations of the Leases module.</summary>
internal static class LeasesRegistration
{
    internal static IServiceCollection AddLeasesModule(this IServiceCollection services)
    {
        services.AddScoped<IOptionUsageSource, Leases.LeaseOptionUsage>();
        services.AddScoped<IDeposits, Leases.Deposits>();
        services.AddScoped<ITodoSource, Leases.DepositTodos>();
        services.AddScoped<IOccupancyLookup, LeaseOccupancy>();
        services.AddScoped<IOccupancyPlan, OccupancyPlanService>();
        services.AddScoped<ILeaseService, LeaseService>();
        services.AddScoped<IPersonReference, LeasePersonalReferences>();
        services.AddScoped<IPersonalDataSource, LeasePersonalData>();
        services.AddScoped<ICpiService, CpiService>();
        services.AddScoped<RentStepApplication>();
        services.AddScoped<ITodoSource, RentModelTodos>();
        services.AddScoped<IContractSignatures, ContractSignatures>();
        services.AddScoped<ILeaseContracts, LeaseContracts>();
        services.AddScoped<IRentIncreases, RentIncreases>();
        services.AddScoped<IDocumentLeaseSource, DocumentLeaseSource>();

        return services;
    }
}
