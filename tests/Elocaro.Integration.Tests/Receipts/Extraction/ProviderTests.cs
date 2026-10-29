using System.Net;
using System.Text.Json;
using Elocaro.Application.Receipts;
using Elocaro.Domain.Receipts;
using Elocaro.Infrastructure.Receipts.Extraction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elocaro.Integration.Tests.Receipts.Extraction;

/// <summary>Providers against a fake HTTP handler: request format, answer handling, errors and data protection.</summary>
public class ProviderTests
{
    private const string SecretKey = "sk-ant-test-secret-123";
    private static readonly byte[] Pdf = "%PDF-1.4 test"u8.ToArray();
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];
    private static readonly ExtractionContext Context = new(new DateOnly(2026, 10, 19));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly object ToolInput = new
    {
        supplier_name = new { value = "Stadtwerke Köln", confidence = 0.98 },
        invoice_date = new { value = "2026-09-30", confidence = 0.97 },
        gross = new { value = 238.00, confidence = 0.99 },
        net = new { value = 200.00, confidence = 0.99 },
        vat = new { value = 38.00, confidence = 0.99 },
    };

    private static ReceiptExtractionOptions Options(Action<ReceiptExtractionOptions>? change = null)
    {
        var options = new ReceiptExtractionOptions
        {
            Provider = "Claude",
            AcceptExternalProcessing = true,
            Claude = { ApiKey = SecretKey, BaseUrl = new Uri("https://claude.test/") },
            Ollama = { BaseUrl = new Uri("http://ollama.test:11434/") },
        };
        change?.Invoke(options);
        return options;
    }

    private static ClaudeReceiptExtractor Claude(HttpMessageHandler handler, ReceiptExtractionOptions? options = null, ILogger<ClaudeReceiptExtractor>? logger = null) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(options ?? Options()), logger ?? NullLogger<ClaudeReceiptExtractor>.Instance);

    private static OllamaReceiptExtractor Ollama(FakeHttpHandler handler, ReceiptExtractionOptions? options = null) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(options ?? Options(o => o.Provider = "Ollama")), NullLogger<OllamaReceiptExtractor>.Instance);

    // ---- Claude ----

    [Fact]
    public async Task Claude_sends_pdf_and_image_to_the_messages_api_with_tool_schema_and_reads_the_tool_result()
    {
        var handler = FakeHttpHandler.Json(FakeHttpHandler.ClaudeToolResponse(ToolInput));

        var result = await Claude(handler).ExtractAsync([new("a.pdf", "application/pdf", Pdf), new("b.jpg", "image/jpeg", Jpeg)], Context, Ct);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://claude.test/v1/messages", request.Url.ToString());
        Assert.Equal(SecretKey, request.Headers["x-api-key"]);
        Assert.Equal("2023-06-01", request.Headers["anthropic-version"]);

        var body = request.Json();
        Assert.Equal("claude-sonnet-5-5", body.GetProperty("model").GetString());
        Assert.Equal("record_receipt", body.GetProperty("tool_choice").GetProperty("name").GetString());
        var tool = body.GetProperty("tools")[0];
        Assert.Equal("record_receipt", tool.GetProperty("name").GetString());
        Assert.True(tool.GetProperty("input_schema").GetProperty("properties").TryGetProperty("gross", out _));
        var blocks = body.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal("document", blocks[0].GetProperty("type").GetString());
        Assert.Equal("application/pdf", blocks[0].GetProperty("source").GetProperty("media_type").GetString());
        Assert.Equal(Convert.ToBase64String(Pdf), blocks[0].GetProperty("source").GetProperty("data").GetString());
        Assert.Equal("image", blocks[1].GetProperty("type").GetString());
        Assert.Equal("text", blocks[2].GetProperty("type").GetString());
        Assert.Contains("2026-10-19", body.GetProperty("system").GetString(), StringComparison.Ordinal);
        Assert.Contains("betrkv-10", body.GetProperty("system").GetString(), StringComparison.Ordinal);

        Assert.Equal(238.00m, result.Suggestion.Gross.Value);
        Assert.Equal("Stadtwerke Köln", result.Suggestion.SupplierName.Value);
        Assert.Equal(new ExtractionUsage(1200, 300), result.Usage);
    }

    [Fact]
    public async Task Claude_model_and_base_url_come_from_the_configuration()
    {
        var handler = FakeHttpHandler.Json(FakeHttpHandler.ClaudeToolResponse(ToolInput));
        var options = Options(o =>
        {
            o.Claude.Model = "claude-mein-modell";
            o.Claude.BaseUrl = new Uri("https://gateway.intern.test/proxy/");
        });

        await Claude(handler, options).ExtractAsync([new("a.pdf", "application/pdf", Pdf)], Context, Ct);

        Assert.Equal("https://gateway.intern.test/proxy/v1/messages", handler.Requests[0].Url.ToString());
        Assert.Equal("claude-mein-modell", handler.Requests[0].Json().GetProperty("model").GetString());
    }

    [Fact]
    public async Task Claude_accepts_a_text_answer_with_json_as_fallback()
    {
        var handler = FakeHttpHandler.Json(FakeHttpHandler.ClaudeTextResponse("```json\n{\"gross\": {\"value\": 12.5, \"confidence\": 0.9}}\n```"));

        var result = await Claude(handler).ExtractAsync([new("a.pdf", "application/pdf", Pdf)], Context, Ct);

        Assert.Equal(12.5m, result.Suggestion.Gross.Value);
    }

    [Fact]
    public async Task Claude_without_consent_or_key_does_not_send_anything()
    {
        var handler = FakeHttpHandler.Json("{}");
        var documents = new ExtractionDocument[] { new("a.pdf", "application/pdf", Pdf) };

        var noConsent = Claude(handler, Options(o => o.AcceptExternalProcessing = false));
        var noKey = Claude(handler, Options(o => o.Claude.ApiKey = " "));

        Assert.False(noConsent.Info.Ready);
        Assert.Contains("nicht freigegeben", noConsent.Info.Problem, StringComparison.Ordinal);
        Assert.Contains("API-Schlüssel", noKey.Info.Problem, StringComparison.Ordinal);
        await Assert.ThrowsAsync<ExtractionException>(() => noConsent.ExtractAsync(documents, Context, Ct));
        await Assert.ThrowsAsync<ExtractionException>(() => noKey.ExtractAsync(documents, Context, Ct));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Claude_info_declares_the_transfer_to_a_third_party()
    {
        var info = Claude(FakeHttpHandler.Json("{}")).Info;

        Assert.True(info.SendsToThirdParty);
        Assert.True(info.Ready);
        Assert.Contains("an einen Dritten übermittelt", info.Notice, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretKey, info.Notice, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "API-Schlüssel")]
    [InlineData(HttpStatusCode.Forbidden, "API-Schlüssel")]
    [InlineData(HttpStatusCode.NotFound, "Modell")]
    [InlineData(HttpStatusCode.TooManyRequests, "ausgelastet")]
    [InlineData(HttpStatusCode.InternalServerError, "meldet einen Fehler")]
    [InlineData(HttpStatusCode.BadRequest, "abgelehnt")]
    public async Task Claude_http_errors_become_friendly_messages_without_leaking_key_or_body(HttpStatusCode status, string expected)
    {
        var ex = await Assert.ThrowsAsync<ExtractionException>(() =>
            Claude(FakeHttpHandler.Status(status)).ExtractAsync([new("a.pdf", "application/pdf", Pdf)], Context, Ct));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretKey, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret details", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Claude_connection_failure_and_timeout_are_reported()
    {
        var unreachable = new FakeHttpHandler(_ => throw new HttpRequestException("boom " + SecretKey));

        var ex1 = await Assert.ThrowsAsync<ExtractionException>(() => Claude(unreachable).ExtractAsync([new("a.pdf", "application/pdf", Pdf)], Context, Ct));
        var ex2 = await Assert.ThrowsAsync<ExtractionException>(() => Claude(new FakeAsyncDelay(), Options(o => o.TimeoutSeconds = 1)).ExtractAsync([new("a.pdf", "application/pdf", Pdf)], Context, Ct));

        Assert.Contains("nicht erreichbar", ex1.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretKey, ex1.Message, StringComparison.Ordinal);
        Assert.Contains("nicht rechtzeitig", ex2.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Claude_malformed_output_raises_a_friendly_exception()
    {
        var handler = FakeHttpHandler.Json(FakeHttpHandler.ClaudeTextResponse("Leider kann ich das nicht lesen."));

        var ex = await Assert.ThrowsAsync<ExtractionException>(() => Claude(handler).ExtractAsync([new("a.pdf", "application/pdf", Pdf)], Context, Ct));

        Assert.Contains("Antwort des Modells", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Claude_response_that_is_not_json_or_has_no_content_is_an_error()
    {
        await Assert.ThrowsAsync<ExtractionException>(() => Claude(FakeHttpHandler.Json("<html>bad gateway</html>")).ExtractAsync([new("a.pdf", "application/pdf", Pdf)], Context, Ct));
        await Assert.ThrowsAsync<ExtractionException>(() => Claude(FakeHttpHandler.Json("{\"type\":\"message\"}")).ExtractAsync([new("a.pdf", "application/pdf", Pdf)], Context, Ct));
    }

    [Fact]
    public async Task Claude_rejects_unsupported_types_and_oversized_requests_before_sending()
    {
        var handler = FakeHttpHandler.Json("{}");
        var heic = await Assert.ThrowsAsync<ExtractionException>(() => Claude(handler).ExtractAsync([new("foto.heic", "image/heic", Jpeg)], Context, Ct));
        var big = await Assert.ThrowsAsync<ExtractionException>(() =>
            Claude(handler, Options(o => o.MaxRequestMb = 1)).ExtractAsync([new("a.pdf", "application/pdf", new byte[2 * 1024 * 1024])], Context, Ct));

        Assert.Contains("foto.heic", heic.Message, StringComparison.Ordinal);
        Assert.Contains("1 MB", big.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Claude_never_logs_the_key_or_the_document()
    {
        var logger = new ListLogger<ClaudeReceiptExtractor>();
        await Claude(FakeHttpHandler.Json(FakeHttpHandler.ClaudeToolResponse(ToolInput)), logger: logger).ExtractAsync([new("a.pdf", "application/pdf", Pdf)], Context, Ct);
        await Assert.ThrowsAsync<ExtractionException>(() =>
            Claude(FakeHttpHandler.Status(HttpStatusCode.Unauthorized), logger: logger).ExtractAsync([new("a.pdf", "application/pdf", Pdf)], Context, Ct));

        Assert.NotEmpty(logger.Messages);
        Assert.All(logger.Messages, m =>
        {
            Assert.DoesNotContain(SecretKey, m, StringComparison.Ordinal);
            Assert.DoesNotContain(Convert.ToBase64String(Pdf), m, StringComparison.Ordinal);
            Assert.DoesNotContain("Stadtwerke", m, StringComparison.Ordinal);
        });
    }

    // ---- Ollama ----

    [Fact]
    public async Task Ollama_sends_images_to_the_chat_api_with_schema_and_reads_the_message()
    {
        var content = JsonSerializer.Serialize(ToolInput);
        var handler = FakeHttpHandler.Json(FakeHttpHandler.OllamaResponse(content));

        var result = await Ollama(handler).ExtractAsync([new("a.jpg", "image/jpeg", Jpeg), new("b.png", "image/png", Jpeg)], Context, Ct);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://ollama.test:11434/api/chat", request.Url.ToString());
        Assert.DoesNotContain("x-api-key", request.Headers.Keys);
        var body = request.Json();
        Assert.Equal("qwen2.5vl:7b", body.GetProperty("model").GetString());
        Assert.False(body.GetProperty("stream").GetBoolean());
        Assert.True(body.GetProperty("format").GetProperty("properties").TryGetProperty("invoice_date", out _));
        var user = body.GetProperty("messages")[1];
        Assert.Equal(2, user.GetProperty("images").GetArrayLength());
        Assert.Equal(Convert.ToBase64String(Jpeg), user.GetProperty("images")[0].GetString());

        Assert.Equal(238.00m, result.Suggestion.Gross.Value);
        Assert.Equal(new ExtractionUsage(900, 250), result.Usage);
    }

    [Fact]
    public async Task Ollama_tolerates_json_in_code_fences()
    {
        var handler = FakeHttpHandler.Json(FakeHttpHandler.OllamaResponse("```json\n" + JsonSerializer.Serialize(ToolInput) + "\n```"));

        var result = await Ollama(handler).ExtractAsync([new("a.jpg", "image/jpeg", Jpeg)], Context, Ct);

        Assert.Equal("Stadtwerke Köln", result.Suggestion.SupplierName.Value);
    }

    [Fact]
    public async Task Ollama_needs_no_consent_stays_local_and_rejects_pdf()
    {
        var handler = FakeHttpHandler.Json("{}");
        var provider = Ollama(handler, Options(o =>
        {
            o.Provider = "Ollama";
            o.AcceptExternalProcessing = false;
            o.Claude.ApiKey = null;
        }));

        var ex = await Assert.ThrowsAsync<ExtractionException>(() => provider.ExtractAsync([new("a.pdf", "application/pdf", Pdf)], Context, Ct));

        Assert.True(provider.Info.Ready);
        Assert.False(provider.Info.SendsToThirdParty);
        Assert.Contains("ollama.test:11434", provider.Info.Notice, StringComparison.Ordinal);
        Assert.Contains("kein PDF", ex.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Ollama_errors_and_malformed_output_are_friendly()
    {
        var notFound = await Assert.ThrowsAsync<ExtractionException>(() =>
            Ollama(FakeHttpHandler.Status(HttpStatusCode.NotFound)).ExtractAsync([new("a.jpg", "image/jpeg", Jpeg)], Context, Ct));
        var malformed = await Assert.ThrowsAsync<ExtractionException>(() =>
            Ollama(FakeHttpHandler.Json(FakeHttpHandler.OllamaResponse("Ich sehe eine Rechnung."))).ExtractAsync([new("a.jpg", "image/jpeg", Jpeg)], Context, Ct));
        var empty = await Assert.ThrowsAsync<ExtractionException>(() =>
            Ollama(FakeHttpHandler.Json("{\"done\":true}")).ExtractAsync([new("a.jpg", "image/jpeg", Jpeg)], Context, Ct));

        Assert.Contains("Modell", notFound.Message, StringComparison.Ordinal);
        Assert.Contains("Antwort des Modells", malformed.Message, StringComparison.Ordinal);
        Assert.Contains("Antwort des Modells", empty.Message, StringComparison.Ordinal);
    }

    // ---- None ----

    [Fact]
    public void Unknown_provider_name_activates_nothing_and_reports_the_configuration_error()
    {
        var info = new NoReceiptExtractor(Microsoft.Extensions.Options.Options.Create(new ReceiptExtractionOptions { Provider = "Claud" })).Info;

        Assert.False(info.Ready);
        Assert.Contains("Unbekannter Anbieter „Claud“", info.Problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, ExtractionProvider.None)]
    [InlineData("", ExtractionProvider.None)]
    [InlineData("none", ExtractionProvider.None)]
    [InlineData(" CLAUDE ", ExtractionProvider.Claude)]
    [InlineData("ollama", ExtractionProvider.Ollama)]
    [InlineData("openai", null)]
    [InlineData("7", null)]
    public void Provider_names_are_parsed_leniently(string? name, ExtractionProvider? expected) =>
        Assert.Equal(expected, new ReceiptExtractionOptions { Provider = name }.ProviderKind);

    [Fact]
    public async Task None_is_disabled_and_refuses_to_extract()
    {
        var none = new NoReceiptExtractor(Microsoft.Extensions.Options.Options.Create(new ReceiptExtractionOptions()));

        Assert.False(none.Info.Enabled);
        Assert.False(none.Info.Ready);
        Assert.False(none.Info.SendsToThirdParty);
        await Assert.ThrowsAsync<ExtractionException>(() => none.ExtractAsync([], Context, Ct));
    }

    /// <summary>Takes longer than any test timeout, unless it is cancelled.</summary>
    private sealed class FakeAsyncDelay : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception) + exception);
    }
}
