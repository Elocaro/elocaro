using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elocaro.Application.Receipts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elocaro.Infrastructure.Receipts.Extraction;

/// <summary>
/// Provider "Ollama": a local vision model reads the pages (HTTP API <c>/api/chat</c> with a JSON schema as <c>format</c>).
/// The documents stay on the own server or network. Ollama takes images only, PDFs have to be uploaded as photos.
/// </summary>
internal sealed class OllamaReceiptExtractor(HttpClient http, IOptions<ReceiptExtractionOptions> options, ILogger<OllamaReceiptExtractor> logger) : IReceiptExtractor
{
    private const string ProviderName = "Ollama";

    private ReceiptExtractionOptions Options => options.Value;

    public ExtractorInfo Info
    {
        get
        {
            var ollama = Options.Ollama;
            return new ExtractorInfo(
                ProviderName, ollama.Model, Enabled: true, Ready: true, SendsToThirdParty: false,
                $"Die Belege werden lokal mit Ollama ({ollama.BaseUrl.Authority}) ausgelesen und nicht an Dritte im Internet übermittelt, "
                + "solange Ollama auf dem eigenen Server oder im eigenen Netz läuft. Das Ergebnis ist ein Vorschlag, den Sie prüfen und bestätigen. "
                + "Ollama liest nur Fotos (JPG, PNG), keine PDF-Dateien.",
                null);
        }
    }

    public async Task<ExtractorResult> ExtractAsync(IReadOnlyList<ExtractionDocument> documents, ExtractionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(context);
        if (documents.Count == 0)
        {
            throw new ExtractionException("Es gibt nichts auszulesen.");
        }

        var ollama = Options.Ollama;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(ollama.BaseUrl, "api/chat"))
        {
            Content = new StringContent(Body(documents, context).ToJsonString(), Encoding.UTF8, "application/json"),
        };

        using var response = await ExtractionHttp.SendAsync(http, request, ProviderName, logger, TimeSpan.FromSeconds(Options.TimeoutSeconds), cancellationToken);

        var root = response.RootElement;
        var usage = new ExtractionUsage(Number(root, "prompt_eval_count"), Number(root, "eval_count"));
        ExtractionLog.Finished(logger, ProviderName, usage.InputTokens, usage.OutputTokens);
        var text = root.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content) ? content.GetString() : null;
        return new ExtractorResult(ExtractionResponseParser.Parse(text), usage);
    }

    private JsonObject Body(IReadOnlyList<ExtractionDocument> documents, ExtractionContext context)
    {
        long total = 0;
        var images = new JsonArray();
        foreach (var document in documents)
        {
            if (!ExtractionHttp.ImageTypes.Contains(document.MimeType, StringComparer.OrdinalIgnoreCase))
            {
                throw new ExtractionException($"„{document.FileName}“ kann mit Ollama nicht ausgelesen werden (nur Fotos als JPG oder PNG, kein PDF). Bitte als Foto hochladen oder von Hand erfassen.");
            }

            total += document.Content.Length;
            images.Add(Convert.ToBase64String(document.Content.Span));
        }

        if (total > Options.MaxRequestMb * 1024L * 1024L)
        {
            throw new ExtractionException($"Die Dateien sind zusammen größer als {Options.MaxRequestMb} MB und können nicht ausgelesen werden.");
        }

        return new JsonObject
        {
            ["model"] = Options.Ollama.Model,
            ["stream"] = false,
            ["format"] = ExtractionSchema.Schema(),
            ["options"] = new JsonObject { ["temperature"] = 0 },
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = ExtractionSchema.SystemPrompt(context.Today) },
                new JsonObject { ["role"] = "user", ["content"] = ExtractionSchema.UserPrompt, ["images"] = images }),
        };
    }

    private static int Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
}
