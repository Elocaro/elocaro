using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MietHahn.Application.Receipts;

namespace MietHahn.Infrastructure.Receipts.Extraction;

/// <summary>
/// Provider "Claude": sends the pages (PDF and photos) directly to the Messages API and forces a structured answer
/// through a tool schema. Model and base URL are configurable. The API key is read from the configuration only and is never logged.
/// </summary>
internal sealed class ClaudeReceiptExtractor(HttpClient http, IOptions<ReceiptExtractionOptions> options, ILogger<ClaudeReceiptExtractor> logger) : IReceiptExtractor
{
    private const string ProviderName = "Claude API";
    private static readonly string[] SupportedTypes = ["application/pdf", .. ExtractionHttp.ImageTypes];

    private ReceiptExtractionOptions Options => options.Value;

    public ExtractorInfo Info
    {
        get
        {
            var claude = Options.Claude;
            string? problem = null;
            if (!Options.AcceptExternalProcessing)
            {
                problem = "Die Übermittlung von Belegen an die Claude API ist nicht freigegeben (ReceiptExtraction__AcceptExternalProcessing).";
            }
            else if (string.IsNullOrWhiteSpace(claude.ApiKey))
            {
                problem = "Es ist kein API-Schlüssel konfiguriert (ReceiptExtraction__Claude__ApiKey).";
            }

            return new ExtractorInfo(
                ProviderName, claude.Model, Enabled: true, Ready: problem is null, SendsToThirdParty: true,
                $"Achtung: Zum Auslesen werden die hochgeladenen Belege an einen Dritten übermittelt (Claude API von Anthropic, {claude.BaseUrl.Host}). "
                + "Belege können personenbezogene Daten enthalten, z. B. Namen und Anschriften von Mietern. Die Auslesung erfolgt nur auf ausdrücklichen Klick, "
                + "das Ergebnis ist ein Vorschlag, den Sie prüfen und bestätigen.",
                problem);
        }
    }

    public async Task<ExtractorResult> ExtractAsync(IReadOnlyList<ExtractionDocument> documents, ExtractionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(context);
        var info = Info;
        if (!info.Ready)
        {
            throw new ExtractionException(info.Problem!);
        }

        if (documents.Count == 0)
        {
            throw new ExtractionException("Es gibt nichts auszulesen.");
        }

        var claude = Options.Claude;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(claude.BaseUrl, "v1/messages"))
        {
            Content = new StringContent(Body(documents, context).ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-api-key", claude.ApiKey);
        request.Headers.Add("anthropic-version", claude.ApiVersion);

        using var response = await ExtractionHttp.SendAsync(http, request, ProviderName, logger, TimeSpan.FromSeconds(Options.TimeoutSeconds), cancellationToken);

        var root = response.RootElement;
        var usage = Usage(root);
        ExtractionLog.Finished(logger, ProviderName, usage.InputTokens, usage.OutputTokens);
        if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                var type = block.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type == "tool_use" && block.TryGetProperty("input", out var input))
                {
                    return new ExtractorResult(ExtractionResponseParser.Parse(input), usage);
                }
            }

            // Fallback: the model answered with text containing the JSON.
            var text = string.Concat(content.EnumerateArray()
                .Where(b => b.TryGetProperty("type", out var t) && t.GetString() == "text" && b.TryGetProperty("text", out _))
                .Select(b => b.GetProperty("text").GetString()));
            return new ExtractorResult(ExtractionResponseParser.Parse(text), usage);
        }

        throw new ExtractionException("Die Antwort der Claude API enthielt kein Ergebnis.");
    }

    private JsonObject Body(IReadOnlyList<ExtractionDocument> documents, ExtractionContext context)
    {
        long total = 0;
        var blocks = new JsonArray();
        foreach (var document in documents)
        {
            if (!SupportedTypes.Contains(document.MimeType, StringComparer.OrdinalIgnoreCase))
            {
                throw new ExtractionException($"„{document.FileName}“ kann nicht ausgelesen werden (unterstützt: PDF, JPG, PNG). Bitte von Hand erfassen.");
            }

            total += document.Content.Length;
            blocks.Add(new JsonObject
            {
                ["type"] = document.MimeType == "application/pdf" ? "document" : "image",
                ["source"] = new JsonObject
                {
                    ["type"] = "base64",
                    ["media_type"] = document.MimeType,
                    ["data"] = Convert.ToBase64String(document.Content.Span),
                },
            });
        }

        if (total > Options.MaxRequestMb * 1024L * 1024L)
        {
            throw new ExtractionException($"Die Dateien sind zusammen größer als {Options.MaxRequestMb} MB und können nicht ausgelesen werden.");
        }

        blocks.Add(new JsonObject { ["type"] = "text", ["text"] = ExtractionSchema.UserPrompt });
        return new JsonObject
        {
            ["model"] = Options.Claude.Model,
            ["max_tokens"] = Options.Claude.MaxTokens,
            ["temperature"] = 0,
            ["system"] = ExtractionSchema.SystemPrompt(context.Today),
            ["tools"] = new JsonArray(new JsonObject
            {
                ["name"] = ExtractionSchema.ToolName,
                ["description"] = "Records the data read from the receipt.",
                ["input_schema"] = ExtractionSchema.Schema(),
            }),
            ["tool_choice"] = new JsonObject { ["type"] = "tool", ["name"] = ExtractionSchema.ToolName },
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = blocks }),
        };
    }

    private static ExtractionUsage Usage(JsonElement root) =>
        root.TryGetProperty("usage", out var usage)
            ? new ExtractionUsage(Number(usage, "input_tokens"), Number(usage, "output_tokens"))
            : ExtractionUsage.None;

    private static int Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
}
