using System.Net;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Common;
using MietHahn.Application.Communication;
using MietHahn.Application.Documents;
using MietHahn.Application.Finance;
using MietHahn.Application.Leases;
using MietHahn.Domain.Admin;
using MietHahn.Domain.Common;
using MietHahn.Domain.Documents;
using MietHahn.Domain.Leases;
using MietHahn.Domain.People;
using MietHahn.Domain.Properties;
using MietHahn.Infrastructure.Documents;
using MietHahn.Infrastructure.Leases;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Infrastructure.Properties;
using MietHahn.Infrastructure.Templates;

namespace MietHahn.Infrastructure.Correspondence;

internal sealed class Letters(MietHahnDbContext db, IPdfGenerator pdf, IDocumentLibrary documents, IMailSender mail, TimeProvider clock, ITenantAccounts tenantAccounts, IDeposits deposits) : ILetters
{
    public async Task<IReadOnlyList<LetterRecipient>> RecipientsAsync(Guid propertyId, CancellationToken cancellationToken = default)
    {
        var today = GermanTime.Today(clock.GetUtcNow());
        var rentalUnits = await RentalUnit.GetAsync(db, cancellationToken);
        var unitIds = rentalUnits.Values.Where(e => e.PropertyId == propertyId).Select(e => e.Id).ToList();
        var leases = await CurrentLeasesAsync(unitIds, today, cancellationToken);
        var people = await PeopleAsync(leases, cancellationToken);
        return leases
            .Select(v => (Lease: v, Tenants: TenantsOf(v, people)))
            .OrderBy(x => rentalUnits[x.Lease.RentalUnitId].Name, StringComparer.CurrentCulture)
            .Select(x => new LetterRecipient(x.Lease.Id, string.Join(", ", x.Tenants.Select(p => p.DisplayName)), rentalUnits[x.Lease.RentalUnitId].Name, x.Tenants.Any(p => !string.IsNullOrWhiteSpace(p.Email))))
            .ToList();
    }

    public async Task<TemplatePreview> PreviewAsync(Guid templateId, Guid leaseId, CancellationToken cancellationToken = default)
    {
        try
        {
            var template = await LetterTemplateAsync(templateId, cancellationToken);
            var letter = await PrepareAsync(template, leaseId, cancellationToken);
            return new TemplatePreview(await pdf.GenerateAsync(letter.Request, cancellationToken), []);
        }
        catch (RuleViolationException ex)
        {
            return new TemplatePreview(null, [ex.Message]);
        }
        catch (InvalidOperationException ex)
        {
            return new TemplatePreview(null, [ex.Message]);
        }
        catch (HttpRequestException)
        {
            return new TemplatePreview(null, ["Der PDF-Dienst ist nicht erreichbar."]);
        }
    }

    public async Task<LetterRunResult> CreateAsync(LetterRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        DocumentTemplate template;
        try
        {
            template = await LetterTemplateAsync(request.TemplateId, cancellationToken);
        }
        catch (RuleViolationException ex)
        {
            return new LetterRunResult([], [ex.Message]);
        }

        var allowed = (await RecipientsAsync(request.PropertyId, cancellationToken)).Select(r => r.LeaseId).ToHashSet();
        var revision = await db.Set<DocumentTemplateRevision>().AsNoTracking()
            .SingleAsync(r => r.TemplateId == template.Id && r.Number == template.Revision, cancellationToken);
        var outcomes = new List<LetterOutcome>();
        foreach (var leaseId in request.LeaseIds.Distinct())
        {
            if (!allowed.Contains(leaseId))
            {
                outcomes.Add(new LetterOutcome(leaseId, "?", false, "Der Mietvertrag gehört nicht zu diesem Objekt oder ist beendet.", null, false));
                continue;
            }

            outcomes.Add(await CreateOneAsync(template, revision.Id, leaseId, request, cancellationToken));
        }

        return new LetterRunResult(outcomes, outcomes.Count == 0 ? ["Es wurde kein Mieter gewählt."] : []);
    }

