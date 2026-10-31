using System.Security.Claims;
using Elocaro.Application.Accounts;
using Elocaro.Application.Common;
using Elocaro.Application.Documents;
using Elocaro.Application.Emergency;
using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Domain.Admin;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Emergency;

/// <summary>Signed contracts embedded in the folder (the draft PDF needs Gotenberg).</summary>
public class EmergencyFolderDocumentsTests(PostgresFixture postgres, GotenbergFixture gotenberg) : IClassFixture<GotenbergFixture>, IAsyncLifetime
{
    private const string Password = "Das-ist-ein-langes-Passwort-2026";

    private static readonly ClaimsPrincipal Admin = new(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Admin)], "test"));

    private InfrastructureEnvironment environment = null!;
    private Guid lease;
    private Guid template;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?> { ["Pdf:GotenbergUrl"] = gotenberg.Url, ["FileStore:MaxSizeMb"] = "60" });
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        var house = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        var apartment = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct));
        var anna = Validate(await scope.ServiceProvider.GetRequiredService<IPersonService>().CreateAsync(
            new PersonInput { FirstName = "Anna", LastName = "Schmidt", Salutation = Elocaro.Domain.People.Salutation.Mrs }, null, Ct));
        lease = Validate(await scope.ServiceProvider.GetRequiredService<ILeaseService>().CreateAsync(
            new LeaseCreation { RentalUnitId = apartment, Start = new DateOnly(2026, 3, 1), TenantIds = [anna], BaseRent = 500m }, Ct));

        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        db.Add(new LandlordProfile("Max Mustermann", "Musterweg 1", "12345", "Musterstadt"));
        await db.SaveChangesAsync(Ct);
        var templates = scope.ServiceProvider.GetRequiredService<IDocumentTemplates>();
        await templates.InstallDefaultsAsync(Ct);
        template = (await templates.ListAsync(Ct)).Single(t => t.Name == "Mietvertrag Wohnraum").Id;
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Signed_contracts_are_embedded_only_when_asked_for()
    {
        var signed = "%PDF-1.4 unterschrieben"u8.ToArray();
        await ConcludeAsync(signed);

        var without = await CreateAsync(includeDocuments: false);
        var with = await CreateAsync(includeDocuments: true);

        Assert.Empty(EmergencyFolderTests.Decrypt(without.Content!, Password).GetProperty("files").EnumerateArray());
        var file = Assert.Single(EmergencyFolderTests.Decrypt(with.Content!, Password).GetProperty("files").EnumerateArray());
        Assert.Equal("Mietvertrag Lindenweg 3 Wohnung 1 Anna Schmidt.pdf", file.GetProperty("name").GetString());
        Assert.Equal("application/pdf", file.GetProperty("type").GetString());
        Assert.Equal(signed, Convert.FromBase64String(file.GetProperty("data").GetString()!));
    }

    [Fact]
    public async Task Embedded_documents_count_towards_the_size_limit_and_the_user_gets_a_clear_answer()
    {
        // One signed contract that alone exceeds the limit of the unencrypted content.
        var tooBig = new byte[(int)EmergencyFolderLimits.MaxContentBytes + 1024];
        "%PDF-1.4 gross"u8.CopyTo(tooBig);
        await ConcludeAsync(tooBig);

        var refused = await CreateAsync(includeDocuments: true);
        var without = await CreateAsync(includeDocuments: false);

        Assert.Equal(EmergencyFolderProblem.TooLarge, refused.Problem);
        Assert.Null(refused.Content);
        Assert.True(without.Succeeded);
    }

    private async Task ConcludeAsync(byte[] pdf)
    {
        var draft = Validate(await WithAsync<ILeaseContracts, Result>(c => c.CreateDraftAsync(lease, template, Ct)));
        using var content = new MemoryStream(pdf);
        var result = await WithAsync<ILeaseContracts, Result>(c => c.ConcludeAsync(draft, new SignedContractInput { SignedOn = new DateOnly(2026, 3, 2) }, "unterschrieben.pdf", content, Ct));
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
    }

    private Task<EmergencyFolderResult> CreateAsync(bool includeDocuments) =>
        WithAsync<IEmergencyFolder, EmergencyFolderResult>(f => f.CreateAsync(new EmergencyFolderRequest(Password, Password, includeDocuments), Admin, Ct));

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private async Task<T> WithAsync<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
