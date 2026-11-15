using Elocaro.Application.People;
using Elocaro.Application.Receipts;
using Elocaro.Application.Todos;
using Elocaro.Infrastructure.Receipts.Extraction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elocaro.Infrastructure.Receipts;

/// <summary>Registrations of the Receipts module.</summary>
internal static class ReceiptsRegistration
{
    internal static IServiceCollection AddReceiptsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ReceiptExtractionOptions>(configuration.GetSection(ReceiptExtractionOptions.Section));
        services.Configure<ReceiptInboxOptions>(configuration.GetSection(ReceiptInboxOptions.Section));
        services.AddHttpClient<ClaudeReceiptExtractor>().ConfigureHttpClient(http => http.Timeout = Timeout.InfiniteTimeSpan);
        services.AddHttpClient<OllamaReceiptExtractor>().ConfigureHttpClient(http => http.Timeout = Timeout.InfiniteTimeSpan);
        services.AddScoped<NoReceiptExtractor>();
        services.AddScoped<IReceiptExtractor>(sp => sp.GetRequiredService<IOptions<ReceiptExtractionOptions>>().Value.ProviderKind switch
        {
            ExtractionProvider.Claude => sp.GetRequiredService<ClaudeReceiptExtractor>(),
            ExtractionProvider.Ollama => sp.GetRequiredService<OllamaReceiptExtractor>(),
            _ => sp.GetRequiredService<NoReceiptExtractor>(),
        });
        services.AddScoped<IReceiptExtraction, ReceiptExtraction>();
        services.AddScoped<IReceiptImport, ReceiptImport>();
        services.AddScoped<IReceiptService, ReceiptService>();
        services.AddScoped<ReceiptInbox>();
        services.AddScoped<ITodoSource, ReceiptTodos>();
        services.AddScoped<IPersonalDataSource, ReceiptPersonalData>();

        return services;
    }
}
