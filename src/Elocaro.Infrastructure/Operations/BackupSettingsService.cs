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

/// <summary>The target for the backup container, built from a saved target and, if given, an entry not yet saved.</summary>
internal sealed record ResolvedBackupTarget(BackupRepositoryTarget? Target, IReadOnlyList<string> Errors);

internal interface IBackupTargets
{
    /// <param name="id">The saved target (its secrets fill the empty ones of <paramref name="draft"/>); <c>null</c> for a new one.</param>
    /// <param name="draft">Entry of the page. <c>null</c>: use the saved target as it is.</param>
    Task<ResolvedBackupTarget> ResolveAsync(Guid? id, BackupSettingsInput? draft, CancellationToken cancellationToken = default);
}

internal sealed class BackupSettingsService(ElocaroDbContext db, IDataProtectionProvider protection, BackupSidecar sidecar) : IBackupSettings, IBackupTargets
{
    internal const string ProtectionPurpose = "Elocaro.Backup.Settings";

    private IDataProtector Protector => protection.CreateProtector(ProtectionPurpose);

    public async Task<IReadOnlyList<BackupSettingsView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.Set<BackupSetting>().AsNoTracking().OrderBy(v => v.CreatedAt).ThenBy(v => v.Name).ToListAsync(cancellationToken);
        return [.. rows.Select(ToView)];
    }

    public async Task<BackupSettingsView?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await db.Set<BackupSetting>().AsNoTracking().SingleOrDefaultAsync(v => v.Id == id, cancellationToken);
        return row is null ? null : ToView(row);
    }

    public Task<Result> SaveAsync(Guid? id, BackupSettingsInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var row = id is { } given
                ? await db.Set<BackupSetting>().SingleOrDefaultAsync(v => v.Id == given, cancellationToken) ?? throw new NotFoundException()
                : null;

            var name = input.Name?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                throw new RuleViolationException("Bitte einen Namen für das Ziel angeben.");
            }

            var others = await db.Set<BackupSetting>().AsNoTracking().Where(v => row == null || v.Id != row.Id).Select(v => v.Name).ToListAsync(cancellationToken);
            if (others.Any(other => string.Equals(other, name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new RuleViolationException("Es gibt bereits ein Ziel mit diesem Namen.");
            }

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

            row.Name = name;
            row.Provider = input.Provider;
            row.Location = input.Location.Trim();
            row.Enabled = input.Enabled;
            row.Time = input.Time;
            row.IntervalDays = input.IntervalDays;
            row.Weekday = input.IntervalDays == 7 ? input.Weekday : null;
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

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var row = await db.Set<BackupSetting>().SingleOrDefaultAsync(v => v.Id == id, cancellationToken) ?? throw new NotFoundException();
            db.Remove(row);
            await db.SaveChangesAsync(cancellationToken);

            // Without a target of its own the app has nothing to schedule: the container's own schedule applies again.
            if (!await db.Set<BackupSetting>().AnyAsync(cancellationToken))
            {
                await sidecar.SetManagedAsync(false, cancellationToken);
            }

            return id;
        });

    public async Task<ResolvedBackupTarget> ResolveAsync(Guid? id, BackupSettingsInput? draft, CancellationToken cancellationToken = default)
    {
        var row = id is { } given ? await db.Set<BackupSetting>().AsNoTracking().SingleOrDefaultAsync(v => v.Id == given, cancellationToken) : null;
        if (draft is null && row is null)
        {
            return new ResolvedBackupTarget(null, ["Dieses Ziel für die Sicherung gibt es nicht (mehr)."]);
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

    private BackupSettingsView ToView(BackupSetting row) =>
        new(row.Id, row.Name, row.Provider, row.Location, row.Enabled, row.Time, row.IntervalDays, row.KeepDaily, row.KeepWeekly, row.KeepMonthly,
            Unprotect(row.ProtectedPassword) is { Length: > 0 }, Credentials(row).Keys.ToHashSet(StringComparer.Ordinal), row.Weekday);

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
