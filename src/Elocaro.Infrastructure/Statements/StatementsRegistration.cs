using Elocaro.Application.Statements;
using Elocaro.Application.Todos;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Statements;

/// <summary>Registrations of the Statements module.</summary>
internal static class StatementsRegistration
{
    internal static IServiceCollection AddStatementsModule(this IServiceCollection services)
    {
        services.AddScoped<IStatements, StatementService>();
        services.AddScoped<IStatementDispatch, StatementDispatchService>();
        services.AddScoped<IStatementReceiptShares, StatementReceiptShareService>();
        services.AddScoped<ITenantStatements, TenantStatementService>();
        services.AddScoped<ITodoSource, StatementDeadlineTodos>();

        return services;
    }
}
