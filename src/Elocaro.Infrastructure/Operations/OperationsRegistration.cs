using Elocaro.Application.Operations;
using Elocaro.Application.Todos;
using Elocaro.Infrastructure.Statements;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elocaro.Infrastructure.Operations;

/// <summary>Registrations of the Operations module.</summary>
internal static class OperationsRegistration
{
    internal static IServiceCollection AddOperationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StatementOptions>(configuration.GetSection(StatementOptions.Section));
        services.Configure<BackupOptions>(configuration.GetSection(BackupOptions.Section));
        services.AddScoped<IBackupStatus, BackupStatus>();
        static void ConfigureSidecar(IServiceProvider sp, HttpClient http, TimeSpan timeout)
        {
            var backup = sp.GetRequiredService<IOptions<BackupOptions>>().Value;
            if (Uri.TryCreate(backup.Url, UriKind.Absolute, out var url))
            {
                http.BaseAddress = url;
            }

            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", backup.ApiToken ?? string.Empty);
            http.Timeout = timeout;
        }

        // Testing a slow target and listing its backups can take a while.
        services.AddHttpClient<BackupSidecar>((sp, http) => ConfigureSidecar(sp, http, TimeSpan.FromSeconds(120)));
        // Handing a backup file in or fetching it takes as long as the file is big.
        services.AddHttpClient(BackupSidecar.TransferClient, (sp, http) => ConfigureSidecar(sp, http, Timeout.InfiniteTimeSpan));
        services.Configure<GoogleDriveOptions>(configuration.GetSection(GoogleDriveOptions.Section));
        services.AddScoped<GoogleClientSettingsService>();
        services.AddScoped<IGoogleClientSettings>(sp => sp.GetRequiredService<GoogleClientSettingsService>());
        services.AddScoped<IGoogleClientSource>(sp => sp.GetRequiredService<GoogleClientSettingsService>());
        services.AddHttpClient<IGoogleDriveAuthorization, GoogleDriveAuthorization>(http => http.Timeout = TimeSpan.FromSeconds(20));
        services.AddScoped<BackupSettingsService>();
        services.AddScoped<IBackupSettings>(sp => sp.GetRequiredService<BackupSettingsService>());
        services.AddScoped<IBackupTargets>(sp => sp.GetRequiredService<BackupSettingsService>());
        services.AddScoped<IBackupControl, BackupControl>();
        services.AddScoped<ITodoSource, BackupTodos>();
        services.AddSingleton<UpdateState>();
        services.AddSingleton<IUpdateInfo, UpdateInfoService>();
        services.AddHttpClient<UpdateChecker>(http =>
        {
            http.Timeout = TimeSpan.FromSeconds(20);
            // generic client name: nothing about the installation is sent
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Elocaro-UpdateCheck");
        });
        services.AddScoped<ITodoSource, UpdateTodos>();
        services.AddScoped<BackupWarning>();
        services.AddScoped<IDataExport, DataExport>();
        services.AddScoped<MailRetention>();

        return services;
    }
}
