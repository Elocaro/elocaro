using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Audit;
using MietHahn.Application.Common;
using MietHahn.Application.People;
using MietHahn.Application.Privacy;
using MietHahn.Domain.Common;
using MietHahn.Domain.People;
using MietHahn.Infrastructure.Audit;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.People;

public class PeopleTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(InfrastructureEnvironment.StartTime.UtcDateTime);

    private readonly TestPersonalReferences references = new();
    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() =>
        environment = await InfrastructureEnvironment.CreateAsync(postgres, configure: s => s.AddSingleton<IPersonReference>(references));

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Create_tenant_with_address_and_multiple_IBANs()
    {
        var id = await AnnaAsync();

        Validate(await WithAsync(v => v.AddBankDetailsAsync(id, new BankDetailsInput { Iban = "DE89 3704 0044 0532 0130 00", AccountHolder = "Hans Schmidt" }, Ct)));
        var duplicate = await WithAsync(v => v.AddBankDetailsAsync(id, new BankDetailsInput { Iban = "de02120300000000202051" }, Ct));

        var person = (await WithAsync(v => v.GetAsync(id, Ct)))!;
        Assert.Equal("Dr. Anna Schmidt", person.DisplayName);
        Assert.Equal("Lindenweg 3, 50667 Köln", person.Address!.SingleLine);
        Assert.Equal(["DE02 1203 0000 0000 2020 51", "DE89 3704 0044 0532 0130 00"], person.BankDetails.Select(b => b.Iban));
        Assert.False(duplicate.Succeeded);
        Assert.Equal("Diese IBAN ist bereits hinterlegt.", duplicate.Errors.Single());
    }

    [Fact]
    public async Task A_bank_detail_can_be_corrected_but_not_into_a_duplicate_or_invalid_IBAN()
    {
        var id = await AnnaAsync();
        Validate(await WithAsync(v => v.AddBankDetailsAsync(id, new BankDetailsInput { Iban = "DE89 3704 0044 0532 0130 00" }, Ct)));
        var entry = (await WithAsync(v => v.GetAsync(id, Ct)))!.BankDetails.Single(b => b.Iban.StartsWith("DE89", StringComparison.Ordinal));

        Assert.False((await WithAsync(v => v.UpdateBankDetailsAsync(id, entry.Id, new BankDetailsInput { Iban = "DE00 0000 0000 0000 0000 00" }, Ct))).Succeeded);
        Assert.False((await WithAsync(v => v.UpdateBankDetailsAsync(id, entry.Id, new BankDetailsInput { Iban = "DE02 1203 0000 0000 2020 51" }, Ct))).Succeeded); // Anna's own IBAN from the setup
        Validate(await WithAsync(v => v.UpdateBankDetailsAsync(id, entry.Id, new BankDetailsInput { Iban = "DE89 3704 0044 0532 0130 00", AccountHolder = "Hans Schmidt", Bic = "cobadeff" }, Ct)));

        var saved = (await WithAsync(v => v.GetAsync(id, Ct)))!.BankDetails.Single(b => b.Id == entry.Id);
        Assert.Equal(("Hans Schmidt", "COBADEFF"), (saved.AccountHolder, saved.Bic));
    }

    [Fact]
    public async Task Opening_the_person_record_is_logged()
    {
        var id = await AnnaAsync();

        await WithAsync(v => v.GetAsync(id, Ct));

        await using var scope = environment.Services.CreateAsyncScope();
        var entry = await scope.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<AuditEntry>()
            .SingleAsync(a => a.EntityId == id && a.Action == AuditAction.Read, Ct);
        Assert.Equal("Personenakte geöffnet", entry.Purpose);
    }

    [Theory]
    [InlineData("schmidt", 1)]
    [InlineData("anna@", 1)]
    [InlineData("0221", 1)]
    [InlineData("DE89370400440532013000", 1)] // an IBAN without spaces finds the formatted one
    [InlineData("3704 0044", 1)]
    [InlineData("Sanitär", 1)]
    [InlineData("niemand", 0)]
    [InlineData("100%", 0)] // wildcards are not interpreted as patterns
    public async Task Search_by_name_contact_IBAN_and_trade(string text, int match)
    {
        var anna = await AnnaAsync();
        Validate(await WithAsync(v => v.AddBankDetailsAsync(anna, new BankDetailsInput { Iban = "DE89370400440532013000" }, Ct)));
        Validate(await WithAsync(v => v.CreateAsync(new PersonInput { LastName = "Klempner", Company = "Rohr & Co.", ContactType = ContactType.Tradesperson, Occupation = "Sanitär" }, null, Ct)));

        var result = await WithAsync(v => v.SearchAsync(new PersonQuery(text), Ct));

        Assert.Equal(match, result.Count);
    }

    [Fact]
    public async Task Filter_private_and_contacts_with_roles_from_relations()
    {
        var anna = await AnnaAsync();
        Validate(await WithAsync(v => v.CreateAsync(new PersonInput { LastName = "Stadtwerke", ContactType = ContactType.Utility, Occupation = "Wasser" }, null, Ct)));
        references.Add(new PersonRelation(anna, "Mieter", "Wohnung 1", true, null));

        var isPrivate = await WithAsync(v => v.SearchAsync(new PersonQuery(Filter: PersonFilter.Private), Ct));
        var contacts = await WithAsync(v => v.SearchAsync(new PersonQuery(Filter: PersonFilter.Contacts), Ct));

        Assert.Equal(["Mieter"], Assert.Single(isPrivate).Roles);
        Assert.Equal("Stadtwerke", Assert.Single(contacts).DisplayName);
    }

    [Fact]
    public async Task People_with_references_cannot_be_deleted()
    {
        var anna = await AnnaAsync();
        references.Add(new PersonRelation(anna, "Mieter", "Wohnung 1", false, Today.AddYears(-1)));

        Assert.False((await WithAsync(v => v.DeleteAsync(anna, Ct))).Succeeded);

        var prospect = Validate(await WithAsync(v => v.CreateAsync(new PersonInput { LastName = "Interessent" }, null, Ct)));
        Validate(await WithAsync(v => v.DeleteAsync(prospect, Ct)));
        Assert.Null(await WithAsync(v => v.GetAsync(prospect, Ct)));
    }

    [Fact]
    public async Task Access_invites_and_links_the_account()
    {
        var anna = await AnnaAsync();
        var withoutMail = Validate(await WithAsync(v => v.CreateAsync(new PersonInput { LastName = "Ohne Mail" }, null, Ct)));

        var result = await WithAsync(v => v.SetUpAccessAsync(anna, Ct));

        Validate(result);
        var person = (await WithAsync(v => v.GetAsync(anna, Ct)))!;
        Assert.Equal("anna@example.de", person.Access!.Email);
        Assert.Equal(Roles.Tenant, person.Access.Role);
        Assert.False((await WithAsync(v => v.SetUpAccessAsync(anna, Ct))).Succeeded);
        Assert.Equal("Für einen Zugang braucht die Person eine E-Mail-Adresse.", (await WithAsync(v => v.SetUpAccessAsync(withoutMail, Ct))).Errors.Single());

        // A second person with the same address must not take over the account.
        var second = Validate(await WithAsync(v => v.CreateAsync(new PersonInput { LastName = "Doppelt", Email = "anna@example.de" }, null, Ct)));
        Assert.False((await WithAsync(v => v.SetUpAccessAsync(second, Ct))).Succeeded);
    }

    [Fact]
    public async Task Invitation_names_the_landlord_and_the_access_shows_its_delivery_state()
    {
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<MietHahn.Application.Admin.ILandlordProfileService>().SaveAsync(
                new MietHahn.Application.Admin.LandlordProfileInput { Name = "Hausverwaltung Beispiel", Street = "Weg 1", PostalCode = "50667", City = "Köln" }, Ct);
        }

        var anna = await AnnaAsync();
        Assert.Null((await WithAsync(v => v.GetAsync(anna, Ct)))!.Access);
        Validate(await WithAsync(v => v.SetUpAccessAsync(anna, Ct)));

        var access = (await WithAsync(v => v.GetAsync(anna, Ct)))!.Access!;
        Assert.Null(access.LastSignInAt);
        Assert.Equal(MietHahn.Domain.Communication.MailStatus.Pending, access.Invitation!.Status);

        await using var read = environment.Services.CreateAsyncScope();
        var mails = await read.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<MietHahn.Domain.Communication.OutgoingMail>().AsNoTracking().ToListAsync(Ct);
        var mail = Assert.Single(mails);
        Assert.Contains("Hausverwaltung Beispiel", mail.Subject);
        Assert.Contains("Hausverwaltung Beispiel", mail.Html);
    }

    [Fact]
    public async Task Invitation_can_be_sent_again_until_the_person_has_signed_in()
    {
        var anna = await AnnaAsync();
        Assert.False((await WithAsync(v => v.ResendInvitationAsync(anna, Ct))).Succeeded);
        Validate(await WithAsync(v => v.SetUpAccessAsync(anna, Ct)));

        Validate(await WithAsync(v => v.ResendInvitationAsync(anna, Ct)));

        await using (var read = environment.Services.CreateAsyncScope())
        {
            var mails = await read.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<MietHahn.Domain.Communication.OutgoingMail>().AsNoTracking().ToListAsync(Ct);
            Assert.Equal(2, mails.Count);
            Assert.All(mails, m => Assert.Equal(["anna@example.de"], m.To));
        }

        var userId = (await WithAsync(v => v.GetAsync(anna, Ct)))!.Access!.UserId;
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<MietHahn.Infrastructure.Identity.ApplicationUser>>();
            var user = (await manager.FindByIdAsync(userId.ToString()))!;
            user.LastSignInAt = DateTimeOffset.UtcNow;
            await manager.UpdateAsync(user);
        }

        var late = await WithAsync(v => v.ResendInvitationAsync(anna, Ct));
        Assert.False(late.Succeeded);
        Assert.Contains("bereits angemeldet", late.Errors.Single());
        Assert.NotNull((await WithAsync(v => v.GetAsync(anna, Ct)))!.Access!.LastSignInAt);
    }

    [Fact]
    public async Task Gdpr_access_report_contains_all_stored_data()
    {
        var anna = await AnnaAsync();
        Validate(await WithAsync(v => v.AddBankDetailsAsync(anna, new BankDetailsInput { Iban = "DE89370400440532013000", AccountHolder = "Hans Schmidt", Bic = "COBADEFFXXX" }, Ct)));
        var removed = (await WithAsync(v => v.GetAsync(anna, Ct)))!.BankDetails[0].Id;
        Validate(await WithAsync(v => v.RemoveBankDetailsAsync(anna, removed, Ct)));
        Validate(await WithAsync(v => v.SetUpAccessAsync(anna, Ct)));
        references.Add(new PersonRelation(anna, "Mieter", "Wohnung 1, Lindenweg 3", true, null));

        var json = await AccessReportAsync(anna);

        var person = json.GetProperty("person");
        // Every property of the entities is included so that new fields are not accidentally missing.
        foreach (var name in Properties<Person>().Except(["addresses", "bankDetails"]))
        {
            Assert.True(person.TryGetProperty(name, out _), $"person.{name} fehlt in der Auskunft");
        }

        var address = person.GetProperty("addresses")[0];
        Assert.All(Properties<PersonAddress>(), n => Assert.True(address.TryGetProperty(n, out _), $"anschrift.{n} fehlt"));
        var account = person.GetProperty("bankDetails")[0];
        Assert.All(Properties<BankDetails>(), n => Assert.True(account.TryGetProperty(n, out _), $"bankverbindung.{n} fehlt"));
        Assert.Equal("DE89 3704 0044 0532 0130 00", account.GetProperty("iban").GetString()); // in plain text

        var text = json.GetRawText();
        Assert.Contains("1990-04-12", text, StringComparison.Ordinal);
        Assert.Contains("DE02 1203 0000 0000 2020 51", json.GetProperty("deletedEntries").GetProperty("bankDetails")[0].GetProperty("iban").GetString()!, StringComparison.Ordinal);
        Assert.Equal("anna@example.de", json.GetProperty("userAccount").GetProperty("email").GetString());
        Assert.Equal("Wohnung 1, Lindenweg 3", json.GetProperty("references")[0].GetProperty("description").GetString());
        Assert.Contains(json.GetProperty("changeLog").EnumerateArray(), e => e.GetProperty("entityType").GetString() == nameof(BankDetails));
        Assert.Equal("Anna", person.GetProperty("firstName").GetString());
    }

    [Fact]
    public async Task Suggestions_after_retention_period()
    {
        var active = await AnnaAsync();
        var former = Validate(await WithAsync(v => v.CreateAsync(new PersonInput { LastName = "Ehemalig" }, null, Ct)));
        var stillWithinDeadline = Validate(await WithAsync(v => v.CreateAsync(new PersonInput { LastName = "Noch in Frist" }, null, Ct)));
        var prospect = Validate(await WithAsync(v => v.CreateAsync(new PersonInput { LastName = "Interessent" }, null, Ct)));
        Validate(await WithAsync(v => v.CreateAsync(new PersonInput { LastName = "Stadtwerke", ContactType = ContactType.Utility }, null, Ct)));
        references.Add(new PersonRelation(active, "Mieter", "Wohnung 1", true, null));
        references.Add(new PersonRelation(former, "Mieter", "Wohnung 2", false, Today.AddYears(-11)));
        references.Add(new PersonRelation(stillWithinDeadline, "Mieter", "Wohnung 3", false, Today.AddYears(-3)));

        Assert.Equal([former], (await SuggestionsAsync()).Select(v => v.PersonId));

        environment.Clock.Advance(TimeSpan.FromDays(400));
        var later = await SuggestionsAsync();
        Assert.Contains(later, v => v.PersonId == prospect && v.Reason.StartsWith("Kein Vertrag", StringComparison.Ordinal));
        Assert.DoesNotContain(later, v => v.PersonId == active || v.PersonId == stillWithinDeadline);
        Assert.DoesNotContain(later, v => v.DisplayName == "Stadtwerke");
    }

    [Fact]
    public async Task Anonymize_removes_data_permanently_including_from_the_log()
    {
        var anna = await AnnaAsync();
        var bank = (await WithAsync(v => v.GetAsync(anna, Ct)))!.BankDetails[0].Id;
        Validate(await WithAsync(v => v.RemoveBankDetailsAsync(anna, bank, Ct)));
        references.Add(new PersonRelation(anna, "Mieter", "Wohnung 1", true, null));
        Assert.False((await WithPrivacyAsync(d => d.AnonymizeAsync(anna, Ct))).Succeeded);
        references.Clear();
        references.Add(new PersonRelation(anna, "Mieter", "Wohnung 1", false, Today.AddYears(-11)));

        Validate(await WithPrivacyAsync(d => d.AnonymizeAsync(anna, Ct)));

        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        var person = await db.Set<Person>().SingleAsync(p => p.Id == anna, Ct);
        Assert.True(person.IsAnonymized);
        Assert.False(await db.Set<PersonAddress>().IgnoreQueryFilters().AnyAsync(a => a.PersonId == anna, Ct));
        Assert.False(await db.Set<BankDetails>().IgnoreQueryFilters().AnyAsync(b => b.PersonId == anna, Ct));

        var log = await db.Set<AuditEntry>().Select(a => a.ChangesJson).ToListAsync(Ct);
        Assert.DoesNotContain(log, p => p.Contains("Schmidt", StringComparison.Ordinal) || p.Contains("Lindenweg", StringComparison.Ordinal) || p.Contains("anna@", StringComparison.Ordinal));
        Assert.Contains(await db.Set<AuditEntry>().Where(a => a.EntityId == anna).Select(a => a.Purpose).ToListAsync(Ct), z => z == "Anonymisiert (DSGVO)");

        Assert.False((await WithAsync(v => v.UpdateAsync(anna, new PersonInput { LastName = "Wieder da" }, Ct))).Succeeded);
        Assert.Empty(await WithAsync(v => v.SearchAsync(new PersonQuery(), Ct)));
    }

    private async Task<Guid> AnnaAsync()
    {
        var id = Validate(await WithAsync(v => v.CreateAsync(
            new PersonInput { Salutation = Salutation.Mrs, Title = "Dr.", FirstName = "Anna", LastName = "Schmidt", Email = "anna@example.de", Phone = "0221 123456", DateOfBirth = new DateOnly(1990, 4, 12), Notes = "Hat einen Hund" },
            new AddressInput { From = Today, Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" },
            Ct)));
        Validate(await WithAsync(v => v.AddBankDetailsAsync(id, new BankDetailsInput { Iban = "DE02120300000000202051" }, Ct)));
        return id;
    }

    private static IEnumerable<string> Properties<T>() =>
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name));

    private async Task<JsonElement> AccessReportAsync(Guid id)
    {
        var bytes = await WithPrivacyAsync(d => d.AccessReportAsync(id, Ct));
        return JsonDocument.Parse(bytes!).RootElement;
    }

    private Task<IReadOnlyList<AnonymizationSuggestion>> SuggestionsAsync() => WithPrivacyAsync(d => d.SuggestionsAsync(Ct));

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private async Task<T> WithAsync<T>(Func<IPersonService, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IPersonService>());
    }

    private async Task<T> WithPrivacyAsync<T>(Func<IPrivacyService, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IPrivacyService>());
    }

    private sealed class TestPersonalReferences : IPersonReference
    {
        private readonly List<PersonRelation> list = [];

        public void Add(PersonRelation reference) => list.Add(reference);

        public void Clear() => list.Clear();

        public Task<IReadOnlyList<PersonRelation>> ReferencesAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PersonRelation>>(list.Where(b => personIds.Contains(b.PersonId)).ToList());
    }
}
