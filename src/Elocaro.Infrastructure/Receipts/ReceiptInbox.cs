using Elocaro.Application.Files;
using Elocaro.Application.Receipts;
using Elocaro.Domain.Common;
using Elocaro.Infrastructure.Common;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Quartz;

namespace Elocaro.Infrastructure.Receipts;

/// <summary>Configuration section <c>ReceiptInbox</c>: a mailbox whose attachments become open receipts.</summary>
public sealed class ReceiptInboxOptions
{
    public const string Section = "ReceiptInbox";

    public bool Enabled { get; set; }

    public string Host { get; set; } = "";

    public int Port { get; set; } = 993;

    /// <summary><c>SslOnConnect</c> (Port 993), <c>StartTls</c> (Port 143) or <c>Auto</c>.</summary>
    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.SslOnConnect;

    public string? UserName { get; set; }

    public string? Password { get; set; }

    public string Folder { get; set; } = "INBOX";

    /// <summary>Folder for processed mails (created if missing). Empty = only mark them as read.</summary>
    public string ProcessedFolder { get; set; } = "Elocaro-verarbeitet";

    /// <summary>Senders that may submit receipts: full addresses or <c>@domain</c>. Empty = every mail is rejected.</summary>
    public string[] AllowedSenders { get; set; } = [];

    public int MaxAttachmentMegabytes { get; set; } = 15;

    /// <summary>Read the data from the attachments if the receipt extraction is ready (documents may leave the server, see there).</summary>
    public bool Extract { get; set; }

    /// <summary>Mails per run.</summary>
    public int BatchSize { get; set; } = 20;
}

/// <summary>What became of one mail.</summary>
public sealed record InboxOutcome(Guid? ReceiptId, string? Reason)
{
    public bool Created => ReceiptId is not null;
}

/// <summary>
/// Receipts by mail: the attachments (PDF, photos) of a mail from a known sender are stored as one open receipt without amount and cost type,
/// which then waits in the receipts list to be completed. The sender address is not verified by Elocaro: use a non-public address
/// and a mailbox whose server checks SPF/DKIM.
/// </summary>
internal sealed class ReceiptInbox(IReceiptService receipts, IFileStore files, IReceiptExtraction extraction, IOptions<ReceiptInboxOptions> options, TimeProvider clock, ILogger<ReceiptInbox> logger)
{
    private static readonly string[] Extensions = [".pdf", ".jpg", ".jpeg", ".png"];

