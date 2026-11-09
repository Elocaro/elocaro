using Microsoft.Playwright;

namespace Elocaro.Web.Tests.Browser;

/// <summary>Creating an access token in a real browser: scopes are clickable and do not overlap other fields.</summary>
public class AccessTokenBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    protected override IDictionary<string, string?>? Configuration => new Dictionary<string, string?> { ["Mcp:Enabled"] = "true" };

    [Fact]
    public async Task Secret_is_shown_once_and_the_token_can_be_revoked()
    {
        var admin = await SignedInAsAdminAsync(mobile: false);
        await WithScreenshotOnFailureAsync(admin, async () =>
        {
            await admin.GotoAsync(BaseUrl + "/konto/sicherheit/zugriffstoken");
            await admin.GetByRole(AriaRole.Button, new() { Name = "Token erstellen" }).First.ClickAsync();
            var dialog = admin.Locator("dialog#token-dialog");
            await dialog.GetByLabel("Name (z. B. Gerät oder Programm)").FillAsync("Claude Code");
            await dialog.GetByLabel("Stammdaten lesen", new() { Exact = false }).CheckAsync();
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Token erstellen" }).ClickAsync();
            await admin.Locator("#token-secret").WaitForAsync();

            await admin.GotoAsync(BaseUrl + "/konto/sicherheit/zugriffstoken");
            Assert.Equal(0, await admin.Locator("#token-secret").CountAsync());
            var card = admin.Locator(".token-card", new() { HasText = "Claude Code" });
            await Assertions.Expect(card).ToContainTextAsync("Stammdaten");

            await card.GetByLabel("Token Claude Code widerrufen", new() { Exact = true }).First.ClickAsync();
            await card.GetByRole(AriaRole.Button, new() { Name = "Token Claude Code widerrufen (bestätigen)" }).ClickAsync();
            await Assertions.Expect(admin.Locator(".token-card", new() { HasText = "Claude Code" })).ToContainTextAsync("widerrufen");
        });
    }

    [Fact]
    public async Task Dialog_opens_again_with_the_message_when_the_name_is_missing()
    {
        var admin = await SignedInAsAdminAsync(mobile: false);
        await WithScreenshotOnFailureAsync(admin, async () =>
        {
            await admin.GotoAsync(BaseUrl + "/konto/sicherheit/zugriffstoken");
            await admin.GetByRole(AriaRole.Button, new() { Name = "Token erstellen" }).First.ClickAsync();
            var dialog = admin.Locator("dialog#token-dialog");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Token erstellen" }).ClickAsync();
            await Assertions.Expect(dialog).ToBeVisibleAsync();
            await Assertions.Expect(dialog).ToContainTextAsync("Bitte einen Namen angeben.");
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Admin_picks_a_scope_and_creates_a_token(bool mobile)
    {
        var admin = await SignedInAsAdminAsync(mobile);
        await WithScreenshotOnFailureAsync(admin, async () =>
        {
            await admin.GotoAsync(BaseUrl + "/konto/sicherheit/zugriffstoken");
            await NoHorizontalScrollAsync(admin);

            var dialog = admin.Locator("dialog#token-dialog");
            await Assertions.Expect(dialog).ToBeHiddenAsync();
            await admin.GetByRole(AriaRole.Button, new() { Name = "Token erstellen" }).First.ClickAsync();
            await Assertions.Expect(dialog).ToBeVisibleAsync();

            var scope = admin.Locator("fieldset.token-scopes input[type=checkbox]").First;
            var days = admin.GetByLabel("Gültig für", new() { Exact = false });
            var scopeBox = await scope.BoundingBoxAsync();
            var daysBox = await days.BoundingBoxAsync();
            Assert.True(scopeBox!.Y + scopeBox.Height <= daysBox!.Y, "Scope checkbox overlaps the validity field.");

            await admin.GetByLabel("Name (z. B. Gerät oder Programm)").FillAsync("Browser-Test");
            await scope.CheckAsync();
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Token erstellen" }).ClickAsync();

            var secret = admin.Locator("#token-secret");
            await secret.WaitForAsync();
            var shown = await secret.InnerTextAsync();
            Assert.StartsWith("elo_", shown, StringComparison.Ordinal);
            await admin.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
            await admin.GetByRole(AriaRole.Button, new() { Name = "Kopieren" }).ClickAsync();
            await Assertions.Expect(admin.GetByRole(AriaRole.Button, new() { Name = "Kopiert" })).ToBeVisibleAsync();
            Assert.Equal(shown.Trim(), await admin.EvaluateAsync<string>("navigator.clipboard.readText()"));
            await Assertions.Expect(dialog).ToBeHiddenAsync();
            await Assertions.Expect(admin.Locator(".token-list")).ToContainTextAsync("Browser-Test");
        });
    }
}
