using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Elocaro.Application.Accounts;
using Elocaro.Application.Admin;
using Elocaro.Application.Audit;
using Elocaro.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Accounts;

/// <summary>Registrations of the Identity module.</summary>
internal static class AccountsRegistration
{
    internal static IServiceCollection AddAccountsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        var dataProtection = services.AddDataProtection()
            // The name takes part in deriving the keys: changing it makes everything protected so far unreadable.
            .SetApplicationName("Elocaro")
            .PersistKeysToDbContext<ElocaroDbContext>();

        // Encrypts new keys only; keys already stored without encryption stay as they are and stay readable.
        if (KeyCertificate(configuration) is { } certificate)
        {
            dataProtection.ProtectKeysWithCertificate(certificate);
        }

        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.SignIn.RequireConfirmedAccount = false;

                // Length instead of character classes (NIST SP 800-63B).
                o.Password.RequiredLength = 12;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;

                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<Role>()
            .AddEntityFrameworkStores<ElocaroDbContext>()
            .AddSignInManager<ElocaroSignInManager>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<InvitationTokenProvider>(TokenProviders.Invitation)
            .AddClaimsPrincipalFactory<ElocaroClaimsFactory>()
            .AddErrorDescriber<GermanIdentityErrorDescriber>();

        // Password reset links: 2 hours; invitations use their own provider (7 days).
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(2));
        services.AddScoped<IAccountService, AccountService>();
        services.AddSingleton<ISetupCode, SetupCode>();
        services.AddScoped<IUserManagementService, UserManagement>();
        services.Configure<AccessTokenOptions>(configuration.GetSection(AccessTokenOptions.Section));
        services.Configure<McpOptions>(configuration.GetSection(McpOptions.Section));
        services.AddScoped<IAccessTokens, AccessTokens>();
        services.AddScoped<IOptionUsageSource, OwnerOptionUsage>();

        services.AddScoped<ISecurityAudit, SecurityAudit>();

        return services;
    }

    /// <summary>The optional certificate protecting the data protection keys; a configured but unusable one stops the start.</summary>
    private static X509Certificate2? KeyCertificate(IConfiguration configuration)
    {
        var path = configuration["DataProtection:CertificatePath"];
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Zertifikat für den Schlüsselschutz fehlt: Die Datei '{path}' (DataProtection:CertificatePath) existiert nicht.");
        }

        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadPkcs12FromFile(path, configuration["DataProtection:CertificatePassword"]);
        }
        catch (CryptographicException e)
        {
            throw new InvalidOperationException($"Zertifikat für den Schlüsselschutz lässt sich nicht öffnen: '{path}' (Dateirechte und Passwort in DataProtection:CertificatePassword prüfen).", e);
        }

        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new InvalidOperationException($"Zertifikat für den Schlüsselschutz enthält keinen privaten Schlüssel: '{path}'.");
        }

        return certificate;
    }
}
