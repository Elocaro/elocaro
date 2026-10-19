using MailKit.Security;

namespace MietHahn.Infrastructure.Mail;

/// <summary>Konfigurationsabschnitt <c>Mail</c>.</summary>
public sealed class MailOptions
{
    public const string Section = "Mail";

    /// <summary>Absenderadresse, z. B. <c>verwaltung@example.de</c>.</summary>
    public string SenderAddress { get; set; } = "miethahn@localhost";

    public string SenderName { get; set; } = "MietHahn";

    /// <summary>Optionale Antwortadresse (z. B. persönliches Postfach des Vermieters).</summary>
    public string? ReplyTo { get; set; }

    /// <summary>Höchstzahl Mails pro Joblauf.</summary>
    public int BatchSize { get; set; } = 20;

    public SmtpOptions Smtp { get; set; } = new();
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 587;

    /// <summary><c>StartTls</c> (Port 587), <c>SslOnConnect</c> (Port 465), <c>None</c> (nur lokal, z. B. Mailpit), <c>Auto</c>.</summary>
    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.StartTls;

    public string? UserName { get; set; }

    public string? Password { get; set; }
}
