using System.Net;
using Elocaro.Application.Accounts;
using Elocaro.Web.Tests.Account;

namespace Elocaro.Web.Tests.AccessControl;

public class SectionsTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private static readonly string[] Paths = ["/verwaltung", "/verwaltung/benutzer", "/verwaltung/system/jobs", "/service", "/portal"];

    [Theory]
    [InlineData(Roles.Admin, "/verwaltung", "/verwaltung,/verwaltung/benutzer,/verwaltung/system/jobs")]
    [InlineData(Roles.Manager, "/verwaltung", "/verwaltung")]
    [InlineData(Roles.Caretaker, "/service", "/service")]
    [InlineData(Roles.Tradesperson, "/service", "/service")]
    [InlineData(Roles.Tenant, "/portal", "/portal")]
    public async Task Each_role_reaches_exactly_its_areas(string role, string startPage, string allowed)
    {
        var (browser, _) = await SignedInAsAsync(role);
        using var _ = browser;

        Assert.Equal(startPage, (await browser.GetAsync("/")).Path);

        var allowedPaths = allowed.Split(',');
        foreach (var path in Paths)
        {
            var response = await browser.GetAsync(path, followRedirects: false);
            if (allowedPaths.Contains(path))
            {
                Assert.True(response.Status == HttpStatusCode.OK, $"{role} sollte {path} sehen (Status {response.Status}).");
            }
            else
            {
                Assert.True(
                    response.Status == HttpStatusCode.Redirect && response.RedirectLocation!.AbsolutePath == "/konto/zugriff-verweigert",
                    $"{role} darf {path} nicht sehen (Status {response.Status}, Ziel {response.RedirectLocation}).");
            }
        }
    }

    [Fact]
    public async Task Manager_with_user_management_permission_may_manage_users_but_not_the_system()
    {
        var (browser, _) = await SignedInAsAsync(Roles.Manager, [AccessRight.MasterData, AccessRight.UserManagement]);
        using var _ = browser;

        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/verwaltung/benutzer", followRedirects: false)).Status);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/verwaltung/system/jobs", followRedirects: false)).Status);
    }
}
