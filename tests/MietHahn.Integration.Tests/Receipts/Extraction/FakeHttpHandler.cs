using System.Net;
using System.Text;
using System.Text.Json;

namespace MietHahn.Integration.Tests.Receipts.Extraction;

/// <summary>Stands in for the provider's HTTP server: records requests and answers with canned responses. Nothing leaves the process.</summary>
internal sealed class FakeHttpHandler(Func<RecordedRequest, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    public static FakeHttpHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    public static FakeHttpHandler Status(HttpStatusCode status, string body = "{\"error\":\"secret details\"}") => Json(body, status);

    /// <summary>Claude Messages API response with the tool call.</summary>
    public static string ClaudeToolResponse(object input, int inputTokens = 1200, int outputTokens = 300) =>
        JsonSerializer.Serialize(new
        {
            id = "msg_test",
            type = "message",
            role = "assistant",
            content = new object[] { new { type = "tool_use", id = "toolu_1", name = "record_receipt", input } },
            stop_reason = "tool_use",
            usage = new { input_tokens = inputTokens, output_tokens = outputTokens },
        });

    /// <summary>Claude response in which the model answered with text instead of calling the tool.</summary>
    public static string ClaudeTextResponse(string text) =>
        JsonSerializer.Serialize(new { type = "message", content = new object[] { new { type = "text", text } }, usage = new { input_tokens = 10, output_tokens = 5 } });

    /// <summary>Ollama <c>/api/chat</c> response (<paramref name="content"/> is the model output as text).</summary>
    public static string OllamaResponse(string content, int promptTokens = 900, int outputTokens = 250) =>
        JsonSerializer.Serialize(new { model = "qwen2.5vl:7b", message = new { role = "assistant", content }, done = true, prompt_eval_count = promptTokens, eval_count = outputTokens });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(request.Method, request.RequestUri!, request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase), body);
        lock (Requests)
        {
            Requests.Add(recorded);
        }

        return respond(recorded);
    }
}

internal sealed record RecordedRequest(HttpMethod Method, Uri Url, Dictionary<string, string> Headers, string Body)
{
    public JsonElement Json() => JsonElement.Parse(Body);
}
