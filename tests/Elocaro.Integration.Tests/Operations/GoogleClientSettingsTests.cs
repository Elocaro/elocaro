using Elocaro.Application.Operations;
using Elocaro.Domain.Operations;
using Elocaro.Infrastructure.Operations;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Operations;

public class GoogleClientSettingsTests(PostgresFixture postgres)
{
    private const string Id = "123456-abc.apps.googleusercontent.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<T> InScopeAsync<T>(InfrastructureEnvironment environment, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    [Fact]
    public async Task Without_a_client_the_sign_in_is_not_available()
    {
        await using var environment = await InfrastructureEnvironment.CreateAsync(postgres);

        var view = await InScopeAsync(environment, sp => sp.GetRequiredService<IGoogleClientSettings>().GetAsync(Ct));
        var available = await InScopeAsync(environment, sp => sp.GetRequiredService<IGoogleDriveAuthorization>().IsAvailableAsync(Ct));

        Assert.Equal(GoogleClientSource.None, view.Source);
        Assert.False(available);
    }

    [Fact]
    public async Task A_client_from_the_environment_is_used_and_shown_as_such()
    {
        await using var environment = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?>
        {
            ["GoogleDrive:ClientId"] = "env-id",
            ["GoogleDrive:ClientSecret"] = "env-secret",
        });

        var view = await InScopeAsync(environment, sp => sp.GetRequiredService<IGoogleClientSettings>().GetAsync(Ct));
        var source = await InScopeAsync(environment, sp => sp.GetRequiredService<IGoogleClientSource>().ResolveAsync(Ct));

