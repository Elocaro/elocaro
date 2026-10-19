using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Accounts;
using MietHahn.Application.Common;
using MietHahn.Application.People;
using MietHahn.Application.Receipts;
using MietHahn.Application.Todos;
using MietHahn.Domain.Common;
using MietHahn.Domain.Files;
using MietHahn.Domain.People;
using MietHahn.Domain.Properties;
using MietHahn.Domain.Receipts;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Receipts;

internal sealed class ReceiptService(MietHahnDbContext db) : IReceiptService
{
    public async Task<IReadOnlyList<ReceiptRow>> SearchAsync(ReceiptQuery search, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        var query = db.Set<Receipt>().AsNoTracking().Include(b => b.Items).Include(b => b.Files).AsSplitQuery().AsQueryable();
        if (search.Year is { } year)
        {
            var from = new DateOnly(year, 1, 1);
            var to = new DateOnly(year, 12, 31);
            query = query.Where(b => (b.InvoiceDate >= from && b.InvoiceDate <= to)
                || (b.ServiceFrom != null && b.ServiceFrom <= to && (b.ServiceTo == null || b.ServiceTo >= from)));
        }

        if (search.PropertyId is { } property)
        {
            query = query.Where(b => b.Items.Any(p => p.PropertyId == property));
        }

        if (!string.IsNullOrWhiteSpace(search.CostType))
        {
            query = query.Where(b => b.Items.Any(p => p.CostType == search.CostType));
        }

        if (search.Unpaid)
        {
            query = query.Where(b => b.PaidOn == null);
        }

        if (!string.IsNullOrWhiteSpace(search.Text))
        {
            var pattern = "%" + search.Text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            var suppliers = db.Set<Person>().Where(p => EF.Functions.ILike(p.LastName, pattern) || (p.Company != null && EF.Functions.ILike(p.Company, pattern))).Select(p => p.Id);
            query = query.Where(b => (b.SupplierName != null && EF.Functions.ILike(b.SupplierName, pattern))
                || (b.InvoiceNumber != null && EF.Functions.ILike(b.InvoiceNumber, pattern))
                || (b.Note != null && EF.Functions.ILike(b.Note, pattern))
                || (b.SupplierId != null && suppliers.Contains(b.SupplierId.Value)));
        }

        var receipts = await query.OrderByDescending(b => b.InvoiceDate).ThenByDescending(b => b.Id).ToListAsync(cancellationToken);
        if (search.Unallocated)
        {
            receipts = receipts.Where(b => !b.IsFullyAllocated).ToList();
        }

        var names = await SuppliersAsync(receipts.Where(b => b.SupplierId is not null).Select(b => b.SupplierId!.Value), cancellationToken);
        var properties = await db.Set<Property>().AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, cancellationToken);
        return receipts.Select(b => new ReceiptRow(
            b.Id,
            Supplier(b, names),
            b.InvoiceNumber,
            b.InvoiceDate,
            b.Gross,
            b.Allocated,
            b.Items.Select(p => CostTypes.Find(p.CostType)?.Name ?? p.CostType).Distinct().ToList(),
            b.Items.Select(p => properties.GetValueOrDefault(p.PropertyId, "?")).Distinct().ToList(),
            b.PaidOn,
            b.Files.Count)).ToList();
    }

    public async Task<ReceiptDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var receipt = await db.Set<Receipt>().AsNoTracking().Include(b => b.Items).Include(b => b.Files).AsSplitQuery().SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (receipt is null)
        {
            return null;
        }

        var fileIds = receipt.Files.Select(d => d.FileId).ToList();
        var files = await db.Set<StoredFile>().AsNoTracking().Where(d => fileIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, cancellationToken);
        var properties = await db.Set<Property>().AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, cancellationToken);
        var units = await db.Set<Unit>().AsNoTracking().ToDictionaryAsync(e => e.Id, e => e.Name, cancellationToken);
        var names = await SuppliersAsync(receipt.SupplierId is { } l ? [l] : [], cancellationToken);
        return new ReceiptDetails(
            receipt.Id,
            Data(receipt),
            Supplier(receipt, names),
            receipt.Files.OrderBy(d => d.Page).Select(d => new ReceiptFileView(d.FileId, files[d.FileId].Name, files[d.FileId].MimeType, d.Page)).ToList(),
            receipt.Items.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).Select(p => new CostItemView(
                p.Id, p.CostType, CostTypes.Find(p.CostType)?.Name ?? p.CostType, p.Amount,
                properties.GetValueOrDefault(p.PropertyId, "?"), p.UnitId is { } e ? units.GetValueOrDefault(e) : null,
                p.BillingPeriod, p.Allocable, p.Section35a, p.LaborCosts, p.Note)).ToList(),
            receipt.Allocated,
            receipt.Gross - receipt.Allocated);
    }

    public Task<Result> CreateAsync(ReceiptInput data, IReadOnlyList<Guid> fileIds, CostItemInput? position = null, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(fileIds);
            var receipt = new Receipt(data.InvoiceDate ?? throw new RuleViolationException("Bitte das Rechnungsdatum angeben."), data.Gross ?? 0m);
            await ApplyAsync(receipt, data, cancellationToken);
            await ValidateFilesAsync(fileIds, cancellationToken);
            foreach (var fileId in fileIds)
            {
                receipt.AddFile(fileId);
            }

            if (position is not null && !string.IsNullOrWhiteSpace(position.CostType))
            {
                await CreateCostItemAsync(receipt, position, cancellationToken);
            }

            db.Add(receipt);
            await db.SaveChangesAsync(cancellationToken);
            return receipt.Id;
        });

    public Task<Result> UpdateAsync(Guid id, ReceiptInput data, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(data);
            var receipt = await ReceiptAsync(id, cancellationToken);
            await ApplyAsync(receipt, data, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public Task<Result> AddFilesAsync(Guid id, IReadOnlyList<Guid> fileIds, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(fileIds);
            var receipt = await ReceiptAsync(id, cancellationToken);
            await ValidateFilesAsync(fileIds, cancellationToken);
            foreach (var fileId in fileIds)
            {
                receipt.AddFile(fileId);
            }

            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public Task<Result> RemoveFileAsync(Guid id, Guid fileId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var receipt = await ReceiptAsync(id, cancellationToken);
            receipt.RemoveFile(fileId);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public Task<Result> AddItemAsync(Guid id, CostItemInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var receipt = await ReceiptAsync(id, cancellationToken);
            var position = await CreateCostItemAsync(receipt, input, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return position.Id;
        });

    public Task<Result> RemoveCostItemAsync(Guid id, Guid positionId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var receipt = await ReceiptAsync(id, cancellationToken);
            receipt.RemoveCostItem(positionId);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var receipt = await ReceiptAsync(id, cancellationToken);
            db.Remove(receipt);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public async Task<ReceiptOptions> OptionsAsync(CancellationToken cancellationToken = default)
    {
        var suppliers = await db.Set<Person>().AsNoTracking().Where(p => p.ContactType != ContactType.None && p.AnonymizedAt == null).ToListAsync(cancellationToken);
        var properties = await db.Set<Property>().AsNoTracking().OrderBy(o => o.Name).Select(o => new ReferenceOption(o.Id, o.Name)).ToListAsync(cancellationToken);
        var units = await db.Set<Unit>().AsNoTracking().Join(db.Set<Property>(), e => e.PropertyId, o => o.Id, (e, o) => new { e.Id, e.Name, Property = o.Name })
            .OrderBy(e => e.Property).ThenBy(e => e.Name).ToListAsync(cancellationToken);
        return new ReceiptOptions(
            suppliers.OrderBy(p => p.DisplayName, StringComparer.CurrentCulture).Select(p => new ReferenceOption(p.Id, p.DisplayName)).ToList(),
            properties,
            units.Select(e => new ReferenceOption(e.Id, $"{e.Name} ({e.Property})")).ToList());
    }

    private async Task<CostItem> CreateCostItemAsync(Receipt receipt, CostItemInput input, CancellationToken cancellationToken)
    {
        var propertyId = input.PropertyId ?? throw new RuleViolationException("Bitte das Objekt wählen.");
        if (!await db.Set<Property>().AnyAsync(o => o.Id == propertyId, cancellationToken))
        {
            throw new RuleViolationException("Das Objekt wurde nicht gefunden.");
        }

        if (input.UnitId is { } unit && !await db.Set<Unit>().AnyAsync(e => e.Id == unit && e.PropertyId == propertyId, cancellationToken))
        {
            throw new RuleViolationException("Die Einheit gehört nicht zum gewählten Objekt.");
        }

        // Default: service period, otherwise the calendar year of the invoice.
        var from = input.From ?? receipt.ServiceFrom ?? new DateOnly(receipt.InvoiceDate.Year, 1, 1);
        var to = input.To ?? (input.From is null ? receipt.ServiceTo : null) ?? new DateOnly(from.Year, 12, 31);
        if (to < from)
        {
            throw new RuleViolationException("Der Abrechnungszeitraum endet vor seinem Beginn.");
        }

        var position = receipt.AddCostItem(input.CostType, input.Amount ?? receipt.Gross - receipt.Allocated, propertyId, new Period(from, to), input.Allocable);
        position.UnitId = input.UnitId;
        position.Note = TrimToNull(input.Note);
        position.SetSection35a(input.Section35a, input.LaborCosts);
        return position;
    }

    private async Task ApplyAsync(Receipt receipt, ReceiptInput data, CancellationToken cancellationToken)
    {
        if (data.SupplierId is { } supplier && !await db.Set<Person>().AnyAsync(p => p.Id == supplier, cancellationToken))
        {
            throw new RuleViolationException("Der Lieferant wurde nicht gefunden.");
        }

        receipt.SupplierId = data.SupplierId;
        receipt.SupplierName = TrimToNull(data.SupplierName);
        receipt.InvoiceNumber = TrimToNull(data.InvoiceNumber);
        receipt.InvoiceDate = data.InvoiceDate ?? throw new RuleViolationException("Bitte das Rechnungsdatum angeben.");
        receipt.SetServicePeriod(data.ServiceFrom, data.ServiceTo);
        receipt.Net = data.Net;
        receipt.Vat = data.Vat;
        receipt.SetGross(data.Gross ?? 0m);
        receipt.DueOn = data.DueOn;
        receipt.PaidOn = data.PaidOn;
        receipt.FuelQuantity = data.FuelQuantity;
        receipt.FuelUnit = TrimToNull(data.FuelUnit);
        receipt.Co2Kg = data.Co2Kg;
        receipt.Note = TrimToNull(data.Note);
        receipt.Validate();
    }

    private async Task ValidateFilesAsync(IReadOnlyList<Guid> fileIds, CancellationToken cancellationToken)
    {
        var ids = fileIds.Distinct().ToList();
        if (await db.Set<StoredFile>().CountAsync(d => ids.Contains(d.Id), cancellationToken) != ids.Count)
        {
            throw new RuleViolationException("Eine Datei wurde nicht gefunden.");
        }
    }

    private async Task<Receipt> ReceiptAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Set<Receipt>().Include(b => b.Items).Include(b => b.Files).AsSplitQuery().SingleOrDefaultAsync(b => b.Id == id, cancellationToken)
            ?? throw new NotFoundException();

    private async Task<Dictionary<Guid, string>> SuppliersAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var list = ids.Distinct().ToList();
        return (await db.Set<Person>().AsNoTracking().Where(p => list.Contains(p.Id)).ToListAsync(cancellationToken)).ToDictionary(p => p.Id, p => p.DisplayName);
    }

    private static string Supplier(Receipt b, Dictionary<Guid, string> names) =>
        b.SupplierId is { } id && names.TryGetValue(id, out var name) ? name : b.SupplierName ?? "?";

    private static ReceiptInput Data(Receipt b) => new()
    {
        SupplierId = b.SupplierId,
        SupplierName = b.SupplierName,
        InvoiceNumber = b.InvoiceNumber,
        InvoiceDate = b.InvoiceDate,
        ServiceFrom = b.ServiceFrom,
        ServiceTo = b.ServiceTo,
        Gross = b.Gross,
        Net = b.Net,
        Vat = b.Vat,
        DueOn = b.DueOn,
        PaidOn = b.PaidOn,
        FuelQuantity = b.FuelQuantity,
        FuelUnit = b.FuelUnit,
        Co2Kg = b.Co2Kg,
        Note = b.Note,
    };

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Dashboard: due, unpaid receipts and receipts that have not been split.</summary>
internal sealed class ReceiptTodos(MietHahnDbContext db, TimeProvider clock) : ITodoSource
{
    public async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsInRole(Roles.Admin) && !user.HasClaim(Permissions.ClaimType, nameof(AccessRight.Finance)))
        {
            return [];
        }

        var today = GermanTime.Today(clock.GetUtcNow());
        var todos = new List<TodoItem>();
        var due = await db.Set<Receipt>().AsNoTracking().Where(b => b.PaidOn == null && b.DueOn != null && b.DueOn <= today.AddDays(7)).OrderBy(b => b.DueOn).ToListAsync(cancellationToken);
        todos.AddRange(due.Select(b => new TodoItem("Belege", $"Rechnung zahlen: {b.SupplierName ?? "Beleg"} {b.Gross:0.00} €", $"/verwaltung/belege/{b.Id}",
            b.DueOn < today ? Urgency.Urgent : Urgency.Soon, b.InvoiceNumber is null ? null : $"Rechnung {b.InvoiceNumber}", b.DueOn)));

        var receipts = await db.Set<Receipt>().AsNoTracking().Include(b => b.Items).ToListAsync(cancellationToken);
        var open = receipts.Count(b => !b.IsFullyAllocated);
        if (open > 0)
        {
            todos.Add(new TodoItem("Belege", $"{open} Belege ohne vollständige Kostenaufteilung", "/verwaltung/belege?aufteilen=true", Urgency.Info,
                "Ohne Kostenart und Objekt fehlen sie in Nebenkostenabrechnung und Anlage V."));
        }

        return todos;
    }
}

/// <summary>Receipts of a supplier (contact) for the GDPR access request.</summary>
internal sealed class ReceiptPersonalData(MietHahnDbContext db) : IPersonalDataSource
{
    public string Section => "receipts";

    public async Task<object?> DataAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var receipts = await db.Set<Receipt>().AsNoTracking().Where(b => b.SupplierId == personId).OrderBy(b => b.InvoiceDate)
            .Select(b => new { b.InvoiceNumber, b.InvoiceDate, b.Gross }).ToListAsync(cancellationToken);
        return receipts.Count == 0 ? null : receipts;
    }
}
