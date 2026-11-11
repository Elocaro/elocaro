using System.Security.Cryptography;
using Elocaro.Application.Common;
using Elocaro.Application.Operations;
using Elocaro.Domain.Common;
using Elocaro.Domain.Operations;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Elocaro.Infrastructure.Operations;

/// <summary>The OAuth client used for the sign-in: the one entered in the app, otherwise the one of the environment.</summary>
internal interface IGoogleClientSource
{
    Task<(string? ClientId, string? ClientSecret)> ResolveAsync(CancellationToken cancellationToken = default);
}

internal sealed class GoogleClientSettingsService(ElocaroDbContext db, IDataProtectionProvider protection, IOptions<GoogleDriveOptions> options) : IGoogleClientSettings, IGoogleClientSource
{
    internal const string ProtectionPurpose = "Elocaro.Backup.GoogleClient";

    private IDataProtector Protector => protection.CreateProtector(ProtectionPurpose);

    public async Task<GoogleClientView> GetAsync(CancellationToken cancellationToken = default)
    {
        var (id, secret) = await ResolveAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret))
        {
            return new GoogleClientView(GoogleClientSource.None, null);
        }

        var inApp = await db.Set<GoogleClientSetting>().AsNoTracking().AnyAsync(cancellationToken);
        return new GoogleClientView(inApp ? GoogleClientSource.App : GoogleClientSource.Environment, id);
    }

    public Task<Result> SaveAsync(string clientId, string? clientSecret, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var row = await db.Set<GoogleClientSetting>().SingleOrDefaultAsync(cancellationToken);
            var secret = string.IsNullOrWhiteSpace(clientSecret) ? (row is null ? null : Unprotect(row.ProtectedClientSecret)) : clientSecret.Trim();
            if (string.IsNullOrEmpty(secret))
            {
                throw new RuleViolationException("Der Client-Schlüssel fehlt.");
            }

            if (row is null)
            {
                row = new GoogleClientSetting();
                db.Add(row);
            }

            row.ClientId = clientId?.Trim() ?? string.Empty;
            row.Validate();
            row.ProtectedClientSecret = Protector.Protect(secret);
            await db.SaveChangesAsync(cancellationToken);
            return row.Id;
        });

    public Task<Result> RemoveAsync(CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var row = await db.Set<GoogleClientSetting>().SingleOrDefaultAsync(cancellationToken) ?? throw new NotFoundException();
            db.Remove(row);
            await db.SaveChangesAsync(cancellationToken);
            return row.Id;
        });

    public async Task<(string? ClientId, string? ClientSecret)> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var row = await db.Set<GoogleClientSetting>().AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (row is not null && Unprotect(row.ProtectedClientSecret) is { Length: > 0 } secret)
        {
            return (row.ClientId, secret);
        }

        return (options.Value.ClientId, options.Value.ClientSecret);
    }

    /// <summary>An unreadable value (keys lost) counts as missing.</summary>
    private string? Unprotect(string value)
    {
        try
        {
            return string.IsNullOrEmpty(value) ? null : Protector.Unprotect(value);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
