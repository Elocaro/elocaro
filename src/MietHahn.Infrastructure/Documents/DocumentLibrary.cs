using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Accounts;
using MietHahn.Application.Common;
using MietHahn.Application.Documents;
using MietHahn.Application.Files;
using MietHahn.Application.People;
using MietHahn.Application.Todos;
using MietHahn.Domain.Common;
using MietHahn.Domain.Documents;
using MietHahn.Domain.Files;
using MietHahn.Domain.Leases;
using MietHahn.Domain.People;
using MietHahn.Domain.Properties;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Leases;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Documents;

internal sealed class DocumentLibrary(MietHahnDbContext db, TimeProvider clock) : IDocumentLibrary
{
    public async Task<IReadOnlyList<DocumentRow>> SearchAsync(DocumentQuery search, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        var query = db.Set<Document>().AsNoTracking();
        if (search.Category is { } category)
        {
            query = query.Where(d => d.Category == category);
        }

        if (search.PropertyId is { } property)
        {
            // A property also includes the documents of its units and leases.
            var units = db.Set<Unit>().Where(e => e.PropertyId == property).Select(e => e.Id);
            var leases = db.Set<Lease>().Where(v => units.Contains(v.UnitId)).Select(v => v.Id);
            query = query.Where(d => d.PropertyId == property || (d.UnitId != null && units.Contains(d.UnitId.Value)) || (d.LeaseId != null && leases.Contains(d.LeaseId.Value)));
        }

        if (search.UnitId is { } unit)
        {
            var leases = db.Set<Lease>().Where(v => v.UnitId == unit).Select(v => v.Id);
            query = query.Where(d => d.UnitId == unit || (d.LeaseId != null && leases.Contains(d.LeaseId.Value)));
        }

        if (search.LeaseId is { } lease)
        {
            query = query.Where(d => d.LeaseId == lease);
        }

        if (search.PersonId is { } person)
        {
            var leases = db.Set<LeaseParty>().Where(p => p.PersonId == person).Select(p => p.LeaseId);
            query = query.Where(d => d.PersonId == person || (d.LeaseId != null && leases.Contains(d.LeaseId.Value)));
        }

        if (!string.IsNullOrWhiteSpace(search.Text))
        {
            var pattern = "%" + search.Text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            var files = db.Set<StoredFile>().Where(f => EF.Functions.ILike(f.Name, pattern)).Select(f => f.Id);
            query = query.Where(d => EF.Functions.ILike(d.Title, pattern)
                || d.Tags.Any(t => EF.Functions.ILike(t, pattern))
                || (d.Note != null && EF.Functions.ILike(d.Note, pattern))
                || files.Contains(d.FileId));
        }

        var documents = await query.OrderByDescending(d => d.Date ?? DateOnly.MinValue).ThenByDescending(d => d.CreatedAt).ToListAsync(cancellationToken);
        return await RowsAsync(documents, cancellationToken);
    }

