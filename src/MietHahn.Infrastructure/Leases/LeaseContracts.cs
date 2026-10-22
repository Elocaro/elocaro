using System.Net;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Common;
using MietHahn.Application.Documents;
using MietHahn.Application.Files;
using MietHahn.Application.Leases;
using MietHahn.Domain.Admin;
using MietHahn.Domain.Common;
using MietHahn.Domain.Documents;
using MietHahn.Domain.Leases;
using MietHahn.Domain.People;
using MietHahn.Domain.Properties;
using MietHahn.Domain.Receipts;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Documents;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Infrastructure.Templates;

namespace MietHahn.Infrastructure.Leases;

internal sealed class LeaseContracts(MietHahnDbContext db, IPdfGenerator pdf, IDocumentLibrary documents, IFileStore fileStore, TimeProvider clock) : ILeaseContracts
{
    public async Task<IReadOnlyList<ContractRow>> ListAsync(Guid leaseId, CancellationToken cancellationToken = default)
    {
        var contracts = await db.Set<LeaseContract>().AsNoTracking().Where(c => c.LeaseId == leaseId).OrderByDescending(c => c.CreatedAt).ToListAsync(cancellationToken);
        var revisionIds = contracts.Select(c => c.TemplateRevisionId).ToList();
        var revisions = await db.Set<DocumentTemplateRevision>().AsNoTracking().Where(r => revisionIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);
        var templateIds = revisions.Values.Select(r => r.TemplateId).Distinct().ToList();
        var names = await db.Set<DocumentTemplate>().IgnoreQueryFilters().AsNoTracking().Where(t => templateIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
        return contracts.Select(c =>
        {
            var revision = revisions[c.TemplateRevisionId];
            return new ContractRow(c.Id, c.Status, c.CreatedOn, names.GetValueOrDefault(revision.TemplateId, "?"), revision.Number, c.DraftFileId, c.SignedOn, c.SignedDocumentId);
        }).ToList();
    }

    public async Task<TemplatePreview> PreviewAsync(Guid templateId, Guid leaseId, CancellationToken cancellationToken = default)
    {
        try
        {
            var template = await ContractTemplateAsync(templateId, cancellationToken);
            return new TemplatePreview(await pdf.GenerateAsync(await RequestAsync(template, leaseId, cancellationToken), cancellationToken), []);
        }
        catch (Exception ex) when (ex is RuleViolationException or InvalidOperationException)
        {
            return new TemplatePreview(null, [ex.Message]);
        }
        catch (HttpRequestException)
        {
            return new TemplatePreview(null, ["Der PDF-Dienst ist nicht erreichbar."]);
        }
    }

    public Task<Result> CreateDraftAsync(Guid leaseId, Guid templateId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var template = await ContractTemplateAsync(templateId, cancellationToken);
            var request = await RequestAsync(template, leaseId, cancellationToken);
            var revisionId = (await db.Set<DocumentTemplateRevision>().AsNoTracking().SingleAsync(r => r.TemplateId == template.Id && r.Number == template.Revision, cancellationToken)).Id;

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            foreach (var open in await db.Set<LeaseContract>().Where(c => c.LeaseId == leaseId && c.Status == ContractStatus.Draft).ToListAsync(cancellationToken))
            {
                open.Withdraw();
            }

            await db.SaveChangesAsync(cancellationToken);
            Domain.Files.StoredFile file;
            try
            {
                file = await pdf.GenerateAndStoreAsync(request, FileName(request.Title, ((ContractModel)request.Model!).TenantNames), cancellationToken);
            }
            catch (HttpRequestException)
            {
                throw new RuleViolationException("Der PDF-Dienst ist nicht erreichbar; es wurde nichts verändert.");
            }

            var contract = new LeaseContract(leaseId, revisionId, file.Id, request.Date!.Value);
            db.Add(contract);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return contract.Id;
        });

