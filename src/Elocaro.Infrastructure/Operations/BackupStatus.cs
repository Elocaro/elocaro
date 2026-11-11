using System.Globalization;
using System.Text.Json;
using Elocaro.Application.Operations;
using Elocaro.Domain.Common;
using Elocaro.Domain.Operations;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Elocaro.Infrastructure.Operations;

/// <summary>
/// Reads the status files that the backup container writes after every run: <c>backup-&lt;id&gt;.json</c> per target managed in the
/// app, the plain file (<c>Backup:StatusFile</c>) for the backup of the container environment.
/// </summary>
internal sealed class BackupStatus(IOptions<BackupOptions> options, TimeProvider clock, ElocaroDbContext db) : IBackupStatus
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A little more than the interval, so that a run that takes a while is not reported as late.</summary>
    private const int BufferHours = 2;

    public async Task<BackupSnapshot> QueryAsync(CancellationToken cancellationToken = default)
    {
        var file = options.Value.StatusFile;
        if (string.IsNullOrWhiteSpace(file))
        {
            return new BackupSnapshot(BackupState.NotConfigured, "Die Backup-Überwachung ist nicht eingerichtet.");
        }

        var targets = await QueryTargetsAsync(cancellationToken);
        if (targets.Count == 0)
        {
            return await ReadAsync(file, options.Value.MaxAgeHours, cancellationToken);
        }

        var enabled = targets.Where(t => t.Enabled).ToList();
        if (enabled.Count == 0)
        {
            return new BackupSnapshot(BackupState.Unknown, "Alle Sicherungsziele sind ausgeschaltet, es wird nicht gesichert.");
        }

        var worst = enabled.OrderByDescending(t => Severity(t.Status.State)).First();
        if (worst.Status.State == BackupState.Ok)
        {
            var oldest = enabled.Min(t => t.Status.Timestamp);
            return enabled.Count == 1
                ? worst.Status
                : new BackupSnapshot(BackupState.Ok, $"Alle {enabled.Count} Sicherungsziele sind aktuell.", oldest, Snapshot: null);
        }

        return worst.Status with { Description = $"Ziel „{worst.Name}“: {worst.Status.Description}" };
    }

    public async Task<IReadOnlyList<BackupTargetStatus>> QueryTargetsAsync(CancellationToken cancellationToken = default)
    {
        var file = options.Value.StatusFile;
        var rows = await db.Set<BackupSetting>().AsNoTracking().OrderBy(v => v.CreatedAt).ThenBy(v => v.Name).ToListAsync(cancellationToken);
        var directory = string.IsNullOrWhiteSpace(file) ? null : Path.GetDirectoryName(file);
        var result = new List<BackupTargetStatus>();
        foreach (var row in rows)
        {
            var status = directory is null
                ? new BackupSnapshot(BackupState.NotConfigured, "Die Backup-Überwachung ist nicht eingerichtet.")
                : await ReadAsync(Path.Combine(directory, $"backup-{row.Id:N}.json"), (row.IntervalDays * 24) + BufferHours, cancellationToken);
            result.Add(new BackupTargetStatus(row.Id, row.Name, row.Enabled, status));
        }

        return result;
    }

    /// <summary>Worst first: a failure outweighs a late backup, which outweighs a missing one.</summary>
    private static int Severity(BackupState state) => state switch
    {
        BackupState.Failed => 4,
        BackupState.Outdated => 3,
        BackupState.Unknown => 2,
        BackupState.NotConfigured => 1,
        _ => 0,
    };

    private async Task<BackupSnapshot> ReadAsync(string file, int maxAgeHours, CancellationToken cancellationToken)
    {
        if (!File.Exists(file))
        {
            return new BackupSnapshot(BackupState.Unknown, "Es liegt noch keine Sicherung vor.");
        }

        StatusFile? status;
        try
        {
            await using var content = File.OpenRead(file);
            status = await JsonSerializer.DeserializeAsync<StatusFile>(content, Json, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            status = null;
        }

        if (status is null)
        {
            return new BackupSnapshot(BackupState.Unknown, "Der Status der letzten Sicherung ist nicht lesbar.");
        }

        var level = !status.Succeeded ? BackupState.Failed
            : clock.GetUtcNow() - status.Timestamp > TimeSpan.FromHours(maxAgeHours) ? BackupState.Outdated
            : BackupState.Ok;
        var time = TimeZoneInfo.ConvertTime(status.Timestamp, GermanTime.Germany).ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("de-DE"));
        var description = level switch
        {
            BackupState.Failed => $"Die Sicherung am {time} ist fehlgeschlagen.",
            BackupState.Outdated => $"Die letzte Sicherung ist vom {time}. Läuft der Backup-Container?",
            _ => $"Letzte Sicherung am {time} erfolgreich.",
        };
        return new BackupSnapshot(level, description, status.Timestamp, status.Message, NullIfEmpty(status.Snapshot), status.DurationSeconds);
    }

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private sealed record StatusFile(DateTimeOffset Timestamp, bool Succeeded, string? Message, string? Snapshot, int? DurationSeconds);
}