    private async Task<LetterOutcome> CreateOneAsync(DocumentTemplate template, Guid revisionId, Guid leaseId, LetterRequest request, CancellationToken cancellationToken)
    {
        var recipient = "?";
        try
        {
            var letter = await PrepareAsync(template, leaseId, cancellationToken);
            recipient = letter.Recipient;
            string? note = null;
            if (request.SendByMail && letter.Emails.Count == 0)
            {
                note = "Keine E-Mail-Adresse hinterlegt: das PDF liegt bei den Dokumenten und kann per Post gesendet werden.";
                // The PDF is still created (for the post), but there is nobody to send it to.
                request = new LetterRequest { TemplateId = request.TemplateId, PropertyId = request.PropertyId, ShareWithTenants = request.ShareWithTenants, SendByMail = false };
            }

            var today = letter.Request.Date!.Value;
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var file = await pdf.GenerateAndStoreAsync(letter.Request, FileName(letter.Request.Title, letter.Recipient), cancellationToken);
            var title = $"{letter.Request.Title} – {letter.Recipient}";
            var document = await documents.CreateAsync(file.Id, new DocumentInput
            {
                Title = title.Length > 200 ? title[..200] : title,
                Category = DocumentCategory.Correspondence,
                Date = today,
                LeaseId = leaseId,
                SharedWithTenants = request.ShareWithTenants,
                TenantsInformedElsewhere = request.SendByMail,
                TemplateRevisionId = revisionId,
                Note = $"Aus der Vorlage „{template.Name}“ (Fassung {template.Revision}) erzeugt.",
            }, cancellationToken);
            if (!document.Succeeded)
            {
                throw new RuleViolationException(string.Join(' ', document.Errors));
            }

            if (request.SendByMail)
            {
                await mail.EnqueueAsync(new MailRequest(
                    letter.Emails,
                    "letter",
                    new { Title = letter.Request.Title, letter.Salutation, SenderName = letter.SenderName },
                    [file.Id],
                    $"letter:{document.Id}"), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new LetterOutcome(leaseId, recipient, true, note, document.Id, request.SendByMail);
        }
        catch (Exception ex) when (ex is RuleViolationException or InvalidOperationException or HttpRequestException)
        {
            db.ChangeTracker.Clear();
            return new LetterOutcome(leaseId, recipient, false, ex is HttpRequestException ? "Der PDF-Dienst ist nicht erreichbar." : ex.Message, null, false);
        }
    }

    private async Task<DocumentTemplate> LetterTemplateAsync(Guid templateId, CancellationToken cancellationToken)
    {
        var template = await db.Set<DocumentTemplate>().AsNoTracking().SingleOrDefaultAsync(t => t.Id == templateId, cancellationToken)
            ?? throw new RuleViolationException("Die Vorlage wurde nicht gefunden.");
        return template.Kind == TemplateKind.Letter ? template : throw new RuleViolationException("Ein Baustein lässt sich nicht als Schreiben versenden.");
    }

    private async Task<Prepared> PrepareAsync(DocumentTemplate template, Guid leaseId, CancellationToken cancellationToken)
    {
        var today = GermanTime.Today(clock.GetUtcNow());
        var lease = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).Include(v => v.Components).SingleOrDefaultAsync(v => v.Id == leaseId, cancellationToken)
            ?? throw new RuleViolationException("Der Mietvertrag wurde nicht gefunden.");
        var landlord = await SenderProfiles.ForUnitAsync(db, lease.UnitId, cancellationToken)
            ?? throw new RuleViolationException("Es ist noch kein Vermieterprofil hinterlegt (Einstellungen → Vermieter). Es wird für Briefkopf und Absender benötigt.");
        var people = TenantsOf(lease, await PeopleAsync([lease], cancellationToken));
        var rentalUnit = (await RentalUnit.GetAsync(db, cancellationToken)).GetValueOrDefault(lease.RentalUnitId)
            ?? throw new RuleViolationException("Die Mietsache wurde nicht gefunden.");
        var property = await db.Set<Property>().AsNoTracking().SingleAsync(o => o.Id == rentalUnit.PropertyId, cancellationToken);

        var baseRent = lease.AmountAt(RentComponentType.BaseRent, today) ?? 0;
        var operating = (lease.AmountAt(RentComponentType.OperatingCostPrepayment, today) ?? 0) + (lease.AmountAt(RentComponentType.OperatingCostFlatRate, today) ?? 0);
        var heating = lease.AmountAt(RentComponentType.HeatingCostPrepayment, today) ?? 0;
        var account = await tenantAccounts.GetAsync(leaseId, cancellationToken);
        var deposit = await deposits.GetAsync(leaseId, cancellationToken);
        decimal? area = lease.RoomId is { } roomId
            ? (await db.Set<Room>().AsNoTracking().Include(r => r.Areas).SingleOrDefaultAsync(r => r.Id == roomId, cancellationToken))?.AreaOn(today)?.Area
            : (await db.Set<Unit>().AsNoTracking().Include(e => e.Areas).SingleOrDefaultAsync(e => e.Id == lease.UnitId, cancellationToken))?.AreaOn(today)?.LivingArea;
        var salutation = LetterParts.SalutationOf(people);
        var model = new LetterModel(
            salutation, string.Join(" und ", people.Select(p => p.DisplayName)), $"{rentalUnit.Name}, {property.Name}",
            property.Name, property.Street, property.PostalCode, property.City, lease.Start, lease.End, landlord.Name, today,
            landlord.Bank, landlord.Iban is null ? null : Iban.Format(landlord.Iban), landlord.Bic,
            baseRent, operating, heating, lease.MonthlyRentOn(today), lease.Deposit,
            deposit?.Account.Received ?? 0, deposit?.Account.Interest ?? 0, deposit?.Account.Deducted ?? 0, deposit?.Account.Refunded ?? 0, deposit?.Account.Balance ?? 0,
            account?.AccountingStart is null ? null : account.Due, area);

        string subject;
        try
        {
            var snippets = await TemplateSnippets.LoadAsync(db, cancellationToken);
            subject = WebUtility.HtmlDecode(ScribanRenderer.RenderText(template.Subject, model, snippets: snippets).Html).Trim();
        }
        catch (TemplateErrorException ex)
        {
            throw new RuleViolationException(ex.Message);
        }

        var request = new PdfRequest(null, subject, model, new PostalAddress(LetterParts.AddressRowsOf(people, rentalUnit.Name, property, today)), template.Body, today, property.Id);
        var emails = people.Select(p => p.Email).Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new Prepared(request, string.Join(", ", people.Select(p => p.DisplayName)), emails, salutation, landlord.Name);
    }

    private async Task<List<Lease>> CurrentLeasesAsync(List<Guid> rentalUnitIds, DateOnly today, CancellationToken cancellationToken) =>
        await db.Set<Lease>().AsNoTracking().Include(v => v.Parties)
            .Where(v => rentalUnitIds.Contains(v.RentalUnitId) && v.Term.From <= today && (v.Term.To == null || v.Term.To >= today))
            .ToListAsync(cancellationToken);

    private async Task<Dictionary<Guid, Person>> PeopleAsync(IEnumerable<Lease> leases, CancellationToken cancellationToken)
    {
        var ids = leases.SelectMany(v => v.Parties.Select(p => p.PersonId)).Distinct().ToList();
        return await db.Set<Person>().AsNoTracking().Include(p => p.Addresses).Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
    }

    private static List<Person> TenantsOf(Lease lease, Dictionary<Guid, Person> people) =>
        lease.Tenant.Select(p => people.GetValueOrDefault(p.PersonId)).OfType<Person>().ToList();

    private static string FileName(string title, string recipient)
    {
        var name = $"{title} {recipient}";
        var cleaned = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim();
        return (cleaned.Length > 200 ? cleaned[..200] : cleaned) + ".pdf";
    }

    private sealed record Prepared(PdfRequest Request, string Recipient, IReadOnlyList<string> Emails, string Salutation, string SenderName);
}
