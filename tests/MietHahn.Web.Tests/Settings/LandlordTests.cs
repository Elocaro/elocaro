using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Admin;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.Settings;

public class LandlordTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    [Fact]
    public async Task Without_a_profile_the_page_asks_for_one()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        Assert.Contains("Noch kein Vermieterprofil hinterlegt", (await admin.GetAsync("/verwaltung/einstellungen/vermieter")).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manager_without_system_permission_has_no_access()
    {
        var (manager, _) = await SignedInAsAsync(Roles.Manager);
        using var _ = manager;

        var response = await manager.GetAsync("/verwaltung/einstellungen/vermieter", followRedirects: false);

        Assert.Equal(HttpStatusCode.Redirect, response.Status);
        Assert.Equal("/konto/zugriff-verweigert", response.RedirectLocation!.AbsolutePath);
    }
}
