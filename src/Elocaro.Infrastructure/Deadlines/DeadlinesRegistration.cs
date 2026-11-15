using Elocaro.Application.Deadlines;
using Elocaro.Application.Todos;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Deadlines;

/// <summary>Registrations of the Deadlines module.</summary>
internal static class DeadlinesRegistration
{
    internal static IServiceCollection AddDeadlinesModule(this IServiceCollection services)
    {
        services.AddScoped<IDeadlineSource, StatementDeadlineSource>();
        services.AddScoped<IDeadlineSource, LeaseDeadlineSource>();
        services.AddScoped<IDeadlineSource, LoanDeadlineSource>();
        services.AddScoped<IDeadlineSource, CalibrationDeadlineSource>();
        services.AddScoped<IDeadlineSource, RemoteReadingDeadlineSource>();
        services.AddScoped<IDeadlineSource, ReadingRoundDeadlineSource>();
        services.AddScoped<IDeadlineSource, DocumentDeadlineSource>();
        services.AddScoped<IDeadlineSource, TaskDeadlineSource>();
        services.AddScoped<DeadlineService>();
        services.AddScoped<IDeadlines>(sp => sp.GetRequiredService<DeadlineService>());
        services.AddScoped<DeadlineReminders>();
        services.AddScoped<ITodoSource, DeadlineTodos>();

        return services;
    }
}
