using Elocaro.Application.Files;
using Elocaro.Application.People;
using Elocaro.Application.Tickets;
using Elocaro.Application.Todos;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Tickets;

/// <summary>Registrations of the Tickets module.</summary>
internal static class TicketsRegistration
{
    internal static IServiceCollection AddTicketsModule(this IServiceCollection services)
    {
        services.AddScoped<ITickets, TicketService>();
        services.AddScoped<IFileShare, TicketFileShare>();
        services.AddScoped<IPersonReference, TicketPersonReferences>();
        services.AddScoped<ITodoSource, TicketTodos>();
        services.AddScoped<ITicketWork, TicketWorkService>();
        services.AddScoped<IContractorLinks, ContractorLinks>();
        services.AddScoped<ICareRoutines, CareRoutines>();
        services.AddScoped<ITodoSource, CareRoutineTodos>();

        return services;
    }
}
