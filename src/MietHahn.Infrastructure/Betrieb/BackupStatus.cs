using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MietHahn.Application.Betrieb;
using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Infrastructure.Betrieb;

/// <summary>Liest die Statusdatei, die <c>deploy/backup/miethahn-backup.sh</c> nach jedem Lauf schreibt.</summary>
internal sealed class BackupStatus(IOptions<BackupOptionen> optionen, TimeProvider uhr) : IBackupStatus
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<BackupZustand> AbfragenAsync(CancellationToken cancellationToken = default)
    {
        var datei = optionen.Value.StatusDatei;
        if (string.IsNullOrWhiteSpace(datei))
        {
            return new BackupZustand(BackupLage.NichtEingerichtet, "Die Backup-Überwachung ist nicht eingerichtet.");
        }

        if (!File.Exists(datei))
        {
            return new BackupZustand(BackupLage.Unbekannt, "Es liegt noch keine Sicherung vor.");
        }

        StatusDatei? status;
        try
        {
            await using var inhalt = File.OpenRead(datei);
            status = await JsonSerializer.DeserializeAsync<StatusDatei>(inhalt, Json, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            status = null;
        }

        if (status is null)
        {
            return new BackupZustand(BackupLage.Unbekannt, "Der Status der letzten Sicherung ist nicht lesbar.");
        }

        var lage = !status.Erfolgreich ? BackupLage.Fehlgeschlagen
            : uhr.GetUtcNow() - status.Zeitpunkt > TimeSpan.FromHours(optionen.Value.MaxAlterStunden) ? BackupLage.Veraltet
            : BackupLage.Ok;
        var zeit = TimeZoneInfo.ConvertTime(status.Zeitpunkt, Zeitzone.Deutschland).ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("de-DE"));
        var beschreibung = lage switch
        {
            BackupLage.Fehlgeschlagen => $"Die Sicherung am {zeit} ist fehlgeschlagen.",
            BackupLage.Veraltet => $"Die letzte Sicherung ist vom {zeit}. Läuft der Backup-Container?",
            _ => $"Letzte Sicherung am {zeit} erfolgreich.",
        };
        return new BackupZustand(lage, beschreibung, status.Zeitpunkt, status.Meldung, NullWennLeer(status.Snapshot), status.DauerSekunden);
    }

    private static string? NullWennLeer(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private sealed record StatusDatei(DateTimeOffset Zeitpunkt, bool Erfolgreich, string? Meldung, string? Snapshot, int? DauerSekunden);
}
