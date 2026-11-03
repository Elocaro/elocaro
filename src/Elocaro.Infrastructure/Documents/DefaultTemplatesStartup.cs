using Elocaro.Application.Documents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Documents;

public static class DefaultTemplatesStartup
{
    /// <summary>
    /// Creates the bundled templates that are missing, so nobody has to ask for them. Templates that were deleted on purpose stay deleted.
    /// Switched off with <c>Documents:InstallDefaults=false</c> (tests that start without templates).
    /// </summary>
    public static async Task InstallDefaultTemplatesAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        if (!scope.ServiceProvider.GetRequiredService<IConfiguration>().GetValue("Documents:InstallDefaults", true))
        {
            return;
        }

        await scope.ServiceProvider.GetRequiredService<IDocumentTemplates>().EnsureDefaultsAsync(cancellationToken);
    }
}
