using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MietHahn.Application.Dateien;
using MietHahn.Domain.Kommunikation;
using MietHahn.Infrastructure.Persistenz;
using MimeKit;

namespace MietHahn.Infrastructure.Mail;

/// <summary>Stellt fällige Mails aus der Outbox zu, mit Wiederholung und Backoff bei Fehlern.</summary>
internal sealed partial class MailOutbox(
    MietHahnDbContext db,
    ISmtpVersender smtp,
    IDateiablage dateiablage,
    TimeProvider uhr,
    IOptions<MailOptionen> optionen,
    ILogger<MailOutbox> logger)
{
    /// <summary>Verarbeitet einen Stapel fälliger Mails und liefert die Anzahl erfolgreich gesendeter.</summary>
    public async Task<int> ZustellenAsync(CancellationToken cancellationToken = default)
    {
        var jetzt = uhr.GetUtcNow();
        var faellig = await db.Set<AusgehendeMail>()
            .Where(m => m.Status == MailStatus.Ausstehend && m.NaechsterVersuchAm <= jetzt)
            .OrderBy(m => m.NaechsterVersuchAm)
            .Take(optionen.Value.StapelGroesse)
            .ToListAsync(cancellationToken);

        if (faellig.Count == 0)
        {
            return 0;
        }

        var gesendet = 0;
        var zuordnung = new Dictionary<MimeMessage, AusgehendeMail>();
        foreach (var mail in faellig)
        {
            try
            {
                zuordnung[await NachrichtErstellenAsync(mail, cancellationToken)] = mail;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                mail.MarkiereFehlversuch(jetzt, "Nachricht konnte nicht erstellt werden: " + ex.Message);
            }
        }

        await smtp.SendenAsync(zuordnung.Keys.ToList(), (nachricht, fehler) =>
        {
            var mail = zuordnung[nachricht];
            if (fehler is null)
            {
                mail.MarkiereGesendet(uhr.GetUtcNow());
                gesendet++;
            }
            else
            {
                mail.MarkiereFehlversuch(uhr.GetUtcNow(), fehler.Message);
                ZustellungFehlgeschlagen(logger, fehler, mail.Id, mail.Versuche);
            }

            return Task.CompletedTask;
        }, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return gesendet;
    }

    private async Task<MimeMessage> NachrichtErstellenAsync(AusgehendeMail mail, CancellationToken cancellationToken)
    {
        var o = optionen.Value;
        var nachricht = new MimeMessage();
        nachricht.From.Add(new MailboxAddress(o.AbsenderName, o.AbsenderAdresse));
        if (!string.IsNullOrWhiteSpace(o.AntwortAn))
        {
            nachricht.ReplyTo.Add(MailboxAddress.Parse(o.AntwortAn));
        }

        foreach (var an in mail.An)
        {
            nachricht.To.Add(MailboxAddress.Parse(an));
        }

        nachricht.Subject = mail.Betreff;
        nachricht.MessageId = $"{mail.Id:N}@miethahn";

        var body = new BodyBuilder { HtmlBody = mail.Html, TextBody = mail.Text };
        foreach (var dateiId in mail.AnhangDateiIds)
        {
            await using var datei = await dateiablage.OeffnenAsync(dateiId, cancellationToken)
                ?? throw new InvalidOperationException($"Anhang {dateiId} nicht gefunden.");
            using var puffer = new MemoryStream();
            await datei.Inhalt.CopyToAsync(puffer, cancellationToken);
            await body.Attachments.AddAsync(datei.Datei.Name, new MemoryStream(puffer.ToArray()), ContentType.Parse(datei.Datei.MimeTyp), cancellationToken);
        }

        nachricht.Body = body.ToMessageBody();
        return nachricht;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Zustellung der Mail {MailId} fehlgeschlagen (Versuch {Versuch}).")]
    private static partial void ZustellungFehlgeschlagen(ILogger logger, Exception fehler, Guid mailId, int versuch);
}
