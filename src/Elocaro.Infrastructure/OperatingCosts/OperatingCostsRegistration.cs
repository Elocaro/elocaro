using Elocaro.Application.Metering;
using Elocaro.Application.OperatingCosts;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.OperatingCosts;

/// <summary>Registrations of the OperatingCosts module.</summary>
internal static class OperatingCostsRegistration
{
    internal static IServiceCollection AddOperatingCostsModule(this IServiceCollection services)
    {
        services.AddScoped<ICostAllocation, CostAllocationService>();
        services.AddScoped<ISupplyContracts, SupplyContractService>();
        services.AddScoped<ICostSubtypes, CostSubtypeService>();
        services.AddScoped<IOperatingCosts, OperatingCostService>();
        services.AddScoped<IMeterUsage, SupplyContractMeterUsage>();

        return services;
    }
}
