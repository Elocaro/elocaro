using Elocaro.Application.Payments;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Payments;

/// <summary>Registrations of the Payments module.</summary>
internal static class PaymentsRegistration
{
    internal static IServiceCollection AddPaymentsModule(this IServiceCollection services)
    {
        services.AddScoped<IPaymentOrders, PaymentOrders>();
        services.AddScoped<IDirectDebitMandates, DirectDebitMandates>();
        services.AddScoped<IDirectDebits, DirectDebits>();
        return services;
    }
}
