using MailKit.Security;

namespace MietHahn.Infrastructure.Mail;

/// <summary>Konfigurationsabschnitt <c>Mail</c>.</summary>
public sealed class MailOptionen
{
    public const string Abschnitt = "Mail";

    /// <summary>Absenderadresse, z. B. <c>verwaltung@example.de</c>.</summary>
    public string AbsenderAdresse { get; set; } = "miethahn@localhost";

    public string AbsenderName { get; set; } = "MietHahn";

    /// <summary>Optionale Antwortadresse (z. B. persönliches Postfach des Vermieters).</summary>
    public string? AntwortAn { get; set; }

    /// <summary>Höchstzahl Mails pro Joblauf.</summary>
    public int StapelGroesse { get; set; } = 20;

    public SmtpOptionen Smtp { get; set; } = new();
}

public sealed class SmtpOptionen
{
    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 587;

    /// <summary><c>StartTls</c> (Port 587), <c>SslOnConnect</c> (Port 465), <c>None</c> (nur lokal, z. B. Mailpit), <c>Auto</c>.</summary>
    public SecureSocketOptions Sicherheit { get; set; } = SecureSocketOptions.StartTls;

    public string? Benutzer { get; set; }

    public string? Passwort { get; set; }
}
