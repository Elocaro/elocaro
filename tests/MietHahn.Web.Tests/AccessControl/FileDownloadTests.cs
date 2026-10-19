using System.Net;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Files;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.AccessControl;

public class FileDownloadTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private static readonly HashSet<Guid> SharedFiles = [];

    protected override void ConfigureServices(IServiceCollection services) => services.AddScoped<IFileShare, TestShare>();

    [Fact]
    public async Task Admin_downloads_files_with_secure_headers()
    {
        var id = await StoreFileAsync();
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        using var response = await admin.Http.GetAsync(new Uri($"/dateien/{id}", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        // PDFs ohne „sandbox“ (sonst zeigt der Browser sie nicht an), aber ohne Skripte und nur in der App einbettbar.
        Assert.StartsWith("default-src 'none';", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'self'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.StartsWith("%PDF", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);

        using var download = await admin.Http.GetAsync(new Uri($"/dateien/{id}?download=true", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal("Receipt.pdf", download.Content.Headers.ContentDisposition!.FileNameStar ?? download.Content.Headers.ContentDisposition.FileName);
    }

    [Fact]
    public async Task Tenant_without_share_gets_no_access()
    {
        var id = await StoreFileAsync();
        var (tenant, _) = await SignedInAsAsync(Roles.Tenant);
        using var _ = tenant;

        var response = await tenant.GetAsync($"/dateien/{id}", followRedirects: false);

        Assert.Equal("/konto/zugriff-verweigert", response.RedirectLocation!.AbsolutePath);
    }

    [Fact]
    public async Task Tenant_with_share_from_a_feature_module_gets_access()
    {
        var id = await StoreFileAsync();
        SharedFiles.Add(id);
        var (tenant, _) = await SignedInAsAsync(Roles.Tenant);
        using var _ = tenant;

        Assert.Equal(HttpStatusCode.OK, (await tenant.GetAsync($"/dateien/{id}", followRedirects: false)).Status);
    }

    [Fact]
    public async Task Without_sign_in_redirects_to_sign_in_and_unknown_files_are_not_found()
    {
        var id = await StoreFileAsync();
        using var anonymous = App.Browser();
        Assert.Equal("/konto/anmelden", (await anonymous.GetAsync($"/dateien/{id}", followRedirects: false)).RedirectLocation!.AbsolutePath);

        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/dateien/{Guid.CreateVersion7()}", followRedirects: false)).Status);
    }

    private Task<Guid> StoreFileAsync() => App.WithScopeAsync(async sp =>
    {
        using var pdf = new MemoryStream("%PDF-1.4\n%%EOF\n"u8.ToArray());
        return (await sp.GetRequiredService<IFileStore>().SaveAsync(pdf, "Receipt.pdf", TestContext.Current.CancellationToken)).Id;
    });

    private sealed class TestShare : IFileShare
    {
        public Task<bool> IsSharedAsync(Guid fileId, ClaimsPrincipal user, CancellationToken cancellationToken = default) =>
            Task.FromResult(user.IsInRole(Roles.Tenant) && SharedFiles.Contains(fileId));
    }
}