    public Task<Result> ConcludeAsync(Guid contractId, SignedContractInput input, string fileName, Stream content, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(content);
            var signedOn = input.SignedOn ?? throw new RuleViolationException("Bitte das Datum der Unterschrift angeben.");
            var contract = await db.Set<LeaseContract>().SingleOrDefaultAsync(c => c.Id == contractId, cancellationToken) ?? throw new NotFoundException();
            if (contract.Status != ContractStatus.Draft)
            {
                contract.Sign(signedOn, Guid.Empty); // throws the matching message
            }

            var tenants = await TenantNamesAsync(contract.LeaseId, cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var file = await fileStore.SaveAsync(content, Path.GetFileName(fileName), cancellationToken);
            var title = $"Mietvertrag {tenants} (unterschrieben)";
            var document = await documents.CreateAsync(file.Id, new DocumentInput
            {
                Title = title.Length > 200 ? title[..200] : title,
                Category = DocumentCategory.Lease,
                Date = signedOn,
                LeaseId = contract.LeaseId,
                TemplateRevisionId = contract.TemplateRevisionId,
                SharedWithTenants = input.ShareWithTenants,
                Note = "Unterschriebene Fassung des Mietvertrags.",
            }, cancellationToken);
            if (!document.Succeeded)
            {
                throw new RuleViolationException(string.Join(' ', document.Errors));
            }

            contract.Sign(signedOn, document.Id!.Value);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return contract.Id;
        });

