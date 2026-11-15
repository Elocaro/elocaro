using Elocaro.Application.Admin;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Properties;

/// <summary>Registrations of the Properties module.</summary>
internal static class PropertiesRegistration
{
    internal static IServiceCollection AddPropertiesModule(this IServiceCollection services)
    {
        services.AddScoped<IOptionUsageSource, Properties.PropertyOptionUsage>();
        services.AddScoped<IPropertyService, PropertyService>();
        services.AddScoped<IPropertyAssignments, PropertyAssignments>();
        services.AddScoped<IOccupancyPlan, OccupancyPlanService>();
        services.AddScoped<IPropertyOwners, PropertyOwners>();
        services.AddScoped<IPropertyLandlords, PropertyLandlords>();
        services.AddScoped<IPersonReference, PropertyOwnerReferences>();

        services.AddScoped<ILandlordProfileService, LandlordProfileService>();

        return services;
    }
}
