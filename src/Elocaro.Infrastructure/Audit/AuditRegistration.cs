using Elocaro.Application.Audit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Audit;

/// <summary>Registrations of the Audit module.</summary>
internal static class AuditRegistration
{
    internal static IServiceCollection AddAuditModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuditOptions>(configuration.GetSection(AuditOptions.Section));
        services.AddScoped<IAccessLog, AccessLog>();
        services.AddScoped<IChangeHistory, ChangeHistory>();
        services.AddSingleton<IOperationScope, OperationScope>();
        services.AddScoped<IActivityLog, ActivityLog>();
        services.AddScoped<IOperationUndo, OperationUndo>();
        services.AddScoped<AuditCleanup>();
        services.AddScoped<AuditWriter>();

        return services;
    }
}
