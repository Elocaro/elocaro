using Microsoft.Extensions.Logging;

namespace Elocaro.Backup;

/// <summary>Log messages. Deliberately without credentials (they are never part of a message).</summary>
internal static partial class BackupLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Datenbank {Database} sichern")]
    public static partial void DumpingDatabase(ILogger logger, string database);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sicherung in das Repository schreiben")]
    public static partial void WritingBackup(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Alte Sicherungen aufräumen")]
    public static partial void Pruning(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Repository prüfen")]
    public static partial void Checking(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Repository wird angelegt")]
    public static partial void CreatingRepository(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sicherung {Snapshot} aus dem Repository holen")]
    public static partial void FetchingSnapshot(ILogger logger, string snapshot);

    [LoggerMessage(Level = LogLevel.Information, Message = "Datenbank {Database} neu anlegen und einspielen")]
    public static partial void RestoringDatabase(ILogger logger, string database);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dateiablage {Path} ersetzen")]
    public static partial void ReplacingFiles(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Kind}: {Message}")]
    public static partial void Succeeded(ILogger logger, OperationKind kind, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Kind} fehlgeschlagen: {Message}")]
    public static partial void Failed(ILogger logger, OperationKind kind, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Kind} mit unerwartetem Fehler beendet")]
    public static partial void Crashed(ILogger logger, Exception exception, OperationKind kind);

    [LoggerMessage(Level = LogLevel.Error, Message = "Statusdatei {File} konnte nicht geschrieben werden")]
    public static partial void StatusFileFailed(ILogger logger, Exception exception, string file);

    [LoggerMessage(Level = LogLevel.Information, Message = "Kein RESTIC_REPOSITORY gesetzt: Zeitplan und Ziel kommen von der App.")]
    public static partial void NoEnvironmentTarget(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "BACKUP_TIME '{Time}' ist keine Uhrzeit (HH:mm), keine tägliche Sicherung.")]
    public static partial void InvalidTime(ILogger logger, string time);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tägliche Sicherung um {Time} ({Zone})")]
    public static partial void Scheduled(ILogger logger, string time, string zone);

    [LoggerMessage(Level = LogLevel.Information, Message = "Nächste Sicherung in {Minutes} Minuten")]
    public static partial void NextRun(ILogger logger, int minutes);
}
