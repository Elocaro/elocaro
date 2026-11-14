using System.Globalization;
using Elocaro.Application.Operations;
using Elocaro.Domain.Operations;
using Elocaro.Web.Components.Common;

namespace Elocaro.Web.Components.Pages.Admin.Operations;

/// <summary>Texts and small rules of the backup page and its parts.</summary>
public static class BackupTexts
{
    /// <summary>The word that has to be typed before a restore replaces the data.</summary>
    public const string RestoreWord = "WIEDERHERSTELLEN";

    public static bool IsConfirmed(string? entry) => string.Equals(entry?.Trim(), RestoreWord, StringComparison.Ordinal);

    public static string Retention(int daily, int weekly, int monthly) => $"{daily} täglich, {weekly} wöchentlich, {monthly} monatlich";

    /// <summary>"täglich um 02:30", "wöchentlich montags um 02:30", "alle 3 Tage um 02:30".</summary>
    public static string Schedule(BackupSettingsView target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var rhythm = target.IntervalDays switch
        {
            1 => "täglich",
            7 => target.Weekday is { } day ? $"wöchentlich {BackupTargetForm.WeekdayPlural(day)}" : "wöchentlich",
            _ => $"alle {target.IntervalDays} Tage",
        };
        return $"{rhythm} um {target.Time.ToString("HH:mm", CultureInfo.InvariantCulture)}";
    }

    public static StatusLevel Chip(BackupState state) => state switch
    {
        BackupState.Ok => StatusLevel.Success,
        BackupState.Failed => StatusLevel.Error,
        BackupState.Outdated => StatusLevel.Warning,
        _ => StatusLevel.Neutral,
    };

    public static string ChipText(BackupState state) => state switch
    {
        BackupState.Ok => "aktuell",
        BackupState.Failed => "fehlgeschlagen",
        BackupState.Outdated => "überfällig",
        _ => "noch keine Sicherung",
    };

    public static NoticeLevel Level(BackupState level) => level switch
    {
        BackupState.Ok => NoticeLevel.Success,
        BackupState.NotConfigured => NoticeLevel.Info,
        _ => NoticeLevel.Error,
    };

    /// <summary>What runs at the moment, for the progress bar of the page.</summary>
    public static string RunningText(BackupProgress progress, IReadOnlyList<BackupSettingsView> targets)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(targets);
        var name = targets.FirstOrDefault(t => t.Id.ToString("N") == progress.RunningTargetId)?.Name;
        return progress.Kind switch
        {
            "Export" => "Die Sicherung wird für den Download vorbereitet …",
            "Delete" => "Eine Sicherung wird gelöscht …",
            "Restore" => "Die Wiederherstellung läuft …",
            _ => name is null ? "Eine Sicherung läuft …" : $"Eine Sicherung läuft: {name} …",
        };
    }

    /// <summary>Identifies a prepared download file (name and creation time).</summary>
    public static string ExportKey(BackupFileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return file.Name + "|" + file.Created.ToString("O", CultureInfo.InvariantCulture);
    }

    /// <summary>What the upload form reports back in the address ("upload=ok" …); <c>null</c> if nothing was uploaded.</summary>
    public static string? UploadMessage(string? result) => result switch
    {
        "ok" => "Die Datei wurde hochgeladen. Geben Sie das Passwort der Sicherung ein, um sie zu öffnen.",
        "abgelehnt" => "Die Datei ist keine Sicherungsdatei von Elocaro. Gültig ist eine .tar, die über „Herunterladen“ entstanden ist.",
        "belegt" => "Es läuft gerade eine Sicherung oder Wiederherstellung. Bitte später noch einmal hochladen.",
        "leer" => "Es wurde keine Datei gewählt.",
        null => null,
        _ => "Die Datei konnte nicht hochgeladen werden. Läuft der Backup-Dienst?",
    };

    /// <summary>Explanation above the target dialog when the target of the server configuration is taken over.</summary>
    public const string TakeOverNotice =
        "Dieses Ziel kommt aus der Server-Konfiguration (deploy/.env). Mit „Speichern“ übernimmt es die App: Ziel, Zeitplan und Aufbewahrung lassen sich dann hier ändern, und die Sicherung nach .env ruht. "
        + "Tragen Sie die Zugangsdaten und das Passwort der bestehenden Sicherung ein; „Verbindung testen“ zeigt, ob sie passen. Die vorhandenen Sicherungen bleiben erhalten.";

    /// <summary>The entry values for taking over the target of the server configuration (.env) into the app; <c>null</c> if its repository is not understood.</summary>
    public static BackupSettingsInput? TakeOver(BackupEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (BackupProviders.Parse(environment.Repository) is not { } parsed)
        {
            return null;
        }

        return new BackupSettingsInput
        {
            Name = "Sicherung",
            Provider = parsed.Provider,
            Location = parsed.Location,
            Time = TimeOnly.TryParseExact(environment.Time, ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : new TimeOnly(2, 30),
            KeepDaily = environment.KeepDaily,
            KeepWeekly = environment.KeepWeekly,
            KeepMonthly = environment.KeepMonthly,
        };
    }
}
