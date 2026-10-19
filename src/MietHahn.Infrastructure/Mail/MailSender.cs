using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MietHahn.Application.Communication;
using MietHahn.Domain.Communication;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Infrastructure.Templates;

namespace MietHahn.Infrastructure.Mail;

/// <summary>
/// Renders mail templates from <c>Mail/Templates/{name}.sbn</c> into the layout <c>_layout.sbn</c> and places them in the outbox.
/// Every template must set the subject via <c>{{ capture subject }}…{{ end }}</c>; optionally a plain-text version via <c>capture text</c>.
/// </summary>
internal sealed partial class MailSender(
    MietHahnDbContext db,
    ScribanRenderer renderer,
    TimeProvider clock,
    IOptions<MailOptions> options) : IMailSender
{
    public async Task<Guid> EnqueueAsync(MailRequest job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.Template);
        if (!TemplateName().IsMatch(job.Template))
        {
            throw new ArgumentException($"Ungültiger Vorlagenname '{job.Template}'.", nameof(job));
        }

        var content = renderer.RenderEmbedded($"Mail/Templates/{job.Template}.sbn", job.Model);
        var subject = content.Variable("subject")
            ?? throw new TemplateErrorException($"Mailvorlage '{job.Template}' setzt keinen Betreff (capture betreff).");
        var text = content.Variable("text") ?? HtmlToText(content.Html);

        var layout = renderer.RenderEmbedded("Mail/Templates/_layout.sbn", null, new Dictionary<string, object?>
        {
            ["subject"] = subject,
            ["content"] = new RawHtml(content.Html),
            ["sender_name"] = options.Value.SenderName,
        });

        var mail = new OutgoingMail(
            job.To, subject, layout.Html, text, clock.GetUtcNow(), job.Attachments, job.Template, job.Reference);
        db.Add(mail);
        await db.SaveChangesAsync(cancellationToken);
        return mail.Id;
    }

    /// <summary>Simple plain-text version: preserve paragraphs/line breaks, strip tags, decode entities.</summary>
    internal static string HtmlToText(string html)
    {
        var text = LineBreakTags().Replace(html, "\n");
        text = LinkTags().Replace(text, "$2 ($1)");
        text = AllTags().Replace(text, "");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = MultipleBlankLines().Replace(text, "\n\n");
        return string.Join('\n', text.Split('\n').Select(z => z.Trim())).Trim();
    }

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex TemplateName();

    [GeneratedRegex(@"<(br\s*/?|/p|/div|/h[1-6]|/li|/tr)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTags();

    [GeneratedRegex("<a[^>]*href=\"([^\"]*)\"[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LinkTags();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex AllTags();

    [GeneratedRegex(@"\n\s*\n(\s*\n)+")]
    private static partial Regex MultipleBlankLines();
}
