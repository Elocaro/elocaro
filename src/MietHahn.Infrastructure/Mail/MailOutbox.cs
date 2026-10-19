using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MietHahn.Application.Files;
using MietHahn.Domain.Communication;
using MietHahn.Infrastructure.Persistence;
using MimeKit;

namespace MietHahn.Infrastructure.Mail;

/// <summary>Delivers due mails from the outbox, with retry and backoff on errors.</summary>
internal sealed partial class MailOutbox(
    MietHahnDbContext db,
    ISmtpSender smtp,
    IFileStore fileStore,
    TimeProvider clock,
    IOptions<MailOptions> options,
    ILogger<MailOutbox> logger)
{
    /// <summary>Processes a batch of due mails and returns the number sent successfully.</summary>
    public async Task<int> DeliverAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var due = await db.Set<OutgoingMail>()
            .Where(m => m.Status == MailStatus.Pending && m.NextAttemptAt <= now)
            .OrderBy(m => m.NextAttemptAt)
            .Take(options.Value.BatchSize)
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
        {
            return 0;
        }

        var sent = 0;
        var assignment = new Dictionary<MimeMessage, OutgoingMail>();
        foreach (var mail in due)
        {
            try
            {
                assignment[await CreateMessageAsync(mail, cancellationToken)] = mail;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                mail.MarkFailedAttempt(now, "Nachricht konnte nicht erstellt werden: " + ex.Message);
            }
        }

        await smtp.SendAsync(assignment.Keys.ToList(), (message, errors) =>
        {
            var mail = assignment[message];
            if (errors is null)
            {
                mail.MarkSent(clock.GetUtcNow());
                sent++;
            }
            else
            {
                mail.MarkFailedAttempt(clock.GetUtcNow(), errors.Message);
                DeliveryFailed(logger, errors, mail.Id, mail.Attempts);
            }

            return Task.CompletedTask;
        }, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return sent;
    }

    private async Task<MimeMessage> CreateMessageAsync(OutgoingMail mail, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(o.SenderName, o.SenderAddress));
        if (!string.IsNullOrWhiteSpace(o.ReplyTo))
        {
            message.ReplyTo.Add(MailboxAddress.Parse(o.ReplyTo));
        }

        foreach (var to in mail.To)
        {
            message.To.Add(MailboxAddress.Parse(to));
        }

        message.Subject = mail.Subject;
        message.MessageId = $"{mail.Id:N}@miethahn";

        var body = new BodyBuilder { HtmlBody = mail.Html, TextBody = mail.Text };
        foreach (var fileId in mail.AttachmentFileIds)
        {
            await using var file = await fileStore.OpenAsync(fileId, cancellationToken)
                ?? throw new InvalidOperationException($"Anhang {fileId} nicht gefunden.");
            using var buffer = new MemoryStream();
            await file.Content.CopyToAsync(buffer, cancellationToken);
            await body.Attachments.AddAsync(file.StoredFile.Name, new MemoryStream(buffer.ToArray()), ContentType.Parse(file.StoredFile.MimeType), cancellationToken);
        }

        message.Body = body.ToMessageBody();
        return message;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Zustellung der Mail {MailId} fehlgeschlagen (Versuch {Attempt}).")]
    private static partial void DeliveryFailed(ILogger logger, Exception errors, Guid mailId, int attempt);
}
