using System.Net;
using System.Net.Http.Json;
using Elocaro.Application.Accounts;

namespace Elocaro.Web.Tests.Account;

public class PushPagesTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    [Fact]
    public async Task Push_endpoints_need_a_sign_in_and_the_service_worker_is_public()
    {
        using var anonymous = App.Browser();

        Assert.NotEqual(HttpStatusCode.OK, (await anonymous.GetAsync("/push/schluessel", followRedirects: false)).Status);
        Assert.NotEqual(HttpStatusCode.OK, (await anonymous.GetAsync("/push/ausstehend", followRedirects: false)).Status);
        var worker = await anonymous.Http.GetAsync(new Uri("/sw.js", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, worker.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.Http.GetAsync(new Uri("/offline.html", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task A_signed_in_user_gets_the_key_subscribes_and_finds_the_page_in_the_account()
    {
        var ct = TestContext.Current.CancellationToken;
        var (tenant, _) = await SignedInAsAsync(Roles.Tenant);
        using var _ = tenant;

        var key = await tenant.Http.GetAsync(new Uri("/push/schluessel", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.OK, key.StatusCode);
        Assert.Equal(87, (await key.Content.ReadAsStringAsync(ct)).Length); // 65 bytes as base64url

        var bad = await tenant.Http.PostAsJsonAsync(new Uri("/push/abonnieren", UriKind.Relative), new { endpoint = "https://intern.example/x", p256dh = "a", auth = "b" }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var ok = await tenant.Http.PostAsJsonAsync(new Uri("/push/abonnieren", UriKind.Relative), new { endpoint = "https://fcm.googleapis.com/fcm/send/abc", p256dh = "key", auth = "secret" }, ct);
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        var status = await tenant.Http.PostAsJsonAsync(new Uri("/push/status", UriKind.Relative), new { endpoint = "https://fcm.googleapis.com/fcm/send/abc" }, ct);
        Assert.Contains("true", await status.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        Assert.Equal("[]", await tenant.Http.GetStringAsync(new Uri("/push/ausstehend", UriKind.Relative), ct));

        var page = await tenant.GetAsync("/konto/benachrichtigungen");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("Home-Bildschirm", page.Text);
        Assert.Contains((await tenant.GetAsync("/konto/sicherheit")).Document.QuerySelectorAll("a"), a => a.GetAttribute("href") == "/konto/benachrichtigungen");
    }
}
