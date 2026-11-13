using Elocaro.Application.Common;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Properties;

/// <summary>The rules on the input classes hold without a form: a caller like the MCP interface does not go through the validation of the browser.</summary>
public class InputRulesTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => environment = await InfrastructureEnvironment.CreateAsync(postgres);

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task A_property_with_an_invalid_postal_code_is_refused_by_the_service()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();

        var result = await properties.CreateAsync(new PropertyInput { Name = "Haus", Street = "Weg 1", PostalCode = "123", City = "Köln" }, Ct);

        Assert.False(result.Succeeded);
        Assert.Contains("Die Postleitzahl hat 5 Ziffern.", result.Errors);
        Assert.Empty(await properties.ListAsync(null, Ct));
    }

    [Fact]
    public async Task A_person_without_a_last_name_or_with_a_bad_email_is_refused()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var people = scope.ServiceProvider.GetRequiredService<IPersonService>();

        var result = await people.CreateAsync(new PersonInput { LastName = "", Email = "keine-mail" }, null, Ct);

        Assert.False(result.Succeeded);
        Assert.True(result.Errors.Count >= 2, string.Join(" | ", result.Errors));
    }
}