    /// <returns>Number of receipts created.</returns>
    public async Task<int> PollAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Host))
        {
            return 0;
        }

        if (settings.AllowedSenders.Length == 0)
        {
            InboxLog.NoSenders(logger);
            return 0;
        }

        using var client = new ImapClient();
        await client.ConnectAsync(settings.Host, settings.Port, settings.Security, cancellationToken);
        if (!string.IsNullOrEmpty(settings.UserName))
        {
            await client.AuthenticateAsync(settings.UserName, settings.Password ?? "", cancellationToken);
        }

        var inbox = await client.GetFolderAsync(settings.Folder, cancellationToken);
        await inbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken);
        IMailFolder? processed = null;
        if (!string.IsNullOrWhiteSpace(settings.ProcessedFolder))
        {
            var root = client.GetFolder(client.PersonalNamespaces[0]);
            try
            {
                processed = await root.GetSubfolderAsync(settings.ProcessedFolder, cancellationToken);
            }
            catch (FolderNotFoundException)
            {
                processed = await root.CreateAsync(settings.ProcessedFolder, true, cancellationToken);
            }
        }

        var created = 0;
        foreach (var uid in (await inbox.SearchAsync(SearchQuery.NotSeen, cancellationToken)).Take(settings.BatchSize))
        {
            var message = await inbox.GetMessageAsync(uid, cancellationToken);
            InboxOutcome outcome;
            try
            {
                outcome = await ProcessAsync(message, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                InboxLog.Failed(logger, ex, message.MessageId);
                continue;
            }

            if (outcome.Created)
            {
                created++;
            }
            else
            {
                InboxLog.Rejected(logger, message.From.Mailboxes.FirstOrDefault()?.Address, outcome.Reason);
            }

            if (processed is not null)
            {
                await inbox.MoveToAsync(uid, processed, cancellationToken);
            }
            else
            {
                await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, cancellationToken);
            }
        }

        await client.DisconnectAsync(true, cancellationToken);
        return created;
    }

    public async Task<InboxOutcome> ProcessAsync(MimeMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;
        var sender = message.From.Mailboxes.FirstOrDefault();
        if (sender is null || !IsAllowed(sender.Address, settings.AllowedSenders))
        {
            return new InboxOutcome(null, "Absender nicht freigegeben");
        }

        var limit = settings.MaxAttachmentMegabytes * 1024L * 1024L;
        var fileIds = new List<Guid>();
        var skipped = new List<string>();
        foreach (var part in message.Attachments.OfType<MimePart>())
        {
            var name = part.FileName ?? "Anhang";
            if (!Extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
            {
                skipped.Add($"{name} (Dateityp)");
                continue;
            }

            using var memory = new MemoryStream();
            if (part.Content is null)
            {
                continue;
            }

            await part.Content.DecodeToAsync(memory, cancellationToken);
            if (memory.Length > limit)
            {
                skipped.Add($"{name} (größer als {settings.MaxAttachmentMegabytes} MB)");
                continue;
            }

            memory.Position = 0;
            try
            {
                fileIds.Add((await files.SaveAsync(memory, name, cancellationToken)).Id);
            }
            catch (FileRejectedException ex)
            {
                skipped.Add($"{name} ({ex.Message})");
            }
        }

        if (fileIds.Count == 0)
        {
            return new InboxOutcome(null, skipped.Count > 0 ? "Kein verwertbarer Anhang: " + string.Join(", ", skipped) : "Kein Anhang");
        }

        var today = GermanTime.Today(clock.GetUtcNow());
        var input = new ReceiptInput
        {
            SupplierName = (string.IsNullOrWhiteSpace(sender.Name) ? sender.Address : sender.Name).Truncate(200),
            InvoiceDate = message.Date == DateTimeOffset.MinValue ? today : GermanTime.Today(message.Date),
            Gross = 0m,
            Incomplete = true,
            Note = ("Per E-Mail eingegangen" + (string.IsNullOrWhiteSpace(message.Subject) ? "" : $", Betreff: {message.Subject}") + $", von {sender.Address}. Betrag und Kostenart ergänzen."
                + (skipped.Count > 0 ? " Nicht übernommen: " + string.Join(", ", skipped) + "." : "")).Truncate(2000),
        };

        if (settings.Extract && extraction.Info.Ready)
        {
            var suggestion = (await extraction.SuggestAsync(fileIds, cancellationToken)).Suggestion;
            if (suggestion is not null)
            {
                input.SupplierName = suggestion.SupplierName.HasValue ? suggestion.SupplierName.Value : input.SupplierName;
                input.InvoiceNumber = suggestion.InvoiceNumber.HasValue ? suggestion.InvoiceNumber.Value : null;
                input.InvoiceDate = suggestion.InvoiceDate.Value ?? input.InvoiceDate;
                input.Gross = suggestion.Gross.Value ?? 0m;
                input.Note += " Werte aus der Belegauslesung, bitte prüfen.";
            }
        }

        var result = await receipts.CreateAsync(input, fileIds, null, cancellationToken);
        return result.Succeeded ? new InboxOutcome(result.Id, null) : new InboxOutcome(null, string.Join(" ", result.Errors));
    }

    internal static bool IsAllowed(string address, IEnumerable<string> allowed) =>
        allowed.Any(a => a.StartsWith('@')
            ? address.EndsWith(a, StringComparison.OrdinalIgnoreCase)
            : address.Equals(a, StringComparison.OrdinalIgnoreCase));
}

internal static partial class InboxLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "The receipt inbox is on but ReceiptInbox:AllowedSenders is empty: nothing is fetched.")]
    public static partial void NoSenders(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Receipt inbox: mail {MessageId} could not be processed.")]
    public static partial void Failed(ILogger logger, Exception exception, string? messageId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Receipt inbox: mail from {Sender} rejected: {Reason}")]
    public static partial void Rejected(ILogger logger, string? sender, string? reason);
}

[DisallowConcurrentExecution]
internal sealed class ReceiptInboxJob(ReceiptInbox inbox) : IJob
{
    public static readonly JobKey Key = new("receipt-inbox", "receipts");

    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        await inbox.PollAsync(context.CancellationToken);
    }
}