    public Task<Result> WithdrawAsync(Guid contractId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var contract = await db.Set<LeaseContract>().SingleOrDefaultAsync(c => c.Id == contractId, cancellationToken) ?? throw new NotFoundException();
            contract.Withdraw();
            await db.SaveChangesAsync(cancellationToken);
            return contract.Id;
        });

    private async Task<DocumentTemplate> ContractTemplateAsync(Guid templateId, CancellationToken cancellationToken)
    {
        var template = await db.Set<DocumentTemplate>().AsNoTracking().SingleOrDefaultAsync(t => t.Id == templateId, cancellationToken)
            ?? throw new RuleViolationException("Die Vorlage wurde nicht gefunden.");
        return template.Kind == TemplateKind.Contract ? template : throw new RuleViolationException("Bitte eine Vorlage der Art „Mietvertrag“ wählen.");
    }

    private async Task<PdfRequest> RequestAsync(DocumentTemplate template, Guid leaseId, CancellationToken cancellationToken)
    {
        var today = GermanTime.Today(clock.GetUtcNow());
        var model = await ModelAsync(leaseId, today, cancellationToken);
        string title;
        try
        {
            title = WebUtility.HtmlDecode(ScribanRenderer.RenderText(template.Subject, model, snippets: await TemplateSnippets.LoadAsync(db, cancellationToken)).Html).Trim();
        }
        catch (TemplateErrorException ex)
        {
            throw new RuleViolationException(ex.Message);
        }

        return new PdfRequest(null, title, model, null, template.Body, today, await Admin.LandlordProfiles.PropertyOfLeaseAsync(db, leaseId, cancellationToken));
    }

    private async Task<string> TenantNamesAsync(Guid leaseId, CancellationToken cancellationToken)
    {
        var lease = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).SingleAsync(v => v.Id == leaseId, cancellationToken);
        var ids = lease.Tenant.Select(p => p.PersonId).ToList();
        var people = await db.Set<Person>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
        return string.Join(" und ", ids.Select(i => people.First(p => p.Id == i).DisplayName));
    }

    private async Task<ContractModel> ModelAsync(Guid leaseId, DateOnly today, CancellationToken cancellationToken)
    {
        var lease = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).Include(v => v.Components).Include(v => v.RentSteps).Include(v => v.People)
            .SingleOrDefaultAsync(v => v.Id == leaseId, cancellationToken) ?? throw new RuleViolationException("Der Mietvertrag wurde nicht gefunden.");
        var landlord = await Admin.LandlordProfiles.ForUnitAsync(db, lease.UnitId, cancellationToken)
            ?? throw new RuleViolationException("Es ist noch kein Vermieterprofil hinterlegt (Einstellungen → Vermieter).");
        var rentalUnit = (await RentalUnit.GetAsync(db, cancellationToken)).GetValueOrDefault(lease.RentalUnitId)
            ?? throw new RuleViolationException("Die Mietsache wurde nicht gefunden.");
        var property = await db.Set<Property>().AsNoTracking().SingleAsync(o => o.Id == rentalUnit.PropertyId, cancellationToken);
        var unit = await db.Set<Unit>().AsNoTracking().Include(e => e.Areas).Include(e => e.Room).ThenInclude(z => z.Areas).AsSplitQuery().SingleAsync(e => e.Id == lease.UnitId, cancellationToken);
        var room = lease.RoomId is { } roomId ? unit.Room.SingleOrDefault(z => z.Id == roomId) : null;

        var personIds = lease.Parties.Select(p => p.PersonId).ToList();
        var people = await db.Set<Person>().AsNoTracking().Include(p => p.Addresses).Where(p => personIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        ContractParty Party(LeaseParty p)
        {
            var person = people[p.PersonId];
            return new ContractParty(person.DisplayName, person.DateOfBirth, person.AddressAt(today)?.SingleLine);
        }

        var tenants = lease.Tenant.Select(Party).ToList();
        var guarantors = lease.Parties.Where(p => p.Role == PartyRole.Guarantor).OrderBy(p => p.Order).Select(Party).ToList();

        var start = lease.Start;
        var area = unit.AreaOn(start) ?? unit.AreaOn(today);
        var roomArea = room?.AreaOn(start) ?? room?.AreaOn(today);
        decimal Amount(RentComponentType kind) => lease.AmountAt(kind, start) ?? 0m;
        var others = lease.ComponentsAt(start).Where(b => b.Kind == RentComponentType.Miscellaneous).Select(b => new ContractComponent(b.Name ?? "Sonstiges", b.Amount)).ToList();
        var baseRent = Amount(RentComponentType.BaseRent);
        var allocable = CostTypes.All.Where(t => t.Allocable).ToList();

        return new ContractModel(
            today, landlord.Name, $"{landlord.Street}, {landlord.PostalCode} {landlord.City}", tenants, guarantors, string.Join(" und ", tenants.Select(t => t.Name)),
            property.Name, property.Street, property.PostalCode, property.City, property.YearBuilt, HeatingText(property.HeatingType),
            unit.Name, unit.Location, unit.RoomCount, area?.LivingArea, unit.Bathroom, unit.Balcony, unit.FittedKitchen, unit.Amenities, unit.EnergyStandard,
            room is not null, room?.Name, roomArea?.Area, roomArea?.CommonAreaShare, room is null ? null : FurnishingText(room.Furnishing),
            room?.FurnishingList?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [],
            unit.Room.Where(z => z.Id != lease.RoomId).OrderBy(z => z.Order).Select(z => z.Name).ToList(),
            start, lease.FixedTermUntil, lease.FixedTermUntil is not null, lease.NoticePeriodMonths,
            lease.BillingType == BillingType.Prepayment, lease.BillingType == BillingType.FlatRate, lease.BillingType == BillingType.Inclusive, lease.HeatingCostsConsumptionBased,
            lease.RentModel == RentModel.Graduated, lease.RentModel == RentModel.Index,
            lease.RentSteps.OrderBy(s => s.From).Select(s => new ContractStep(s.From, s.BaseRent)).ToList(), lease.IndexBaseMonth, lease.IndexBaseValue,
            baseRent, Amount(RentComponentType.OperatingCostPrepayment), Amount(RentComponentType.HeatingCostPrepayment), Amount(RentComponentType.OperatingCostFlatRate),
            Amount(RentComponentType.ParkingSpace), Amount(RentComponentType.FurnishingSurcharge), others, lease.MonthlyRentOn(start),
            lease.DueOnWorkingDay, lease.PaymentMethod == PaymentMethod.DirectDebit, lease.Deposit, lease.DepositNote, baseRent * 3,
            allocable.Where(t => lease.IsAgreed(t.Key)).Select(t => new ContractCostType(t.BetrKvNumber, t.Name)).ToList(),
            allocable.Where(t => !lease.IsAgreed(t.Key)).Select(t => new ContractCostType(t.BetrKvNumber, t.Name)).ToList(),
            lease.OccupantCountOn(start), lease.Notes,
            landlord.Iban is null ? null : Iban.Format(landlord.Iban), landlord.Bank);
    }

    private static string FileName(string title, string tenants)
    {
        var cleaned = string.Concat($"{title} {tenants}".Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim();
        return (cleaned.Length > 200 ? cleaned[..200] : cleaned) + ".pdf";
    }

    private static string FurnishingText(Furnishing value) => value switch
    {
        Furnishing.PartlyFurnished => "teilmöbliert",
        Furnishing.Furnished => "möbliert",
        _ => "unmöbliert",
    };

    private static string HeatingText(HeatingType value) => value switch
    {
        HeatingType.CentralHeating => "Zentralheizung",
        HeatingType.FloorHeating => "Fußbodenheizung",
        HeatingType.DistrictHeating => "Fernwärme",
        HeatingType.HeatPump => "Wärmepumpe",
        HeatingType.IndividualStoves => "Einzelöfen",
        HeatingType.Other => "sonstige Heizung",
        _ => "",
    };
}
