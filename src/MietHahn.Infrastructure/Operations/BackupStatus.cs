using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MietHahn.Application.Operations;
using MietHahn.Domain.Common;

namespace MietHahn.Infrastructure.Operations;

/// <summary>Reads the status file that <c>deploy/backup/miethahn-backup.sh</c> writes after every run.</summary>
internal sealed class BackupStatus(IOptions<BackupOptions> options, TimeProvider clock) : IBackupStatus
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<BackupSnapshot> QueryAsync(CancellationToken cancellationToken = default)
    {
        var file = options.Value.StatusFile;
        if (string.IsNullOrWhiteSpace(file))
        {
            return new BackupSnapshot(BackupState.NotConfigured, "Die Backup-Überwachung ist nicht eingerichtet.");
        }

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
            : clock.GetUtcNow() - status.Timestamp > TimeSpan.FromHours(options.Value.MaxAgeHours) ? BackupState.Outdated
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
