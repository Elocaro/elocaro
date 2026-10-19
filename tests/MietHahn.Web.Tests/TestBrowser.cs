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

    public async Task<Page> GetAsync(string url, bool followRedirects = true)
    {
        using var response = await http.GetAsync(new Uri(url, UriKind.RelativeOrAbsolute), TestContext.Current.CancellationToken);
        return await EvaluateAsync(response, followRedirects);
    }

    public async Task<Page> SubmitAsync(Page page, string formName, IDictionary<string, string>? fields = null, bool followRedirects = true)
    {
        ArgumentNullException.ThrowIfNull(page);
        var form = page.Form(formName);
        using var content = new FormUrlEncodedContent(Values(form, formName, fields));
        using var response = await http.PostAsync(Target(page, form), content, TestContext.Current.CancellationToken);
        return await EvaluateAsync(response, followRedirects);
    }

    /// <summary>Schickt ein Formular als multipart/form-data mit einer Datei ab (z. B. Foto-Upload).</summary>
    public async Task<Page> SubmitWithFileAsync(Page page, string formName, string fileField, byte[] content, string fileName, string mimeType = "image/jpeg", IDictionary<string, string>? fields = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        var form = page.Form(formName);
        using var data = new MultipartFormDataContent();
        foreach (var (name, value) in Values(form, formName, fields))
        {
            data.Add(new StringContent(value), name);
        }

        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
        data.Add(file, fileField, fileName);
        using var response = await http.PostAsync(Target(page, form), data, TestContext.Current.CancellationToken);
        return await EvaluateAsync(response, followRedirects: true);
    }

    private static Uri Target(Page page, IHtmlFormElement form) =>
        string.IsNullOrEmpty(form.GetAttribute("action")) ? page.Url : new Uri(page.Url, form.GetAttribute("action"));

    /// <summary>Feldwerte wie ein Browser (ohne Dateifelder), überschrieben mit <paramref name="fields"/>.</summary>
    private static List<KeyValuePair<string, string>> Values(IHtmlFormElement form, string formName, IDictionary<string, string>? fields)
    {
        var values = new List<KeyValuePair<string, string>>();
        foreach (var element in form.Elements.OfType<IHtmlInputElement>())
        {
            if (string.IsNullOrEmpty(element.Name) || element.Type == "file" || (element.Type is "checkbox" or "radio" && !element.IsChecked))
            {
                continue;
            }

            values.Add(new(element.Name, element.Value));
        }

        // Wie ein Browser: ausgewählte Option bzw. Inhalt von Auswahllisten und Textfeldern mitsenden.
        foreach (var selection in form.Elements.OfType<IHtmlSelectElement>().Where(e => !string.IsNullOrEmpty(e.Name)))
        {
            var option = selection.Options.FirstOrDefault(o => o.IsSelected) ?? (selection.Options.Length > 0 ? selection.Options[0] : null);
            if (option is not null)
            {
                values.Add(new(selection.Name!, option.Value));
            }
        }

        foreach (var text in form.Elements.OfType<IHtmlTextAreaElement>().Where(e => !string.IsNullOrEmpty(e.Name)))
        {
            values.Add(new(text.Name!, text.Value));
        }

        // Nur Felder setzen, die das Formular wirklich hat: sonst bestünde ein Test, obwohl der Browser andere Namen sendet.
        var existing = form.Elements.OfType<IHtmlElement>().Select(e => e.GetAttribute("name")).Where(n => n is not null).ToHashSet();
        foreach (var (name, value) in fields ?? new Dictionary<string, string>())
        {
            if (!existing.Contains(name))
            {
                throw new InvalidOperationException($"Formular '{formName}' hat kein Feld '{name}'. Vorhanden: {string.Join(", ", existing)}");
            }

            values.RemoveAll(w => w.Key == name);
            values.Add(new(name, value));
        }

        return values;
    }

    public void Dispose() => http.Dispose();

    private async Task<Page> EvaluateAsync(HttpResponseMessage response, bool followRedirects)
    {
        var url = response.RequestMessage!.RequestUri!;
        for (var i = 0; followRedirects && i < 10 && response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect; i++)
        {
            var destination = new Uri(url, response.Headers.Location!);
            response = await http.GetAsync(destination, TestContext.Current.CancellationToken);
            url = destination;
        }

        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return new Page(response.StatusCode, url, response.Headers.Location, html, Parser.ParseDocument(html));
    }
}

public sealed record Page(HttpStatusCode Status, Uri Url, Uri? RedirectLocation, string Html, IHtmlDocument Document)
{
    public string Path => Url.AbsolutePath;

    public string Text => Document.Body?.TextContent ?? "";

    /// <summary>Text inklusive der per Stream-Rendering nachgelieferten Teile (<c>&lt;blazor-ssr&gt;&lt;template&gt;</c>).</summary>
    public string TextWithStreaming =>
        Text + string.Concat(Document.QuerySelectorAll("blazor-ssr template").OfType<IHtmlTemplateElement>().Select(t => t.Content.TextContent));

    public IHtmlFormElement Form(string formName) =>
        Document.Forms.FirstOrDefault(f => f.Elements.OfType<IHtmlInputElement>().Any(i => i.Name == "_handler" && i.Value == formName))
        ?? throw new InvalidOperationException($"Formular '{formName}' nicht gefunden auf {Url}. Vorhanden: {string.Join(", ", Document.Forms.SelectMany(f => f.Elements.OfType<IHtmlInputElement>()).Where(i => i.Name == "_handler").Select(i => i.Value))}");
}
