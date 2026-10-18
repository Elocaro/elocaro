using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MietHahn.Application.Kommunikation;
using MietHahn.Domain.Kommunikation;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Infrastructure.Vorlagen;

namespace MietHahn.Infrastructure.Mail;

/// <summary>
/// Rendert Mailvorlagen aus <c>Mail/Vorlagen/{name}.sbn</c> in das Layout <c>_layout.sbn</c> und legt sie in die Outbox.
/// Jede Vorlage muss den Betreff per <c>{{ capture betreff }}…{{ end }}</c> setzen; optional eine Textfassung per <c>capture text</c>.
/// </summary>
internal sealed partial class MailVersand(
    MietHahnDbContext db,
    ScribanRenderer renderer,
    TimeProvider uhr,
    IOptions<MailOptionen> optionen) : IMailVersand
{
    public async Task<Guid> EinreihenAsync(MailAuftrag auftrag, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auftrag);
        ArgumentException.ThrowIfNullOrWhiteSpace(auftrag.Vorlage);
        if (!VorlagenName().IsMatch(auftrag.Vorlage))
        {
            throw new ArgumentException($"Ungültiger Vorlagenname '{auftrag.Vorlage}'.", nameof(auftrag));
        }

        var inhalt = renderer.RendernEingebettet($"Mail/Vorlagen/{auftrag.Vorlage}.sbn", auftrag.Modell);
        var betreff = inhalt.Variable("betreff")
            ?? throw new VorlagenFehlerException($"Mailvorlage '{auftrag.Vorlage}' setzt keinen Betreff (capture betreff).");
        var text = inhalt.Variable("text") ?? HtmlZuText(inhalt.Html);

        var layout = renderer.RendernEingebettet("Mail/Vorlagen/_layout.sbn", null, new Dictionary<string, object?>
        {
            ["betreff"] = betreff,
            ["inhalt"] = new RohesHtml(inhalt.Html),
            ["absender_name"] = optionen.Value.AbsenderName,
        });

        var mail = new AusgehendeMail(
            auftrag.An, betreff, layout.Html, text, uhr.GetUtcNow(), auftrag.Anhaenge, auftrag.Vorlage, auftrag.Bezug);
        db.Add(mail);
        await db.SaveChangesAsync(cancellationToken);
        return mail.Id;
    }

    /// <summary>Einfache Textfassung: Absätze/Umbrüche erhalten, Tags entfernen, Entities dekodieren.</summary>
    internal static string HtmlZuText(string html)
    {
        var text = ZeilenumbruchTags().Replace(html, "\n");
        text = LinkTags().Replace(text, "$2 ($1)");
        text = AlleTags().Replace(text, "");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = MehrfacheLeerzeilen().Replace(text, "\n\n");
        return string.Join('\n', text.Split('\n').Select(z => z.Trim())).Trim();
    }

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex VorlagenName();

    [GeneratedRegex(@"<(br\s*/?|/p|/div|/h[1-6]|/li|/tr)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex ZeilenumbruchTags();

    [GeneratedRegex("<a[^>]*href=\"([^\"]*)\"[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LinkTags();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex AlleTags();

    [GeneratedRegex(@"\n\s*\n(\s*\n)+")]
    private static partial Regex MehrfacheLeerzeilen();
}
