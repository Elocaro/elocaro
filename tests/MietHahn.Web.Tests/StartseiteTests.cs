using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MietHahn.Web.Tests;

public class StartseiteTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Startseite_wird_ausgeliefert()
    {
        using var client = factory.CreateClient();

        using var antwort = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Contains("text/html", antwort.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);
    }
}
