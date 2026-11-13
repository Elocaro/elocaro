using Elocaro.Application.Accounts;
using Elocaro.Application.Audit;
using Elocaro.Application.Common;
using Elocaro.Application.People;
using Elocaro.Domain.Common;
using Elocaro.Domain.People;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Identity;
using Elocaro.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.People;

internal sealed class PersonService(
    ElocaroDbContext db,
    IEnumerable<IPersonReference> referenceSources,
    IAccessLog accesses,
    IAccountService accounts,
    Elocaro.Application.Admin.ILandlordProfileService landlord,
    UserManager<ApplicationUser> userManager,
    TimeProvider clock) : IPersonService
{
    private DateOnly Today => GermanTime.Today(clock.GetUtcNow());

    public async Task<IReadOnlyList<PersonOverview>> SearchAsync(PersonQuery search, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        var query = WithDetails();
        if (!search.IncludeAnonymized)
        {
            query = query.Where(p => p.AnonymizedAt == null);
        }

        query = search.Filter switch
        {
            PersonFilter.Private => query.Where(p => p.ContactType == ContactType.None),
            PersonFilter.Contacts => query.Where(p => p.ContactType != ContactType.None),
            _ => query,
        };

        if (!string.IsNullOrWhiteSpace(search.Text))
        {
            var pattern = "%" + search.Text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            var ibanPattern = "%" + Iban.Compact(search.Text.Trim()) + "%";
            query = query.Where(p =>
                EF.Functions.ILike(p.LastName, pattern)
                || (p.FirstName != null && EF.Functions.ILike(p.FirstName, pattern))
                || (p.Company != null && EF.Functions.ILike(p.Company, pattern))
                || (p.Occupation != null && EF.Functions.ILike(p.Occupation, pattern))
                || (p.Email != null && EF.Functions.ILike(p.Email, pattern))
                || (p.Phone != null && EF.Functions.ILike(p.Phone, pattern))
                || (p.Mobile != null && EF.Functions.ILike(p.Mobile, pattern))
                || p.BankDetails.Any(b => EF.Functions.ILike(b.Iban.Replace(" ", ""), ibanPattern)));
        }

        var people = await query.OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ToListAsync(cancellationToken);
        var references = await ReferencesAsync(people.Select(p => p.Id).ToList(), cancellationToken);
        return people.Select(p => new PersonOverview(
            p.Id,
            p.DisplayName,
            p.SortName,
            p.ContactType,
            p.Occupation,
            p.Email,
            p.Phone ?? p.Mobile,
            p.AddressAt(Today)?.City,
            references.Where(b => b.PersonId == p.Id).Select(b => b.Role).Distinct().ToList(),
            p.UserId is not null,
            p.IsAnonymized)).ToList();
    }

    public async Task<PersonDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var person = await WithDetails().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (person is null)
        {
            return null;
        }

        await accesses.LogAsync(nameof(Person), id, "Personenakte geöffnet", cancellationToken);

        AccessView? access = null;
        if (person.UserId is { } userId && await userManager.FindByIdAsync(userId.ToString()) is { } account)
        {
            var role = (await userManager.GetRolesAsync(account)).FirstOrDefault() ?? "";
            access = new AccessView(userId, account.Email ?? "", role, account.Locked, account.LastSignInAt, await LastInvitationAsync(account.Email, cancellationToken));
        }

        return new PersonDetails(
            person.Id,
            Data(person),
            person.DisplayName,
            person.AddressAt(Today) is { } current ? new AddressVersion(current.Validity, current.SingleLine) : null,
            person.Addresses.OrderByDescending(a => a.Validity.From).Select(a => new AddressVersion(a.Validity, a.SingleLine)).ToList(),
            person.BankDetails.OrderBy(b => b.CreatedAt).Select(b => new BankDetailsView(b.Id, b.Iban, b.AccountHolder, b.Bic)).ToList(),
            access,
            await ReferencesAsync([id], cancellationToken),
            person.AnonymizedAt);
    }

    public Task<Result> CreateAsync(PersonInput data, AddressInput? address = null, CancellationToken cancellationToken = default) =>
        InputRules.Check(data, address) is { } invalid ? Task.FromResult(invalid) :
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(data);
            var person = new Person(data.LastName.Trim(), TrimToNull(data.FirstName));
            Apply(person, data);
            if (address is not null && !string.IsNullOrWhiteSpace(address.Street))
            {
                SetAddress(person, address);
            }

            db.Add(person);
            await db.SaveChangesAsync(cancellationToken);
            return person.Id;
        });

    public Task<Result> UpdateAsync(Guid id, PersonInput data, CancellationToken cancellationToken = default) =>
        InputRules.Check(data) is { } invalid ? Task.FromResult(invalid) :
        WithPersonAsync(id, person =>
        {
            ArgumentNullException.ThrowIfNull(data);
            person.Validate();
            Apply(person, data);
        }, cancellationToken);

    public Task<Result> SetAddressAsync(Guid id, AddressInput input, CancellationToken cancellationToken = default) =>
        WithPersonAsync(id, person => SetAddress(person, input), cancellationToken);

    public Task<Result> AddBankDetailsAsync(Guid id, BankDetailsInput input, CancellationToken cancellationToken = default) =>
        WithPersonAsync(id, person =>
        {
            ArgumentNullException.ThrowIfNull(input);
            person.AddBankDetails(input.Iban, input.AccountHolder, input.Bic);
        }, cancellationToken);

    public Task<Result> UpdateBankDetailsAsync(Guid id, Guid bankDetailsId, BankDetailsInput input, CancellationToken cancellationToken = default) =>
        WithPersonAsync(id, person =>
        {
            ArgumentNullException.ThrowIfNull(input);
            person.ChangeBankDetails(bankDetailsId, input.Iban, input.AccountHolder, input.Bic);
        }, cancellationToken);

    public Task<Result> RemoveBankDetailsAsync(Guid id, Guid bankDetailsId, CancellationToken cancellationToken = default) =>
        WithPersonAsync(id, person =>
        {
            var bank = person.BankDetails.SingleOrDefault(b => b.Id == bankDetailsId) ?? throw new NotFoundException();
            person.Validate();
            person.BankDetails.Remove(bank);
        }, cancellationToken);

    public async Task<Result> SetUpAccessAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var person = await db.Set<Person>().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (person is null)
        {
            return Command.NotFound;
        }

        if (person.UserId is not null)
        {
            return Result.Failed("Die Person hat bereits einen Zugang.");
        }

        if (string.IsNullOrWhiteSpace(person.Email))
        {
            return Result.Failed("Für einen Zugang braucht die Person eine E-Mail-Adresse.");
        }

        Guid userId;
        if (await userManager.FindByEmailAsync(person.Email) is { } existing)
        {
            if (await db.Set<Person>().AnyAsync(p => p.UserId == existing.Id, cancellationToken))
            {
                return Result.Failed("Das Konto mit dieser E-Mail-Adresse gehört bereits zu einer anderen Person.");
            }

            userId = existing.Id;
        }
        else
        {
            // A person who owns a property and is not a tenant gets the owner access (read-only view of their share).
            var owner = await db.Set<Elocaro.Domain.Properties.PropertyOwner>().AnyAsync(o => o.PersonId == person.Id, cancellationToken)
                && !await db.Set<Elocaro.Domain.Leases.Lease>().AnyAsync(v => v.Parties.Any(p => p.PersonId == person.Id), cancellationToken);
            var role = person.ContactType switch
            {
                ContactType.Caretaker => Roles.Caretaker,
                ContactType.Tradesperson => Roles.Tradesperson,
                _ => owner ? Roles.Owner : Roles.Tenant,
            };
            var invitation = await accounts.InviteAsync(person.Email, person.DisplayName, role, await LandlordNameAsync(cancellationToken), cancellationToken);
            if (!invitation.Succeeded)
            {
                return Result.Failed([.. invitation.Errors]);
            }

            userId = invitation.UserId!.Value;
        }

        person.UserId = userId;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok(userId);
    }

    public async Task<Result> ResendInvitationAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var person = await db.Set<Person>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (person is null)
        {
            return Command.NotFound;
        }

        if (person.UserId is not { } userId)
        {
            return Result.Failed("Die Person hat noch keinen Zugang.");
        }

        var result = await accounts.ResendInvitationAsync(userId, await LandlordNameAsync(cancellationToken), cancellationToken);
        return result.Succeeded ? Result.Ok(userId) : Result.Failed([.. result.Errors]);
    }

    private async Task<string?> LandlordNameAsync(CancellationToken cancellationToken) => (await landlord.GetAsync(cancellationToken))?.Name;

    private async Task<InvitationDelivery?> LastInvitationAsync(string? email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var recent = await db.Set<Elocaro.Domain.Communication.OutgoingMail>().AsNoTracking()
            .Where(m => m.Template != null && m.Template.StartsWith("invitation"))
            .OrderByDescending(m => m.CreatedAt).Take(50).ToListAsync(cancellationToken);
        var mail = recent.FirstOrDefault(m => m.To.Contains(email, StringComparer.OrdinalIgnoreCase));
        return mail is null ? null : new InvitationDelivery(mail.Status, mail.SentAt ?? mail.CreatedAt);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if ((await ReferencesAsync([id], cancellationToken)).Count > 0)
        {
            return Result.Failed("Die Person ist mit Verträgen oder anderen Vorgängen verknüpft. Nach Ablauf der Aufbewahrungsfrist kann sie anonymisiert werden.");
        }

        return await WithPersonAsync(id, person => db.Remove(person), cancellationToken);
    }

    public async Task<IReadOnlyList<PersonOption>> OptionsAsync(CancellationToken cancellationToken = default)
    {
        var people = await WithDetails().Where(p => p.AnonymizedAt == null).OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ToListAsync(cancellationToken);
        return people.Select(p => new PersonOption(p.Id, p.DisplayName, p.AddressAt(Today)?.City)).ToList();
    }

    internal async Task<IReadOnlyList<PersonRelation>> ReferencesAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken)
    {
        var all = new List<PersonRelation>();
        foreach (var source in referenceSources)
        {
            all.AddRange(await source.ReferencesAsync(personIds, cancellationToken));
        }

        return all;
    }

    private IQueryable<Person> WithDetails() =>
        db.Set<Person>().Include(p => p.Addresses).Include(p => p.BankDetails).AsSplitQuery();

    private Task<Result> WithPersonAsync(Guid id, Action<Person> action, CancellationToken cancellationToken) =>
        Command.ExecuteAsync(async () =>
        {
            var person = await WithDetails().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw new NotFoundException();
            action(person);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        }, overlap: "Für diesen Tag gibt es bereits eine Anschrift.");

    private static void SetAddress(Person person, AddressInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        person.SetAddress(
            input.From ?? throw new RuleViolationException("Bitte angeben, ab wann die Anschrift gilt."),
            input.Street,
            input.PostalCode,
            input.City,
            input.AddressLine2,
            input.Country ?? "Deutschland");
    }

    private static PersonInput Data(Person p) => new()
    {
        Salutation = p.Salutation,
        Title = p.Title,
        FirstName = p.FirstName,
        LastName = p.LastName,
        Company = p.Company,
        ContactType = p.ContactType,
        Occupation = p.Occupation,
        DateOfBirth = p.DateOfBirth,
        Email = p.Email,
        Phone = p.Phone,
        Mobile = p.Mobile,
        Notes = p.Notes,
    };

    private static void Apply(Person p, PersonInput d)
    {
        p.Salutation = d.Salutation;
        p.Title = TrimToNull(d.Title);
        p.FirstName = TrimToNull(d.FirstName);
        p.LastName = d.LastName.Trim();
        p.Company = TrimToNull(d.Company);
        p.ContactType = d.ContactType;
        p.Occupation = TrimToNull(d.Occupation);
        p.DateOfBirth = d.DateOfBirth;
        p.Email = TrimToNull(d.Email);
        p.Phone = TrimToNull(d.Phone);
        p.Mobile = TrimToNull(d.Mobile);
        p.Notes = TrimToNull(d.Notes);
    }

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
