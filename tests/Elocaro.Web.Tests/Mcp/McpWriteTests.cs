using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Elocaro.Application.Accounts;
using Elocaro.Application.Audit;
using Elocaro.Application.Properties;
using Elocaro.Web.Tests.Account;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Web.Tests.Mcp;

/// <summary>Writing through MCP (ADR 0007): only with the write scope and only while Mcp:AllowWrite is on; each call is one operation that can be taken back.</summary>
public class McpWriteTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected override IDictionary<string, string?>? Configuration => new Dictionary<string, string?> { ["Mcp:Enabled"] = "true", ["Mcp:AllowWrite"] = "true" };

    private async Task<string> TokenAsync(Guid userId, params string[] scopes)
    {
        var result = await App.WithScopeAsync(sp => sp.GetRequiredService<IAccessTokens>().CreateAsync(Principal(userId), "Test", scopes, 30, Ct));
        Assert.True(result.Succeeded, result.Error);
        return result.Secret!;
    }

    private HttpClient Client(string token)
    {
        var client = App.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false, BaseAddress = new Uri("https://localhost/") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement?> RpcAsync(HttpClient client, string method, object? parameters = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters ?? new { } }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        using var response = await client.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        var json = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("data:", StringComparison.Ordinal))?[5..].Trim() ?? text;
        return response.IsSuccessStatusCode && json.StartsWith('{') ? JsonElement.Parse(json) : null;
    }

    private static async Task<List<string>> ToolNamesAsync(HttpClient client) =>
        (await RpcAsync(client, "tools/list"))!.Value.GetProperty("result").GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()!).ToList();

    private static bool Refused(JsonElement? body) =>
        body is null
        || body.Value.TryGetProperty("error", out _)
        || (body.Value.TryGetProperty("result", out var result) && result.TryGetProperty("isError", out var isError) && isError.GetBoolean());

    /// <summary>The structured answer of a tool call (the change result).</summary>
    private static JsonElement Answer(JsonElement? body)
    {
        var result = body!.Value.GetProperty("result");
        return result.TryGetProperty("structuredContent", out var structured)
            ? structured
            : JsonElement.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!);
    }

    [Fact]
    public async Task Read_scopes_alone_see_no_write_tool_and_cannot_call_one()
    {
        var (browser, id) = await SignedInAsAsync(Roles.Admin);
        using var b = browser;
        using var client = Client(await TokenAsync(id, "read:masterdata", "read:finance"));

        Assert.DoesNotContain("create_property", await ToolNamesAsync(client));
        var body = await RpcAsync(client, "tools/call", new { name = "create_property", arguments = new { name = "X", street = "Weg 1", postalCode = "50667", city = "Köln" } });
        Assert.True(Refused(body), body?.ToString());
    }

    [Fact]
    public async Task The_write_scope_shows_the_write_tools_with_honest_annotations()
    {
        var (browser, id) = await SignedInAsAsync(Roles.Admin);
        using var b = browser;
        using var client = Client(await TokenAsync(id, IAccessTokens.WriteMasterData));

        var tools = (await RpcAsync(client, "tools/list"))!.Value.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
        var create = tools.Single(t => t.GetProperty("name").GetString() == "create_property");
        Assert.False(create.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
        Assert.False(create.GetProperty("annotations").GetProperty("destructiveHint").GetBoolean());
        Assert.Contains(tools, t => t.GetProperty("name").GetString() == "list_properties"); // the write scope includes reading
        Assert.DoesNotContain(tools, t => t.GetProperty("name").GetString() == "get_tenant_account");
    }

    [Fact]
    public async Task A_created_property_is_an_mcp_operation_and_can_be_taken_back()
    {
        var (browser, id) = await SignedInAsAsync(Roles.Admin);
        using var b = browser;
        using var client = Client(await TokenAsync(id, IAccessTokens.WriteMasterData));

        var body = await RpcAsync(client, "tools/call", new { name = "create_property", arguments = new { name = "Lindenweg 3", street = "Lindenweg 3", postalCode = "50667", city = "Köln" } });
        Assert.False(Refused(body), body?.ToString());
        var answer = Answer(body);
        Assert.True(answer.GetProperty("succeeded").GetBoolean(), answer.ToString());
        var propertyId = answer.GetProperty("id").GetGuid();

        var rows = await App.WithScopeAsync(sp => sp.GetRequiredService<IActivityLog>().RecentAsync(ChangeChannel.Mcp, 10, Ct));
        var row = Assert.Single(rows);
        Assert.Equal("MCP create_property", row.Label);
        Assert.True(row.Restorable);

        var undone = await App.WithScopeAsync(sp => sp.GetRequiredService<IOperationUndo>().UndoAsync(row.Id, Ct));
        Assert.True(undone.Succeeded, string.Join(" ", undone.Errors));
        Assert.Null(await App.WithScopeAsync(sp => sp.GetRequiredService<IPropertyService>().GetAsync(propertyId, Ct)));
    }

    [Fact]
    public async Task Invalid_input_is_refused_with_the_messages_of_the_form_rules()
    {
        var (browser, id) = await SignedInAsAsync(Roles.Admin);
        using var b = browser;
        using var client = Client(await TokenAsync(id, IAccessTokens.WriteMasterData));

        var answer = Answer(await RpcAsync(client, "tools/call", new { name = "create_property", arguments = new { name = "X", street = "Weg 1", postalCode = "12", city = "Köln" } }));

        Assert.False(answer.GetProperty("succeeded").GetBoolean());
        Assert.Contains("Die Postleitzahl hat 5 Ziffern.", answer.GetProperty("errors").EnumerateArray().Select(e => e.GetString()));
    }
}

public class McpWriteSwitchTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    protected override IDictionary<string, string?>? Configuration => new Dictionary<string, string?> { ["Mcp:Enabled"] = "true" };

    [Fact]
    public async Task Without_AllowWrite_a_write_scope_cannot_be_issued()
    {
        var (browser, id) = await SignedInAsAsync(Roles.Admin);
        using var b = browser;

        var result = await App.WithScopeAsync(sp => sp.GetRequiredService<IAccessTokens>().CreateAsync(Principal(id), "Schreiben", [IAccessTokens.WriteMasterData], 30, TestContext.Current.CancellationToken));

        Assert.False(result.Succeeded);
        Assert.Contains("Schreibzugriff", result.Error, StringComparison.Ordinal);
    }
}
