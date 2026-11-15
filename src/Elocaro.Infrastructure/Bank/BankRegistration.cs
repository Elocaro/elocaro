using Elocaro.Application.Bank;
using Elocaro.Application.Todos;
using Elocaro.Infrastructure.Bank.FinTs;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Bank;

/// <summary>Registrations of the Bank module.</summary>
internal static class BankRegistration
{
    internal static IServiceCollection AddBankModule(this IServiceCollection services)
    {
        services.AddScoped<IBankService, BankService>();
        services.AddSingleton<FinTsSessions>();
        services.AddScoped<IFinTsService, FinTsService>();
        services.AddScoped<IPaymentAssignment, PaymentAssignment>();
        services.AddScoped<IMatchRules, MatchRules>();
        services.AddScoped<ITodoSource, BankTodos>();

        return services;
    }
}
