using System.Net;
using Elocaro.Application.Accounts;
using Elocaro.Web.Tests.Account;

namespace Elocaro.Web.Tests.Mcp;

public class AccessTokenPageTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    protected override IDictionary<string, string?>? Configuration => new Dictionary<string, string?> { ["Mcp:Enabled"] = "true" };

    [Fact]
    public async Task Tenant_cannot_open_the_page()
    {
        var (tenant, _) = await SignedInAsAsync(Roles.Tenant);
        using var _ = tenant;

        var page = await tenant.GetAsync("/konto/sicherheit/zugriffstoken", followRedirects: false);
        Assert.Equal("/konto/zugriff-verweigert", page.RedirectLocation!.AbsolutePath);
    }
}

public class AccessTokensDisabledTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    [Fact]
    public async Task Endpoint_does_not_exist_and_the_page_explains_it_when_the_interface_is_off()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        using var response = await admin.Http.PostAsync(new Uri("/mcp", UriKind.Relative), content, TestContext.Current.CancellationToken);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);

        var page = await admin.GetAsync("/konto/sicherheit/zugriffstoken");
        Assert.Contains("nicht eingeschaltet", page.Text, StringComparison.Ordinal);
    }
}