        Assert.Equal(new GoogleClientView(GoogleClientSource.Environment, "env-id"), view);
        Assert.Equal(("env-id", "env-secret"), source);
        Assert.True(await InScopeAsync(environment, sp => sp.GetRequiredService<IGoogleDriveAuthorization>().IsAvailableAsync(Ct)));
    }

    [Fact]
    public async Task A_client_entered_in_the_app_wins_over_the_environment_and_its_secret_is_encrypted()
    {
        await using var environment = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?>
        {
            ["GoogleDrive:ClientId"] = "env-id",
            ["GoogleDrive:ClientSecret"] = "env-secret",
        });

        var saved = await InScopeAsync(environment, sp => sp.GetRequiredService<IGoogleClientSettings>().SaveAsync(Id, "GOCSPX-geheim", Ct));

        Assert.True(saved.Succeeded, string.Join(' ', saved.Errors));
        Assert.Equal(new GoogleClientView(GoogleClientSource.App, Id), await InScopeAsync(environment, sp => sp.GetRequiredService<IGoogleClientSettings>().GetAsync(Ct)));
        Assert.Equal((Id, "GOCSPX-geheim"), await InScopeAsync(environment, sp => sp.GetRequiredService<IGoogleClientSource>().ResolveAsync(Ct)));
        var row = await InScopeAsync(environment, async sp => await sp.GetRequiredService<ElocaroDbContext>().Set<GoogleClientSetting>().AsNoTracking().SingleAsync(Ct));
        Assert.DoesNotContain("GOCSPX-geheim", row.ProtectedClientSecret, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_secret_keeps_the_saved_one_but_the_first_save_needs_it()
    {
        await using var environment = await InfrastructureEnvironment.CreateAsync(postgres);

        var withoutSecret = await InScopeAsync(environment, sp => sp.GetRequiredService<IGoogleClientSettings>().SaveAsync(Id, "  ", Ct));
        Assert.False(withoutSecret.Succeeded);
        Assert.Contains("Schlüssel fehlt", string.Join(' ', withoutSecret.Errors), StringComparison.Ordinal);

        await InScopeAsync(environment, sp => sp.GetRequiredService<IGoogleClientSettings>().SaveAsync(Id, "GOCSPX-eins", Ct));
        var changed = await InScopeAsync(environment, sp => sp.GetRequiredService<IGoogleClientSettings>().SaveAsync("999-neu.apps.googleusercontent.com", null, Ct));

        Assert.True(changed.Succeeded);
        Assert.Equal(("999-neu.apps.googleusercontent.com", "GOCSPX-eins"), await InScopeAsync(environment, sp => sp.GetRequiredService<IGoogleClientSource>().ResolveAsync(Ct)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("mit leerzeichen")]
    public async Task An_invalid_client_id_is_rejected(string id)
    {
        await using var environment = await InfrastructureEnvironment.CreateAsync(postgres);

        var result = await InScopeAsync(environment, sp => sp.GetRequiredService<IGoogleClientSettings>().SaveAsync(id, "GOCSPX-x", Ct));

        Assert.False(result.Succeeded);
        Assert.Equal(GoogleClientSource.None, (await InScopeAsync(environment, sp => sp.GetRequiredService<IGoogleClientSettings>().GetAsync(Ct))).Source);
    }

    [Fact]
    public async Task Removing_the_client_lets_the_environment_apply_again()
    {
        await using var environment = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?>
        {
            ["GoogleDrive:ClientId"] = "env-id",
            ["GoogleDrive:ClientSecret"] = "env-secret",
        });
        await InScopeAsync(environment, sp => sp.GetRequiredService<IGoogleClientSettings>().SaveAsync(Id, "GOCSPX-geheim", Ct));

        Assert.True((await InScopeAsync(environment, sp => sp.GetRequiredService<IGoogleClientSettings>().RemoveAsync(Ct))).Succeeded);

        Assert.Equal(new GoogleClientView(GoogleClientSource.Environment, "env-id"), await InScopeAsync(environment, sp => sp.GetRequiredService<IGoogleClientSettings>().GetAsync(Ct)));
        Assert.False((await InScopeAsync(environment, sp => sp.GetRequiredService<IGoogleClientSettings>().RemoveAsync(Ct))).Succeeded);
    }

    private static string BuiltInFile(string content)
    {
        var file = Path.Combine(Path.GetTempPath(), "elocaro-tests", Guid.NewGuid().ToString("N"), "google-client.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
        return file;
    }

    [Fact]
    public async Task The_client_built_into_the_image_is_used_when_nothing_else_is_set_and_the_other_sources_win()
    {
        var file = BuiltInFile("""{"clientId":"image-id","clientSecret":"image-secret"}""");
        try
        {
            await using var plain = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?> { ["GoogleDrive:BuiltInFile"] = file });
            Assert.Equal(new GoogleClientView(GoogleClientSource.BuiltIn, "image-id"), await InScopeAsync(plain, sp => sp.GetRequiredService<IGoogleClientSettings>().GetAsync(Ct)));
            Assert.Equal(("image-id", "image-secret"), await InScopeAsync(plain, sp => sp.GetRequiredService<IGoogleClientSource>().ResolveAsync(Ct)));
            Assert.True(await InScopeAsync(plain, sp => sp.GetRequiredService<IGoogleDriveAuthorization>().IsAvailableAsync(Ct)));

            // The environment beats the image ...
            await using var withEnvironment = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?>
            {
                ["GoogleDrive:BuiltInFile"] = file,
                ["GoogleDrive:ClientId"] = "env-id",
                ["GoogleDrive:ClientSecret"] = "env-secret",
            });
            Assert.Equal(new GoogleClientView(GoogleClientSource.Environment, "env-id"), await InScopeAsync(withEnvironment, sp => sp.GetRequiredService<IGoogleClientSettings>().GetAsync(Ct)));

            // ... and what is entered in the app beats both; removing it brings the image back
            await InScopeAsync(plain, sp => sp.GetRequiredService<IGoogleClientSettings>().SaveAsync(Id, "GOCSPX-eigen", Ct));
            Assert.Equal(new GoogleClientView(GoogleClientSource.App, Id), await InScopeAsync(plain, sp => sp.GetRequiredService<IGoogleClientSettings>().GetAsync(Ct)));
            await InScopeAsync(plain, sp => sp.GetRequiredService<IGoogleClientSettings>().RemoveAsync(Ct));
            Assert.Equal(GoogleClientSource.BuiltIn, (await InScopeAsync(plain, sp => sp.GetRequiredService<IGoogleClientSettings>().GetAsync(Ct))).Source);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
        }
    }

    [Theory]
    [InlineData("kaputt")]
    [InlineData("""{"clientId":"nur-die-id"}""")]
    [InlineData("""{"clientId":"","clientSecret":""}""")]
    public async Task A_broken_or_incomplete_file_in_the_image_counts_as_no_client(string content)
    {
        var file = BuiltInFile(content);
        try
        {
            await using var environment = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?> { ["GoogleDrive:BuiltInFile"] = file });

            Assert.Equal(GoogleClientSource.None, (await InScopeAsync(environment, sp => sp.GetRequiredService<IGoogleClientSettings>().GetAsync(Ct))).Source);
            Assert.False(await InScopeAsync(environment, sp => sp.GetRequiredService<IGoogleDriveAuthorization>().IsAvailableAsync(Ct)));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
        }
    }
}
