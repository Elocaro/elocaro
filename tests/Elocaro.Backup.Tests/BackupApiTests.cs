using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Elocaro.Backup;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Backup.Tests;

public sealed class BackupApiTests : IDisposable
{
    private readonly BackupTestEnvironment environment = new();
    private readonly WebApplicationFactory<Program> factory;

    public BackupApiTests() =>
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton(environment.Settings);
            services.AddSingleton<IProcessRunner>(environment.Runner);
        }));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        factory.Dispose();
        environment.Dispose();
    }

    private HttpClient Client(string? token = "secret-token")
    {
        var client = factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    [Fact]
    public async Task Health_needs_no_token() =>
        Assert.Equal(HttpStatusCode.OK, (await Client(null).GetAsync(new Uri("/health", UriKind.Relative), Ct)).StatusCode);

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    public async Task Everything_else_needs_the_token(string? token) =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(token).GetAsync(new Uri("/status", UriKind.Relative), Ct)).StatusCode);

    [Fact]
    public async Task Test_endpoint_reports_the_repository_state()
    {
        environment.Runner.Script["restic cat"] = _ => new ProcessResult(12, string.Empty);

        var response = await Client().PostAsJsonAsync(new Uri("/test", UriKind.Relative), new TargetRequest(BackupTestEnvironment.Target), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"WrongPassword\"", await response.Content.ReadAsStringAsync(Ct), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_invalid_target_is_rejected_before_anything_runs()
    {
        var response = await Client().PostAsJsonAsync(new Uri("/backup", UriKind.Relative), new TargetRequest(new BackupTarget("/repo", "pw", new Dictionary<string, string>(StringComparer.Ordinal) { ["PATH"] = "/tmp" })), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(environment.Runner.Calls);
    }

    [Fact]
    public async Task Backup_is_accepted_and_visible_in_the_status()
    {
        environment.Runner.Script["restic backup"] = _ => new ProcessResult(0, "{\"snapshot_id\":\"49ccbcd74357a447\"}");

        var response = await Client().PostAsJsonAsync(new Uri("/backup", UriKind.Relative), new TargetRequest(BackupTestEnvironment.Target), Ct);
        await factory.Services.GetRequiredService<BackupCoordinator>().Running;

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Contains("49ccbcd74357a447", await Client().GetStringAsync(new Uri("/status", UriKind.Relative), Ct), StringComparison.Ordinal);
    }
}
