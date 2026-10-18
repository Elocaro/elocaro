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

        foreach (var (name, wert) in felder ?? new Dictionary<string, string>())
        {
            werte.RemoveAll(w => w.Key == name);
            werte.Add(new(name, wert));
        }

        var ziel = string.IsNullOrEmpty(formular.GetAttribute("action")) ? seite.Url : new Uri(seite.Url, formular.GetAttribute("action"));
        using var inhalt = new FormUrlEncodedContent(werte);
        using var antwort = await http.PostAsync(ziel, inhalt, TestContext.Current.CancellationToken);
        return await AuswertenAsync(antwort, weiterleitungenFolgen);
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
