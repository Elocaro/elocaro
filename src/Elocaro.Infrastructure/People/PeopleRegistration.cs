using Elocaro.Application.People;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.People;

/// <summary>Registrations of the People module.</summary>
internal static class PeopleRegistration
{
    internal static IServiceCollection AddPeopleModule(this IServiceCollection services)
    {
        services.AddScoped<IPersonService, PersonService>();

        return services;
    }
}
