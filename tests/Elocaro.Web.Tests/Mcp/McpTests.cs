using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Elocaro.Application.Accounts;
using Elocaro.Application.Audit;
using Elocaro.Infrastructure.Audit;
using Elocaro.Web.Tests.Account;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Web.Tests.Mcp;

public class McpTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private static readonly string[] AllScopes = ["read:masterdata", "read:finance"];

    protected override IDictionary<string, string?>? Configuration => new Dictionary<string, string?> { ["Mcp:Enabled"] = "true" };

    private async Task<string> TokenAsync(Guid userId, params string[] scopes)
    {
        var result = await App.WithScopeAsync(sp => sp.GetRequiredService<IAccessTokens>().CreateAsync(Principal(userId), "Test", scopes, 30, TestContext.Current.CancellationToken));
        Assert.True(result.Succeeded, result.Error);
        return result.Secret!;
    }

    private HttpClient Client(string? token)
    {
        var client = App.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false, BaseAddress = new Uri("https://localhost/") });
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static async Task<(HttpStatusCode Status, JsonElement? Body)> RpcAsync(HttpClient client, string method, object? parameters = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters ?? new { } }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // The answer is a plain JSON document or one SSE event ("data: {...}").
        var json = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("data:", StringComparison.Ordinal))?[5..].Trim() ?? text;
        return (response.StatusCode, response.IsSuccessStatusCode && json.StartsWith('{') ? JsonDocument.Parse(json).RootElement.Clone() : null);
    }

    private static async Task<List<string>> ToolNamesAsync(HttpClient client)
    {
        var (status, body) = await RpcAsync(client, "tools/list");
        Assert.Equal(HttpStatusCode.OK, status);
        return body!.Value.GetProperty("result").GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()!).ToList();
    }

    [Fact]
    public async Task Without_token_and_with_only_the_session_cookie_the_endpoint_answers_401()
    {
        var (browser, _) = await SignedInAsAsync(Roles.Admin);
        using var b = browser;
        using var anonymous = Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RpcAsync(anonymous, "tools/list")).Status);

        // The sign-in cookie is no credential here.
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await browser.Http.PostAsync(new Uri("/mcp", UriKind.Relative), content, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var wrong = Client("elo_" + new string('x', 43));
        Assert.Equal(HttpStatusCode.Unauthorized, (await RpcAsync(wrong, "tools/list")).Status);
    }

    [Fact]
    public async Task Admin_token_with_both_scopes_sees_all_tools_and_all_are_read_only()
    {
        var (browser, id) = await SignedInAsAsync(Roles.Admin);
        using var b = browser;
        using var client = Client(await TokenAsync(id, AllScopes));

        var (_, body) = await RpcAsync(client, "tools/list");
        var tools = body!.Value.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
        var names = tools.Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Contains("list_properties", names);
        Assert.Contains("get_tenant_account", names);
        Assert.Contains("list_todos", names);
        Assert.All(tools, t => Assert.True(t.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean()));
    }

    [Fact]
    public async Task Token_scope_narrows_an_admin_to_the_master_data_tools()
    {
        var (browser, id) = await SignedInAsAsync(Roles.Admin);
        using var b = browser;
        using var client = Client(await TokenAsync(id, "read:masterdata"));

        var names = await ToolNamesAsync(client);
        Assert.Contains("list_properties", names);
        Assert.DoesNotContain("get_tenant_account", names);
        Assert.DoesNotContain("list_open_items", names);

        // A call of a tool outside the scope is refused too, not only hidden.
        var (_, body) = await RpcAsync(client, "tools/call", new { name = "list_open_items", arguments = new { } });
        Assert.True(Refused(body), body?.ToString());
    }

    [Fact]
    public async Task Manager_without_finance_permission_gets_no_finance_tools_even_with_the_scope_on_the_token()
    {
        var (browser, id) = await SignedInAsAsync(Roles.Manager, [AccessRight.MasterData]);
        using var b = browser;

        // The scope cannot even be chosen: only rights the account has.
        var tooMuch = await App.WithScopeAsync(sp => sp.GetRequiredService<IAccessTokens>().CreateAsync(Principal(id), "Zu viel", AllScopes, 30, TestContext.Current.CancellationToken));
        Assert.False(tooMuch.Succeeded);

        using var client = Client(await TokenAsync(id, "read:masterdata"));
        Assert.DoesNotContain("list_open_items", await ToolNamesAsync(client));
    }

    [Fact]
    public async Task Withdrawing_a_permission_takes_effect_on_the_next_request()
    {
        var (browser, id) = await SignedInAsAsync(Roles.Manager, [AccessRight.MasterData, AccessRight.Finance]);
        using var b = browser;
        using var client = Client(await TokenAsync(id, AllScopes));
        Assert.Contains("list_open_items", await ToolNamesAsync(client));

        await App.WithScopeAsync(async sp =>
        {
            Assert.True((await sp.GetRequiredService<IUserManagementService>().SetPermissionsAsync(AdminPrincipal(await AdminIdAsync()), id, [AccessRight.MasterData], TestContext.Current.CancellationToken)).Succeeded);
            return true;
        });

        var names = await ToolNamesAsync(client);
        Assert.DoesNotContain("list_open_items", names);
        Assert.Contains("list_properties", names);
    }

    [Fact]
    public async Task Revoked_token_is_rejected()
    {
        var (browser, id) = await SignedInAsAsync(Roles.Admin);
        using var b = browser;
        var secret = await TokenAsync(id, AllScopes);
        var token = (await App.WithScopeAsync(sp => sp.GetRequiredService<IAccessTokens>().ListAsync(Principal(id), TestContext.Current.CancellationToken))).Single();
        Assert.Equal(HttpStatusCode.OK, (await RpcAsync(Client(secret), "tools/list")).Status);

        Assert.True(await App.WithScopeAsync(sp => sp.GetRequiredService<IAccessTokens>().RevokeAsync(Principal(id), token.Id, TestContext.Current.CancellationToken)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await RpcAsync(Client(secret), "tools/list")).Status);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var (browser, id) = await SignedInAsAsync(Roles.Admin);
        using var b = browser;
        var secret = await TokenAsync(id, AllScopes);
        Assert.Equal(HttpStatusCode.OK, (await RpcAsync(Client(secret), "tools/list")).Status);

        App.Clock.Advance(TimeSpan.FromDays(31));
        Assert.Equal(HttpStatusCode.Unauthorized, (await RpcAsync(Client(secret), "tools/list")).Status);
    }

    [Fact]
    public async Task Locking_the_account_revokes_its_tokens_for_good()
    {
        var (browser, id) = await SignedInAsAsync(Roles.Admin);
        var (other, otherId) = await SignedInAsAsync(Roles.Admin);
        using var b = browser;
        using var o = other;
        var secret = await TokenAsync(id, AllScopes);

        Assert.True((await App.WithScopeAsync(sp => sp.GetRequiredService<IUserManagementService>().LockAsync(Principal(otherId), id, TestContext.Current.CancellationToken))).Succeeded);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RpcAsync(Client(secret), "tools/list")).Status);

        // Unlocking does not bring the token back.
        Assert.True((await App.WithScopeAsync(sp => sp.GetRequiredService<IUserManagementService>().UnlockAsync(Principal(otherId), id, TestContext.Current.CancellationToken))).Succeeded);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RpcAsync(Client(secret), "tools/list")).Status);
    }

    [Fact]
    public async Task Account_without_two_factor_cannot_use_a_token()
    {
        var (browser, id) = await SignedInAsAsync(Roles.Admin);
        using var b = browser;
        var secret = await TokenAsync(id, AllScopes);

        await App.WithScopeAsync(async sp =>
        {
            var manager = sp.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Elocaro.Infrastructure.Identity.ApplicationUser>>();
            await manager.SetTwoFactorEnabledAsync((await manager.FindByIdAsync(id.ToString()))!, false);
            return true;
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await RpcAsync(Client(secret), "tools/list")).Status);
    }

    [Theory]
    [InlineData(Roles.Tenant)]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.Caretaker)]
    [InlineData(Roles.Tradesperson)]
    public async Task Other_roles_cannot_create_a_token(string role)
    {
        var (browser, id) = await SignedInAsAsync(role);
        using var b = browser;
        var result = await App.WithScopeAsync(sp => sp.GetRequiredService<IAccessTokens>().CreateAsync(Principal(id), "Test", ["read:masterdata"], 30, TestContext.Current.CancellationToken));
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Tool_call_returns_data_and_is_written_to_the_audit_log_without_content()
    {
        var (browser, id) = await SignedInAsAsync(Roles.Admin);
        using var b = browser;
        using var client = Client(await TokenAsync(id, AllScopes));

        var (status, body) = await RpcAsync(client, "tools/call", new { name = "list_properties", arguments = new { } });
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(Refused(body), body?.ToString());

        var entries = await App.WithScopeAsync(sp => sp.GetRequiredService<Elocaro.Infrastructure.Persistence.ElocaroDbContext>()
            .Set<AuditEntry>().Where(a => a.EntityType == "AccessToken" && a.Action == AuditAction.Read).ToListAsync(TestContext.Current.CancellationToken));
        var entry = Assert.Single(entries);
        Assert.StartsWith("MCP list_properties ", entry.Purpose, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Created_token_is_stored_as_hash_only()
    {
        var (browser, id) = await SignedInAsAsync(Roles.Admin);
        using var b = browser;
        var secret = await TokenAsync(id, AllScopes);

        var stored = await App.WithScopeAsync(sp => sp.GetRequiredService<Elocaro.Infrastructure.Persistence.ElocaroDbContext>()
            .Set<Elocaro.Infrastructure.Identity.AccessToken>().SingleAsync(TestContext.Current.CancellationToken));
        Assert.StartsWith("elo_", secret, StringComparison.Ordinal);
        Assert.NotEqual(secret, stored.TokenHash);
        Assert.Equal(Elocaro.Infrastructure.Identity.AccessToken.Hash(secret), stored.TokenHash);
        Assert.DoesNotContain(secret, stored.TokenStart, StringComparison.Ordinal);
    }

    private static bool Refused(JsonElement? body) =>
        body is null
        || body.Value.TryGetProperty("error", out _)
        || (body.Value.TryGetProperty("result", out var result) && result.TryGetProperty("isError", out var isError) && isError.GetBoolean());

    private async Task<Guid> AdminIdAsync()
    {
        var (browser, id) = await SignedInAsAsync(Roles.Admin);
        browser.Dispose();
        return id;
    }

    private static System.Security.Claims.ClaimsPrincipal AdminPrincipal(Guid id) => Principal(id);
}
