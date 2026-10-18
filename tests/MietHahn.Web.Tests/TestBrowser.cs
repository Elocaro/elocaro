using System.Net;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace MietHahn.Web.Tests;

/// <summary>
/// Minimaler „Browser“ für statisch gerenderte Blazor-Seiten: lädt Seiten, folgt Weiterleitungen und schickt
/// Formulare inklusive Antiforgery-Token ab (Formular wird über seinen Blazor-FormName gefunden).
/// </summary>
public sealed class TestBrowser(HttpClient http) : IDisposable
{
    private static readonly HtmlParser Parser = new();

    public HttpClient Http => http;

    public async Task<Seite> LadenAsync(string url, bool weiterleitungenFolgen = true)
    {
        using var antwort = await http.GetAsync(new Uri(url, UriKind.RelativeOrAbsolute), TestContext.Current.CancellationToken);
        return await AuswertenAsync(antwort, weiterleitungenFolgen);
    }

    public async Task<Seite> AbsendenAsync(Seite seite, string formName, IDictionary<string, string>? felder = null, bool weiterleitungenFolgen = true)
    {
        ArgumentNullException.ThrowIfNull(seite);
        var formular = seite.Formular(formName);
        var werte = new List<KeyValuePair<string, string>>();
        foreach (var element in formular.Elements.OfType<IHtmlInputElement>())
        {
            if (string.IsNullOrEmpty(element.Name) || (element.Type is "checkbox" or "radio" && !element.IsChecked))
            {
                continue;
            }

            werte.Add(new(element.Name, element.Value));
        }

        // Wie ein Browser: ausgewählte Option bzw. Inhalt von Auswahllisten und Textfeldern mitsenden.
        foreach (var auswahl in formular.Elements.OfType<IHtmlSelectElement>().Where(e => !string.IsNullOrEmpty(e.Name)))
        {
            var option = auswahl.Options.FirstOrDefault(o => o.IsSelected) ?? (auswahl.Options.Length > 0 ? auswahl.Options[0] : null);
            if (option is not null)
            {
                werte.Add(new(auswahl.Name!, option.Value));
            }
        }

        foreach (var text in formular.Elements.OfType<IHtmlTextAreaElement>().Where(e => !string.IsNullOrEmpty(e.Name)))
        {
            werte.Add(new(text.Name!, text.Value));
        }

        // Nur Felder setzen, die das Formular wirklich hat: sonst bestünde ein Test, obwohl der Browser andere Namen sendet.
        var vorhanden = formular.Elements.OfType<IHtmlElement>().Select(e => e.GetAttribute("name")).Where(n => n is not null).ToHashSet();
        foreach (var (name, wert) in felder ?? new Dictionary<string, string>())
        {
            if (!vorhanden.Contains(name))
            {
                throw new InvalidOperationException($"Formular '{formName}' hat kein Feld '{name}'. Vorhanden: {string.Join(", ", vorhanden)}");
            }

            werte.RemoveAll(w => w.Key == name);
            werte.Add(new(name, wert));
        }

        var ziel = string.IsNullOrEmpty(formular.GetAttribute("action")) ? seite.Url : new Uri(seite.Url, formular.GetAttribute("action"));
        using var inhalt = new FormUrlEncodedContent(werte);
        using var antwort = await http.PostAsync(ziel, inhalt, TestContext.Current.CancellationToken);
        return await AuswertenAsync(antwort, weiterleitungenFolgen);
    }

    /// <summary>Schickt ein Formular als multipart/form-data mit einer Datei ab (z. B. Foto-Upload).</summary>
    public async Task<Seite> AbsendenMitDateiAsync(Seite seite, string formName, string dateiFeld, byte[] inhalt, string dateiname, string mimeTyp = "image/jpeg")
    {
        ArgumentNullException.ThrowIfNull(seite);
        var formular = seite.Formular(formName);
        using var daten = new MultipartFormDataContent();
        foreach (var element in formular.Elements.OfType<IHtmlInputElement>().Where(e => !string.IsNullOrEmpty(e.Name) && e.Type != "file"))
        {
            daten.Add(new StringContent(element.Value), element.Name!);
        }

        var datei = new ByteArrayContent(inhalt);
        datei.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeTyp);
        daten.Add(datei, dateiFeld, dateiname);
        var ziel = string.IsNullOrEmpty(formular.GetAttribute("action")) ? seite.Url : new Uri(seite.Url, formular.GetAttribute("action"));
        using var antwort = await http.PostAsync(ziel, daten, TestContext.Current.CancellationToken);
        return await AuswertenAsync(antwort, weiterleitungenFolgen: true);
    }

    public void Dispose() => http.Dispose();

    private async Task<Seite> AuswertenAsync(HttpResponseMessage antwort, bool weiterleitungenFolgen)
    {
        var url = antwort.RequestMessage!.RequestUri!;
        for (var i = 0; weiterleitungenFolgen && i < 10 && antwort.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect; i++)
        {
            var ziel = new Uri(url, antwort.Headers.Location!);
            antwort = await http.GetAsync(ziel, TestContext.Current.CancellationToken);
            url = ziel;
        }

        var html = await antwort.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return new Seite(antwort.StatusCode, url, antwort.Headers.Location, html, Parser.ParseDocument(html));
    }
}

public sealed record Seite(HttpStatusCode Status, Uri Url, Uri? Weiterleitung, string Html, IHtmlDocument Dokument)
{
    public string Pfad => Url.AbsolutePath;

    public string Text => Dokument.Body?.TextContent ?? "";

    /// <summary>Text inklusive der per Stream-Rendering nachgelieferten Teile (<c>&lt;blazor-ssr&gt;&lt;template&gt;</c>).</summary>
    public string TextMitStreaming =>
        Text + string.Concat(Dokument.QuerySelectorAll("blazor-ssr template").OfType<IHtmlTemplateElement>().Select(t => t.Content.TextContent));

    public IHtmlFormElement Formular(string formName) =>
        Dokument.Forms.FirstOrDefault(f => f.Elements.OfType<IHtmlInputElement>().Any(i => i.Name == "_handler" && i.Value == formName))
        ?? throw new InvalidOperationException($"Formular '{formName}' nicht gefunden auf {Url}. Vorhanden: {string.Join(", ", Dokument.Forms.SelectMany(f => f.Elements.OfType<IHtmlInputElement>()).Where(i => i.Name == "_handler").Select(i => i.Value))}");
}
