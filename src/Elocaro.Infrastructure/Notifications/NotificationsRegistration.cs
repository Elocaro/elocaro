using Elocaro.Application.Communication;
using Elocaro.Application.Notifications;
using Elocaro.Infrastructure.Accounts;
using Elocaro.Infrastructure.Communication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Notifications;

/// <summary>Registrations of the Notifications module.</summary>
internal static class NotificationsRegistration
{
    internal static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IMailSender>(sp => new Notifications.PushingMailSender(sp.GetRequiredService<MailSender>(), sp.GetRequiredService<IPushNotifications>(), sp.GetRequiredService<UserManager<ApplicationUser>>()));
        services.Configure<Notifications.PushOptions>(configuration.GetSection(Notifications.PushOptions.Section));
        services.AddHttpClient(Notifications.PushNotifications.ClientName, http => http.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<IPushNotifications, Notifications.PushNotifications>();

        return services;
    }
}
