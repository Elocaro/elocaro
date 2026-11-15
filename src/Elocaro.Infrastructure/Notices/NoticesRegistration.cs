using Elocaro.Application.Files;
using Elocaro.Application.Notices;
using Elocaro.Application.Todos;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Notices;

/// <summary>Registrations of the Notices module.</summary>
internal static class NoticesRegistration
{
    internal static IServiceCollection AddNoticesModule(this IServiceCollection services)
    {
        services.AddScoped<INotices, NoticeService>();
        services.AddScoped<IMessages, MessageService>();
        services.AddScoped<IFileShare, NoticeFileShare>();
        services.AddScoped<ITodoSource, MessageTodos>();
        services.AddScoped<ITodoSource, NoticeAcknowledgementTodos>();
        services.AddScoped<NoticeNotifications>();

        return services;
    }
}
