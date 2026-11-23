using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Elocaro.Infrastructure.Pdf;

/// <summary>Converts HTML to PDF via Gotenberg 9 (Chromium) and joins PDFs; the app needs the interface of version 9 (stages, <c>/forms/pdf/pipe</c>).</summary>
internal sealed class GotenbergConverter(HttpClient http, IOptions<PdfOptions> options)
{
    // A4 in inches; the CSS (@page) controls the left/right/top margins, the bottom leaves room for the footer (25 mm).
    private const string A4Width = "8.27";
    private const string A4Height = "11.69";
    private const decimal FooterInches = 25m / 25.4m;

    public async Task<byte[]> HtmlToPdfAsync(string html, string footerHtml, PdfMetadata metadata, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        StoredFile(form, "index.html", html);
        StoredFile(form, "footer.html", footerHtml);
        Field(form, "paperWidth", A4Width);
        Field(form, "paperHeight", A4Height);
        Field(form, "preferCssPageSize", "true");
        Field(form, "marginTop", "0");
        Field(form, "marginLeft", "0");
        Field(form, "marginRight", "0");
        Field(form, "marginBottom", FooterInches.ToString("0.00", CultureInfo.InvariantCulture));
        Field(form, "printBackground", "true");
        var metadataStage = new Dictionary<string, string> { ["op"] = "set-metadata", ["title"] = metadata.Title, ["creator"] = "Elocaro" };
        if (!string.IsNullOrWhiteSpace(metadata.Author))
        {
            metadataStage["author"] = metadata.Author;
        }

        Field(form, "stages", Stages(metadataStage));

        using var response = await http.PostAsync(new Uri("forms/chromium/convert/html", UriKind.Relative), form, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errors = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Gotenberg meldet {(int)response.StatusCode}: {errors}");
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>Joins PDFs; Gotenberg sorts the files by name, so they are numbered.</summary>
    public async Task<byte[]> MergeAsync(IReadOnlyList<byte[]> documents, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documents);
        using var form = new MultipartFormDataContent();
        for (var i = 0; i < documents.Count; i++)
        {
            var part = new ByteArrayContent(documents[i]);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(part, "files", $"{i + 1:000}.pdf");
        }

        Field(form, "stages", Stages(new Dictionary<string, string> { ["op"] = "merge" }));

        using var response = await http.PostAsync(new Uri("forms/pdf/pipe", UriKind.Relative), form, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errors = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Gotenberg meldet {(int)response.StatusCode}: {errors}");
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static void StoredFile(MultipartFormDataContent form, string name, string content)
    {
        var part = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        part.Headers.ContentType = new MediaTypeHeaderValue("text/html") { CharSet = "utf-8" };
        form.Add(part, "files", name);
    }

    /// <summary>The processing steps of Gotenberg 9 (it no longer takes <c>metadata</c> and <c>pdfa</c> as form fields): the given steps, then the PDF/A conversion if configured.</summary>
    private string Stages(params Dictionary<string, string>[] steps)
    {
        var all = new List<Dictionary<string, string>>(steps);
        if (!string.IsNullOrWhiteSpace(options.Value.PdfA))
        {
            all.Add(new Dictionary<string, string> { ["op"] = "remediate", ["profile"] = options.Value.PdfA });
        }

        return JsonSerializer.Serialize(all);
    }

    private static void Field(MultipartFormDataContent form, string name, string value) => form.Add(new StringContent(value), name);
}

internal sealed record PdfMetadata(string Title, string? Author);
