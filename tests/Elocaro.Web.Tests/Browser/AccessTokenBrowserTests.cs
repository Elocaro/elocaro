using Microsoft.Playwright;

namespace Elocaro.Web.Tests.Browser;

/// <summary>Creating an access token in a real browser: scopes are clickable and do not overlap other fields.</summary>
public class AccessTokenBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    protected override IDictionary<string, string?>? Configuration => new Dictionary<string, string?> { ["Mcp:Enabled"] = "true" };

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

            var scope = admin.Locator("fieldset.token-scopes input[type=checkbox]").First;
            var days = admin.GetByLabel("Gültig für", new() { Exact = false });
            var scopeBox = await scope.BoundingBoxAsync();
            var daysBox = await days.BoundingBoxAsync();
            Assert.True(scopeBox!.Y + scopeBox.Height <= daysBox!.Y, "Scope checkbox overlaps the validity field.");

            await admin.GetByLabel("Name (z. B. Gerät oder Programm)").FillAsync("Browser-Test");
            await scope.CheckAsync();
            await admin.GetByRole(AriaRole.Button, new() { Name = "Token erstellen" }).ClickAsync();

            var secret = admin.Locator("#token-secret");
            await secret.WaitForAsync();
            Assert.StartsWith("elo_", await secret.InnerTextAsync(), StringComparison.Ordinal);
            await Assertions.Expect(admin.Locator("table.table")).ToContainTextAsync("Browser-Test");
        });
    }
}