    public async Task<DocumentDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await db.Set<Document>().AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null)
        {
            return null;
        }

        var file = await db.Set<StoredFile>().AsNoTracking().SingleAsync(f => f.Id == document.FileId, cancellationToken);
        return new DocumentDetails((await RowsAsync([document], cancellationToken))[0], Data(document), file.Name, document.CreatedAt, document.CreatedBy);
    }

    public async Task<Result> CreateAsync(Guid fileId, DocumentInput data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!await db.Set<StoredFile>().AnyAsync(f => f.Id == fileId, cancellationToken))
        {
            return Result.Failed("Die Datei wurde nicht gefunden.");
        }

        return await Command.ExecuteAsync(async () =>
        {
            var document = new Document(fileId, data.Title.Trim(), data.Category);
            await ApplyAsync(document, data, cancellationToken);
            db.Add(document);
            await db.SaveChangesAsync(cancellationToken);
            return document.Id;
        });
    }

    public Task<Result> UpdateAsync(Guid id, DocumentInput data, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(data);
            var document = await db.Set<Document>().SingleOrDefaultAsync(d => d.Id == id, cancellationToken) ?? throw new NotFoundException();
            await ApplyAsync(document, data, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var document = await db.Set<Document>().SingleOrDefaultAsync(d => d.Id == id, cancellationToken) ?? throw new NotFoundException();
            db.Remove(document);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public async Task<IReadOnlyList<DocumentRow>> SharedAsync(ClaimsPrincipal user, Guid? propertyId = null, CancellationToken cancellationToken = default)
    {
        var visible = await DocumentShares.VisibleAsync(db, user, GermanTime.Today(clock.GetUtcNow()), cancellationToken);
        var documents = await visible.OrderByDescending(d => d.Date ?? DateOnly.MinValue).ThenByDescending(d => d.CreatedAt).ToListAsync(cancellationToken);
        var lines = await RowsAsync(documents, cancellationToken);
        if (propertyId is null)
        {
            return lines;
        }

        var toProperty = (await SearchAsync(new DocumentQuery(PropertyId: propertyId), cancellationToken)).Select(z => z.Id).ToHashSet();
        return lines.Where(z => toProperty.Contains(z.Id)).ToList();
    }

    public async Task<ReferenceOptions> ReferenceOptionsAsync(CancellationToken cancellationToken = default)
    {
        var properties = await db.Set<Property>().AsNoTracking().OrderBy(o => o.Name).Select(o => new ReferenceOption(o.Id, o.Name)).ToListAsync(cancellationToken);
        var rentalUnits = await RentalUnit.GetAsync(db, cancellationToken);
        var units = rentalUnits.Values.Where(m => m.RoomId is null).Select(m => new ReferenceOption(m.Id, $"{m.Name} ({m.Property})")).ToList();
        var leases = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).ToListAsync(cancellationToken);
        var personIds = leases.SelectMany(v => v.Parties.Select(p => p.PersonId)).Distinct().ToList();
        var people = await db.Set<Person>().AsNoTracking().Where(p => p.AnonymizedAt == null).OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ToListAsync(cancellationToken);
        var names = people.ToDictionary(p => p.Id, p => p.DisplayName);
        return new ReferenceOptions(
            properties,
            units,
            leases.OrderByDescending(v => v.Start).Select(v => new ReferenceOption(v.Id,
                $"{rentalUnits.GetValueOrDefault(v.RentalUnitId)?.Name}: {LeaseService.TenantText(v, names)} (ab {v.Start:dd.MM.yyyy})")).ToList(),
            people.Select(p => new ReferenceOption(p.Id, p.DisplayName)).ToList());
    }

    private async Task ApplyAsync(Document document, DocumentInput data, CancellationToken cancellationToken)
    {
        document.Title = data.Title.Trim();
        document.Category = data.Category;
        document.Date = data.Date;
        document.ExpiresOn = data.ExpiresOn;
        document.Note = string.IsNullOrWhiteSpace(data.Note) ? null : data.Note.Trim();
        document.SharedWithTenants = data.SharedWithTenants;
        document.SharedWithCaretakers = data.SharedWithCaretakers;
        document.SetTags((data.Tags ?? "").Split(',', ';'));
        document.SetReference(data.PropertyId, data.UnitId, data.LeaseId, data.PersonId);
        document.Validate();

        if ((data.PropertyId is { } o && !await db.Set<Property>().AnyAsync(x => x.Id == o, cancellationToken))
            || (data.UnitId is { } e && !await db.Set<Unit>().AnyAsync(x => x.Id == e, cancellationToken))
            || (data.LeaseId is { } v && !await db.Set<Lease>().AnyAsync(x => x.Id == v, cancellationToken))
            || (data.PersonId is { } p && !await db.Set<Person>().AnyAsync(x => x.Id == p, cancellationToken)))
        {
            throw new RuleViolationException("Ein gewählter Bezug wurde nicht gefunden.");
        }
    }

    private async Task<List<DocumentRow>> RowsAsync(List<Document> documents, CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
        {
            return [];
        }

        var fileIds = documents.Select(d => d.FileId).ToList();
        var files = await db.Set<StoredFile>().AsNoTracking().Where(f => fileIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, cancellationToken);
        var properties = await db.Set<Property>().AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, cancellationToken);
        var rentalUnits = await RentalUnit.GetAsync(db, cancellationToken);
        var leaseIds = documents.Where(d => d.LeaseId is not null).Select(d => d.LeaseId!.Value).Distinct().ToList();
        var leases = await db.Set<Lease>().AsNoTracking().Where(v => leaseIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.RentalUnitId, cancellationToken);
        var personIds = documents.Where(d => d.PersonId is not null).Select(d => d.PersonId!.Value).Distinct().ToList();
        var people = (await db.Set<Person>().AsNoTracking().Where(p => personIds.Contains(p.Id)).ToListAsync(cancellationToken)).ToDictionary(p => p.Id, p => p.DisplayName);

        return documents.Select(d =>
        {
            var reference = new List<string>();
            if (d.PropertyId is { } o)
            {
                reference.Add(properties.GetValueOrDefault(o, "Objekt"));
            }

            if (d.UnitId is { } e)
            {
                reference.Add(rentalUnits.GetValueOrDefault(e)?.Name ?? "Einheit");
            }

            if (d.LeaseId is { } v)
            {
                reference.Add("Vertrag " + (leases.TryGetValue(v, out var m) ? rentalUnits.GetValueOrDefault(m)?.Name : ""));
            }

            if (d.PersonId is { } p)
            {
                reference.Add(people.GetValueOrDefault(p, "Person"));
            }

            var file = files[d.FileId];
            return new DocumentRow(d.Id, d.FileId, d.Title, d.Category, d.Date, d.ExpiresOn, d.Tags, string.Join(" · ", reference), file.MimeType, file.Size, d.SharedWithTenants, d.SharedWithCaretakers);
        }).ToList();
    }

    private static DocumentInput Data(Document d) => new()
    {
        Title = d.Title,
        Category = d.Category,
        Date = d.Date,
        ExpiresOn = d.ExpiresOn,
        Tags = string.Join(", ", d.Tags),
        Note = d.Note,
        PropertyId = d.PropertyId,
        UnitId = d.UnitId,
        LeaseId = d.LeaseId,
        PersonId = d.PersonId,
        SharedWithTenants = d.SharedWithTenants,
        SharedWithCaretakers = d.SharedWithCaretakers,
    };
}

