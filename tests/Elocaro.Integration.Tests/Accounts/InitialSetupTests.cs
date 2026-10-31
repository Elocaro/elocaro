using Elocaro.Application.Accounts;
using Elocaro.Infrastructure.Identity;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Infrastructure.Setup;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elocaro.Integration.Tests.Accounts;

public class InitialSetupTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Password = "Langes-Passwort-2026";

    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => environment = await InfrastructureEnvironment.CreateAsync(postgres);

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Theory]
    [InlineData("")]
    [InlineData("falsch")]
    public async Task Without_the_correct_code_no_admin_is_created(string code)
    {
        var result = await CreateAdminAsync(code, "admin@example.de");

        Assert.False(result.Succeeded);
        Assert.Contains("Einrichtungscode", result.Errors.Single(), StringComparison.Ordinal);
        Assert.Equal(0, await UserCountAsync());
    }

    [Fact]
    public async Task Simultaneous_requests_create_exactly_one_admin()
    {
        var code = environment.Services.GetRequiredService<ISetupCode>().Value;

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => CreateAdminAsync(code, $"admin{i}@example.de"), Ct)));

        Assert.Single(results, r => r.Succeeded);
        Assert.Equal(7, results.Count(r => r.Errors.SequenceEqual(["Die Ersteinrichtung ist bereits abgeschlossen."])));
        Assert.Equal(1, await UserCountAsync());
        await using var scope = environment.Services.CreateAsyncScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().GetUsersInRoleAsync(Roles.Admin));
    }

    [Fact]
    public async Task Rejected_password_leaves_the_setup_possible()
    {
        var code = environment.Services.GetRequiredService<ISetupCode>().Value;

        Assert.False((await CreateAdminAsync(code, "admin@example.de", "kurz")).Succeeded);

        Assert.True((await CreateAdminAsync(code, "admin@example.de")).Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_configuration_a_random_code_is_generated(string? configured)
    {
        var first = new SetupCode(Options.Create(new SetupOptions { Code = configured }));
        var second = new SetupCode(Options.Create(new SetupOptions { Code = configured }));

        Assert.True(first.IsGenerated);
        Assert.True(first.Value.Length >= 32);
        Assert.NotEqual(first.Value, second.Value);
        Assert.False(first.Matches(""));
        Assert.False(first.Matches(null));
        Assert.True(first.Matches(first.Value));
    }

    [Fact]
    public void Configured_code_is_used_and_compared_without_surrounding_whitespace()
    {
        var code = new SetupCode(Options.Create(new SetupOptions { Code = " geheim " }));

        Assert.False(code.IsGenerated);
        Assert.True(code.Matches("geheim\n"));
        Assert.False(code.Matches("GEHEIM"));
    }

    [Fact]
    public async Task Generated_code_is_logged_only_while_no_account_exists()
    {
        var logged = new CapturingLoggerProvider();
        using var loggerFactory = new LoggerFactory([logged]);
        await using var capturing = await InfrastructureEnvironment.CreateAsync(postgres, configure: s => s.AddSingleton<ILoggerFactory>(loggerFactory));
        var code = capturing.Services.GetRequiredService<ISetupCode>().Value;

        await capturing.Services.AnnounceSetupCodeAsync(Ct);
        Assert.Contains(logged.Messages, m => m.Contains(code, StringComparison.Ordinal));

        logged.Messages.Clear();
        await using (var scope = capturing.Services.CreateAsyncScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<IAccountService>().CreateFirstAdminAsync(code, "admin@example.de", "Admin", Password, Ct)).Succeeded);
        }

        await capturing.Services.AnnounceSetupCodeAsync(Ct);
        Assert.DoesNotContain(logged.Messages, m => m.Contains(code, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Configured_code_is_never_logged()
    {
        var logged = new CapturingLoggerProvider();
        using var loggerFactory = new LoggerFactory([logged]);
        await using var configured = await InfrastructureEnvironment.CreateAsync(
            postgres, new Dictionary<string, string?> { ["Setup:Code"] = "fest-eingestellt" }, s => s.AddSingleton<ILoggerFactory>(loggerFactory));

        await configured.Services.AnnounceSetupCodeAsync(Ct);

        Assert.DoesNotContain(logged.Messages, m => m.Contains("fest-eingestellt", StringComparison.Ordinal));
    }

    private async Task<AccountResult> CreateAdminAsync(string code, string email, string password = Password)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAccountService>().CreateFirstAdminAsync(code, email, "Admin", password, Ct);
    }

    private async Task<int> UserCountAsync()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ElocaroDbContext>().Users.CountAsync(Ct);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider, ILogger
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Messages)
            {
                Messages.Add(formatter(state, exception));
            }
        }

        public void Dispose()
        {
        }
    }
}
