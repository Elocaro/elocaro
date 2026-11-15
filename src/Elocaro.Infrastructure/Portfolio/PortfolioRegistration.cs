using Elocaro.Application.Portfolio;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Portfolio;

/// <summary>Registrations of the Portfolio module.</summary>
internal static class PortfolioRegistration
{
    internal static IServiceCollection AddPortfolioModule(this IServiceCollection services)
    {
        services.AddScoped<IPortfolioFigures, Portfolio.PortfolioFiguresService>();
        services.AddScoped<IPropertyKeyFigures, Portfolio.PropertyKeyFiguresService>();
        services.AddScoped<IRentPotential, Portfolio.RentPotentialService>();
        services.AddScoped<INavigationContext, Portfolio.NavigationContextResolver>();

        return services;
    }
}
