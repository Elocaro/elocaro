using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;

namespace MietHahn.Infrastructure.Mail;

/// <summary>Encapsulates SMTP sending (replaceable in tests).</summary>
internal interface ISmtpSender
{
    Task SendAsync(IReadOnlyList<MimeMessage> messages, Func<MimeMessage, Exception?, Task> result, CancellationToken cancellationToken);
}

/// <summary>Sends a batch of messages over one SMTP connection (MailKit).</summary>
internal sealed class MailKitSmtpSender(IOptions<MailOptions> options) : ISmtpSender
{
    public async Task SendAsync(IReadOnlyList<MimeMessage> messages, Func<MimeMessage, Exception?, Task> result, CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            return;
        }

        var smtp = options.Value.Smtp;
        using var client = new SmtpClient { Timeout = (int)TimeSpan.FromSeconds(30).TotalMilliseconds };

        try
        {
            await client.ConnectAsync(smtp.Host, smtp.Port, smtp.Security, cancellationToken);
            if (!string.IsNullOrEmpty(smtp.UserName))
            {
                await client.AuthenticateAsync(smtp.UserName, smtp.Password ?? "", cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Connection or authentication failed: all messages in this batch count as failed attempts.
            foreach (var message in messages)
            {
                await result(message, ex);
            }

            return;
        }

        foreach (var message in messages)
        {
            try
            {
                await client.SendAsync(message, cancellationToken);
                await result(message, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await result(message, ex);
            }
        }

        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