/// <summary>
/// Which released documents a tenant or caretaker may see. Tenants: documents of their leases and of their
/// person, plus those of the units/properties they currently live in. Caretakers: documents of their assigned properties.
/// </summary>
internal static class DocumentShares
{
    public static async Task<IQueryable<Document>> VisibleAsync(MietHahnDbContext db, ClaimsPrincipal user, DateOnly today, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var none = db.Set<Document>().Where(_ => false);
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return none;
        }

        var documents = db.Set<Document>().AsNoTracking();
        if (user.IsInRole(Roles.Tenant))
        {
            var personIds = await db.Set<Person>().Where(p => p.UserId == userId).Select(p => p.Id).ToListAsync(cancellationToken);
            if (personIds.Count == 0)
            {
                return none;
            }

            var leases = db.Set<Lease>().Where(v => v.Parties.Any(p => personIds.Contains(p.PersonId) && p.Role == PartyRole.Tenant));
            var leaseIds = leases.Select(v => v.Id);
            var current = leases.Where(v => v.Term.From <= today && (v.Term.To == null || v.Term.To >= today));
            var unitIds = current.Select(v => v.UnitId);
            var propertyIds = db.Set<Unit>().Where(e => unitIds.Contains(e.Id)).Select(e => e.PropertyId);
            return documents.Where(d => d.SharedWithTenants && (
                (d.PersonId != null && personIds.Contains(d.PersonId.Value))
                || (d.LeaseId != null && leaseIds.Contains(d.LeaseId.Value))
                || (d.UnitId != null && unitIds.Contains(d.UnitId.Value))
                || (d.PropertyId != null && propertyIds.Contains(d.PropertyId.Value))));
        }

        if (user.IsInRole(Roles.Caretaker))
        {
            var propertyIds = db.Set<PropertyAssignment>().Where(z => z.UserId == userId).Select(z => z.PropertyId);
            var unitIds = db.Set<Unit>().Where(e => propertyIds.Contains(e.PropertyId)).Select(e => e.Id);
            var leaseIds = db.Set<Lease>().Where(v => unitIds.Contains(v.UnitId)).Select(v => v.Id);
            return documents.Where(d => d.SharedWithCaretakers && (
                (d.PropertyId != null && propertyIds.Contains(d.PropertyId.Value))
                || (d.UnitId != null && unitIds.Contains(d.UnitId.Value))
                || (d.LeaseId != null && leaseIds.Contains(d.LeaseId.Value))));
        }

        return none;
    }
}

/// <summary>Allows tenants and caretakers to download the files of released documents (<c>/dateien/{id}</c>).</summary>
internal sealed class DocumentFileShare(MietHahnDbContext db, TimeProvider clock) : IFileShare
{
    public async Task<bool> IsSharedAsync(Guid fileId, ClaimsPrincipal user, CancellationToken cancellationToken = default) =>
        await (await DocumentShares.VisibleAsync(db, user, GermanTime.Today(clock.GetUtcNow()), cancellationToken)).AnyAsync(d => d.FileId == fileId, cancellationToken);
}

/// <summary>Dashboard: documents whose validity ends within 60 days (e.g. energy certificate, insurance).</summary>
internal sealed class DocumentExpiryTodos(MietHahnDbContext db, TimeProvider clock) : ITodoSource
{
    public async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsInRole(Roles.Admin) && !user.HasClaim(Permissions.ClaimType, nameof(AccessRight.MasterData)))
        {
            return [];
        }

        var today = GermanTime.Today(clock.GetUtcNow());
        var cutoff = today.AddDays(60);
        var documents = await db.Set<Document>().AsNoTracking().Where(d => d.ExpiresOn != null && d.ExpiresOn <= cutoff).OrderBy(d => d.ExpiresOn).ToListAsync(cancellationToken);
        return documents.Select(d => new TodoItem(
            "Dokumente",
            d.ExpiresOn < today ? $"Abgelaufen: {d.Title}" : $"Läuft ab: {d.Title}",
            $"/verwaltung/dokumente/{d.Id}",
            d.ExpiresOn < today ? Urgency.Urgent : Urgency.Soon,
            "Verlängern bzw. neues Dokument ablegen; das Ablaufdatum am alten Dokument dann entfernen.",
            d.ExpiresOn)).ToList();
    }
}

/// <summary>Documents of the person (directly or via their leases) for the GDPR access request.</summary>
internal sealed class DocumentPersonalData(IDocumentLibrary fileStore) : IPersonalDataSource
{
    public string Section => "documents";

    public async Task<object?> DataAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var documents = await fileStore.SearchAsync(new DocumentQuery(PersonId: personId), cancellationToken);
        return documents.Count == 0 ? null : documents.Select(d => new { d.Title, Category = d.Category.ToString(), d.Date, d.Reference, StoredFile = $"/dateien/{d.FileId}" }).ToList();
    }
}
