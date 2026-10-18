using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;

namespace MietHahn.Infrastructure.Mail;

/// <summary>Kapselt den SMTP-Versand (austauschbar in Tests).</summary>
internal interface ISmtpVersender
{
    Task SendenAsync(IReadOnlyList<MimeMessage> nachrichten, Func<MimeMessage, Exception?, Task> ergebnis, CancellationToken cancellationToken);
}

/// <summary>Versendet einen Stapel Nachrichten über eine SMTP-Verbindung (MailKit).</summary>
internal sealed class MailKitSmtpVersender(IOptions<MailOptionen> optionen) : ISmtpVersender
{
    public async Task SendenAsync(IReadOnlyList<MimeMessage> nachrichten, Func<MimeMessage, Exception?, Task> ergebnis, CancellationToken cancellationToken)
    {
        if (nachrichten.Count == 0)
        {
            return;
        }

        var smtp = optionen.Value.Smtp;
        using var client = new SmtpClient { Timeout = (int)TimeSpan.FromSeconds(30).TotalMilliseconds };

        try
        {
            await client.ConnectAsync(smtp.Host, smtp.Port, smtp.Sicherheit, cancellationToken);
            if (!string.IsNullOrEmpty(smtp.Benutzer))
            {
                await client.AuthenticateAsync(smtp.Benutzer, smtp.Passwort ?? "", cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Verbindung oder Anmeldung gescheitert: alle Nachrichten dieses Stapels gelten als Fehlversuch.
            foreach (var nachricht in nachrichten)
            {
                await ergebnis(nachricht, ex);
            }

            return;
        }

        foreach (var nachricht in nachrichten)
        {
            try
            {
                await client.SendAsync(nachricht, cancellationToken);
                await ergebnis(nachricht, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await ergebnis(nachricht, ex);
            }
        }

        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
