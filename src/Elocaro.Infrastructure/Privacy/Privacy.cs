using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elocaro.Application.Audit;
using Elocaro.Application.Common;
using Elocaro.Application.People;
using Elocaro.Application.Privacy;
using Elocaro.Domain.Common;
using Elocaro.Domain.People;
using Elocaro.Infrastructure.Audit;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Identity;
using Elocaro.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Elocaro.Infrastructure.Privacy;

internal sealed class Privacy(
    ElocaroDbContext db,
    IEnumerable<IPersonReference> referenceSources,
    IEnumerable<IPersonalDataSource> dataSources,
    UserManager<ApplicationUser> userManager,
    ICurrentUser currentUser,
    IOptions<PrivacyOptions> options,
    TimeProvider clock) : IPrivacyService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(), new PeriodJsonConverter() },
    };

    public async Task<byte[]?> AccessReportAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        // Serialize the entities themselves: new properties are thus included in the access response automatically.
        var person = await db.Set<Person>().Include(p => p.Addresses).Include(p => p.BankDetails).AsSplitQuery()
            .AsNoTracking().SingleOrDefaultAsync(p => p.Id == personId, cancellationToken);
        if (person is null)
        {
            return null;
        }

        // Soft-deleted entries (e.g. a removed IBAN) are stored data as well.
        var deletedAddresses = await db.Set<PersonAddress>().IgnoreQueryFilters().AsNoTracking().Where(a => a.PersonId == personId && a.Deleted).ToListAsync(cancellationToken);
        var deletedAccounts = await db.Set<BankDetails>().IgnoreQueryFilters().AsNoTracking().Where(b => b.PersonId == personId && b.Deleted).ToListAsync(cancellationToken);
        var childIds = person.Addresses.Select(a => a.Id).Concat(person.BankDetails.Select(b => b.Id))
            .Concat(deletedAddresses.Select(a => a.Id)).Concat(deletedAccounts.Select(b => b.Id)).Append(personId).ToList();
        var log = await db.Set<AuditEntry>().AsNoTracking()
            .Where(a => childIds.Contains(a.EntityId))
            .OrderBy(a => a.Timestamp)
            .Select(a => new { a.Timestamp, a.User, a.EntityType, Action = a.Action.ToString(), a.Purpose, Changes = a.ChangesJson })
            .ToListAsync(cancellationToken);

        var accessReport = new Dictionary<string, object?>
        {
            ["createdAt"] = clock.GetUtcNow(),
            ["note"] = "Auskunft nach Art. 15 DSGVO über alle in Elocaro zu dieser Person gespeicherten Daten.",
            ["person"] = person,
            ["deletedEntries"] = new { Addresses = deletedAddresses, BankDetails = deletedAccounts },
            ["userAccount"] = await AccountAsync(person.UserId),
            ["references"] = await ReferencesAsync([personId], cancellationToken),
            // Change log in plain text (including otherwise masked values such as IBAN): the person is entitled to it.
            ["changeLog"] = log.Select(p => new { p.Timestamp, p.User, p.EntityType, p.Action, p.Purpose, Changes = JsonElement.Parse(p.Changes) }),
        };
        foreach (var source in dataSources)
        {
            accessReport[source.Section] = await source.DataAsync(personId, cancellationToken);
        }

        db.Add(new AuditEntry(clock.GetUtcNow(), currentUser.Identifier, nameof(Person), personId, AuditAction.Read, "[]", "DSGVO-Auskunft erstellt"));
        await db.SaveChangesAsync(cancellationToken);
        return JsonSerializer.SerializeToUtf8Bytes(accessReport, Json);
    }

    public async Task<IReadOnlyList<AnonymizationSuggestion>> SuggestionsAsync(CancellationToken cancellationToken = default)
    {
        var today = GermanTime.Today(clock.GetUtcNow());
        var cutoffWithoutReference = clock.GetUtcNow().AddMonths(-options.Value.MonthsWithoutRelation);

        // Business contacts (tradespeople, utilities …) are not proposed.
        var people = await db.Set<Person>().AsNoTracking()
            .Where(p => p.AnonymizedAt == null && p.ContactType == ContactType.None)
            .Select(p => new { p.Id, p.FirstName, p.LastName, p.Company, p.Title, p.UserId, Entry = p.ModifiedAt ?? p.CreatedAt })
            .ToListAsync(cancellationToken);
        var references = await ReferencesAsync(people.Select(p => p.Id).ToList(), cancellationToken);

        var suggestions = new List<AnonymizationSuggestion>();
        foreach (var person in people)
        {
            var name = string.Join(' ', new[] { person.Title, person.FirstName, person.LastName }.Where(t => !string.IsNullOrWhiteSpace(t)));
            var own = references.Where(b => b.PersonId == person.Id).ToList();
            if (own.Count == 0)
            {
                if (person.Entry < cutoffWithoutReference && person.UserId is null)
                {
                    suggestions.Add(new(person.Id, name, $"Kein Vertrag oder Vorgang, seit über {options.Value.MonthsWithoutRelation} Monaten unverändert.", null));
                }

                continue;
            }

            if (own.Any(b => b.Active))
            {
                continue;
            }

            var lastReference = own.Max(b => b.EndsOn);
            if (lastReference is { } end && end.AddYears(options.Value.RetentionYears) < today)
            {
                suggestions.Add(new(person.Id, name,
                    string.Create(CultureInfo.InvariantCulture, $"Letzter Bezug endete am {end:dd.MM.yyyy}, Aufbewahrungsfrist ({options.Value.RetentionYears} Jahre) abgelaufen."),
                    end));
            }
        }

        return suggestions.OrderBy(v => v.LastRelation ?? DateOnly.MinValue).ToList();
    }

    public async Task<Result> AnonymizeAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        if ((await ReferencesAsync([personId], cancellationToken)).Any(b => b.Active))
        {
            return Result.Failed("Die Person hat noch aktive Verträge oder Vorgänge und kann nicht anonymisiert werden.");
        }

        var person = await db.Set<Person>().Include(p => p.Addresses).Include(p => p.BankDetails).AsSplitQuery()
            .SingleOrDefaultAsync(p => p.Id == personId, cancellationToken);
        if (person is null)
        {
            return Command.NotFound;
        }

        if (person.IsAnonymized)
        {
            return Result.Failed("Die Person ist bereits anonymisiert.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        person.Anonymize(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        // Soft-deleted addresses/accounts still contained the data: remove them permanently,
        // as well as the old values in the change log (the "who changed what and when" record is kept).
        var addresses = db.Set<PersonAddress>().IgnoreQueryFilters().Where(a => a.PersonId == personId);
        var accounts = db.Set<BankDetails>().IgnoreQueryFilters().Where(b => b.PersonId == personId);
        var ids = await addresses.Select(a => a.Id).Concat(accounts.Select(b => b.Id)).ToListAsync(cancellationToken);
        ids.Add(personId);
        await addresses.ExecuteDeleteAsync(cancellationToken);
        await accounts.ExecuteDeleteAsync(cancellationToken);
        await db.Set<AuditEntry>()
            .Where(a => ids.Contains(a.EntityId) && a.Action != AuditAction.Read)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ChangesJson, "[]"), cancellationToken);
        db.Add(new AuditEntry(clock.GetUtcNow(), currentUser.Identifier, nameof(Person), personId, AuditAction.Modified, "[]", "Anonymisiert (DSGVO)"));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok(personId);
    }

    private async Task<object?> AccountAsync(Guid? userId)
    {
        if (userId is null || await userManager.FindByIdAsync(userId.Value.ToString()) is not { } account)
        {
            return null;
        }

        return new
        {
            account.Email,
            account.DisplayName,
            Roles = await userManager.GetRolesAsync(account),
            account.CreatedAt,
            account.LastSignInAt,
            TwoFactorEnabled = account.TwoFactorEnabled,
            account.Locked,
        };
    }

    private async Task<List<PersonRelation>> ReferencesAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken)
    {
        var all = new List<PersonRelation>();
        foreach (var source in referenceSources)
        {
            all.AddRange(await source.ReferencesAsync(personIds, cancellationToken));
        }

        return all;
    }
}
