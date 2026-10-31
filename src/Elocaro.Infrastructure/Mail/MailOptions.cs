using MailKit.Security;

namespace Elocaro.Infrastructure.Mail;

/// <summary>Configuration section <c>Mail</c>.</summary>
public sealed class MailOptions
{
    public const string Section = "Mail";

    /// <summary>Sender address, e.g. <c>verwaltung@example.de</c>.</summary>
    public string SenderAddress { get; set; } = "elocaro@localhost";

    public string SenderName { get; set; } = "Elocaro";

    /// <summary>Optional reply-to address (e.g. the landlord's personal mailbox).</summary>
    public string? ReplyTo { get; set; }

    /// <summary>Maximum number of mails per job run.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>Days after which sent or failed mails are removed; 0 or less keeps them forever.</summary>
    public int RetentionDays { get; set; } = 1095;

    public SmtpOptions Smtp { get; set; } = new();
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 587;

    /// <summary><c>StartTls</c> (Port 587), <c>SslOnConnect</c> (Port 465), <c>None</c> (local only, e.g. Mailpit), <c>Auto</c>.</summary>
    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.StartTls;

    public string? UserName { get; set; }

    public string? Password { get; set; }
}
