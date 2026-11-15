using Elocaro.Application.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Files;

/// <summary>Registrations of the Files module.</summary>
internal static class FilesRegistration
{
    internal static IServiceCollection AddFilesModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FileStoreOptions>(configuration.GetSection(FileStoreOptions.Section));
        services.AddScoped<IFileStore, LocalFileStore>();

        return services;
    }
}
