using Elocaro.Application.People;
using Elocaro.Application.Privacy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Privacy;

/// <summary>Registrations of the Privacy module.</summary>
internal static class PrivacyRegistration
{
    internal static IServiceCollection AddPrivacyModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PrivacyOptions>(configuration.GetSection(PrivacyOptions.Section));
        services.AddScoped<IPrivacyService, Privacy>();
        services.AddScoped<IPersonalDataSource, MailPersonalData>();

        return services;
    }
}
