using Elocaro.Application.Communication;
using Elocaro.Application.Todos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elocaro.Infrastructure.Communication;

/// <summary>Registrations of the Mail module.</summary>
internal static class CommunicationRegistration
{
    internal static IServiceCollection AddCommunicationModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MailOptions>(configuration.GetSection(MailOptions.Section));
        services.AddScoped<MailSender>();
        services.TryAddScoped<ISmtpSender, MailKitSmtpSender>();
        services.AddScoped<MailOutbox>();
        services.AddScoped<IMailService, MailService>();
        services.AddScoped<ITodoSource, MailTodos>();

        return services;
    }
}
