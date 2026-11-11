using System.Security.Cryptography;
using System.Text.Json;
using Elocaro.Application.Common;
using Elocaro.Application.Operations;
using Elocaro.Domain.Common;
using Elocaro.Domain.Operations;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Operations;

/// <summary>The target for the backup container, built from saved settings and, if given, an entry not yet saved.</summary>
internal sealed record ResolvedBackupTarget(BackupRepositoryTarget? Target, IReadOnlyList<string> Errors);

internal interface IBackupTargets
{
    /// <param name="draft">Entry of the page; empty secrets are taken from the saved settings. <c>null</c>: only the saved settings.</param>
    Task<ResolvedBackupTarget> ResolveAsync(BackupSettingsInput? draft, CancellationToken cancellationToken = default);
}

internal sealed class BackupSettingsService(ElocaroDbContext db, IDataProtectionProvider protection, BackupSidecar sidecar) : IBackupSettings, IBackupTargets
{
    internal const string ProtectionPurpose = "Elocaro.Backup.Settings";

    private IDataProtector Protector => protection.CreateProtector(ProtectionPurpose);

    public async Task<BackupSettingsView?> GetAsync(CancellationToken cancellationToken = default)
    {
        var row = await db.Set<BackupSetting>().AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var credentials = Credentials(row);
        return new BackupSettingsView(row.Provider, row.Location, row.Enabled, row.Time, row.KeepDaily, row.KeepWeekly, row.KeepMonthly,
            Unprotect(row.ProtectedPassword) is { Length: > 0 }, credentials.Keys.ToHashSet(StringComparer.Ordinal));
    }

    public Task<Result> SaveAsync(BackupSettingsInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var row = await db.Set<BackupSetting>().SingleOrDefaultAsync(cancellationToken);
            var (password, credentials) = Merge(row, input);
            var errors = BackupProviders.Validate(input.Provider, input.Location, credentials, password);
            if (errors.Count > 0)
            {
                throw new RuleViolationException(string.Join(' ', errors));
            }

            if (row is null)
            {
                row = new BackupSetting();
                db.Add(row);
            }

            row.Provider = input.Provider;
            row.Location = input.Location.Trim();
            row.Enabled = input.Enabled;
            row.Time = input.Time;
            row.KeepDaily = input.KeepDaily;
            row.KeepWeekly = input.KeepWeekly;
            row.KeepMonthly = input.KeepMonthly;
            row.ProtectedPassword = Protector.Protect(password!);
            row.ProtectedCredentials = credentials.Count == 0 ? null : Protector.Protect(JsonSerializer.Serialize(credentials));
            row.Validate();
            await db.SaveChangesAsync(cancellationToken);

            // From now on the app schedules the backups; the daily backup from the environment of the container pauses.
            // If the container is not reachable right now, the job repeats this.
            await sidecar.SetManagedAsync(true, cancellationToken);
            return row.Id;
        });

    public async Task<ResolvedBackupTarget> ResolveAsync(BackupSettingsInput? draft, CancellationToken cancellationToken = default)
    {
        var row = await db.Set<BackupSetting>().AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (draft is null && row is null)
        {
            return new ResolvedBackupTarget(null, ["Es ist noch kein Ziel für die Sicherung eingerichtet."]);
        }

        var input = draft ?? new BackupSettingsInput
        {
            Provider = row!.Provider,
            Location = row.Location,
            KeepDaily = row.KeepDaily,
            KeepWeekly = row.KeepWeekly,
            KeepMonthly = row.KeepMonthly,
        };
        var (password, credentials) = Merge(row, input);
        var errors = BackupProviders.Validate(input.Provider, input.Location, credentials, password);
        return errors.Count > 0
            ? new ResolvedBackupTarget(null, errors)
            : new ResolvedBackupTarget(BackupProviders.Build(input.Provider, input.Location, credentials, password!, new BackupRetention(input.KeepDaily, input.KeepWeekly, input.KeepMonthly)), []);
    }

    /// <summary>Entered values win; empty secrets keep the saved ones (only for the same provider).</summary>
    private (string? Password, Dictionary<string, string> Credentials) Merge(BackupSetting? saved, BackupSettingsInput input)
    {
        var password = string.IsNullOrEmpty(input.Password) ? (saved is null ? null : Unprotect(saved.ProtectedPassword)) : input.Password;
        var storedCredentials = saved is not null && saved.Provider == input.Provider ? Credentials(saved) : [];
        var credentials = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in BackupProviders.Fields(input.Provider))
        {
            if (input.Credentials.GetValueOrDefault(field.Key) is { Length: > 0 } entered && !string.IsNullOrWhiteSpace(entered))
            {
                credentials[field.Key] = entered;
            }
            else if (storedCredentials.TryGetValue(field.Key, out var stored))
            {
                credentials[field.Key] = stored;
            }
        }

        return (password, credentials);
    }

    private Dictionary<string, string> Credentials(BackupSetting row)
    {
        if (string.IsNullOrEmpty(row.ProtectedCredentials) || Unprotect(row.ProtectedCredentials) is not { } json)
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Unreadable values (keys lost, e.g. after a restore without the certificate) count as missing.</summary>
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
