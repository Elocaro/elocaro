namespace Elocaro.Web.Components.Account.Login;

/// <summary>Texts of the restore during the initial setup.</summary>
public static class RestoreSetupText
{
    /// <summary>What the upload form reports back in the address ("upload=ok" …); <c>null</c> if nothing was uploaded.</summary>
    public static string? UploadMessage(string? result) => result switch
    {
        "ok" => "Die Datei wurde hochgeladen.",
        "code" => "Der Einrichtungscode stimmt nicht.",
        "abgelehnt" => "Die Datei ist keine Sicherungsdatei von Elocaro. Gültig ist eine .tar, die über „Herunterladen“ entstanden ist.",
        "belegt" => "Es läuft gerade eine Sicherung oder Wiederherstellung. Bitte später noch einmal hochladen.",
        "leer" => "Es wurde keine Datei gewählt.",
        null => null,
        _ => "Die Datei konnte nicht hochgeladen werden. Läuft der Backup-Dienst?",
    };
}

/// <summary>Where the restore stands: choose the source, pick a backup, running.</summary>
public enum RestorePhase
{
    Target,
    Choose,
    Running,
}

/// <summary>Where the backup comes from: a target (storage) or an uploaded file.</summary>
public enum RestoreSource
{
    Target,
    File,
}
