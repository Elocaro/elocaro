using Elocaro.Application.Documents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elocaro.Infrastructure.Pdf;

/// <summary>Registrations of the Pdf module.</summary>
internal static class PdfRegistration
{
    internal static IServiceCollection AddPdfModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PdfOptions>(configuration.GetSection(PdfOptions.Section));
        services.AddHttpClient<GotenbergConverter>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<PdfOptions>>().Value;
            http.BaseAddress = options.GotenbergUrl;
            http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
        services.AddScoped<IPdfGenerator, PdfGenerator>();

        return services;
    }
}
