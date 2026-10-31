using System.Security.Claims;
using Elocaro.Application.Audit;
using Elocaro.Application.Files;
using Elocaro.Application.Statements;
using Elocaro.Domain.Common;
using Elocaro.Domain.Files;
using Elocaro.Domain.Leases;
using Elocaro.Domain.OperatingCosts;
using Elocaro.Domain.Receipts;
using Elocaro.Domain.Statements;
using Elocaro.Infrastructure.Leases;
using Elocaro.Infrastructure.Notices;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Statements;

/// <summary>
/// The statements sent to a tenant, as the portal shows them. Data separation: the view model is built from the tenant's own
/// <see cref="TenantStatement"/> only. <c>Result.Tenants</c>, the landlord positions, the flat-rate comparison, the problems and the
/// sections of the snapshot contain lease labels (names) and values of other parties and are never passed on.
/// </summary>
internal sealed class TenantStatementService(ElocaroDbContext db, IStatements statements, IFileStore files, IAccessLog accessLog) : ITenantStatements
{
    public async Task<IReadOnlyList<PortalStatementRow>> ListAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var dispatches = await OwnDispatchesAsync(user, null, cancellationToken);
        if (dispatches.Count == 0)
        {
            return [];
        }

        return await RowsAsync(dispatches, cancellationToken);
    }

    public async Task<PortalStatement?> GetAsync(ClaimsPrincipal user, Guid dispatchId, CancellationToken cancellationToken = default)
    {
        var dispatch = (await OwnDispatchesAsync(user, dispatchId, cancellationToken)).SingleOrDefault();
        if (dispatch is null)
        {
            return null;
        }

        var stored = await statements.GetAsync(dispatch.StatementId, cancellationToken);
        var tenant = stored?.Result.Tenants.FirstOrDefault(t => t.LeaseId == dispatch.LeaseId);
        if (stored is null || tenant is null)
        {
            return null;
        }

        var header = (await RowsAsync([dispatch], cancellationToken)).Single();
        var summary = stored.Summary;
        var lines = tenant.Lines
            .Select(l => new PortalStatementLine(l.CostType, l.Name, l.Group == CostGroup.Heating, l.Key, l.CostTotal, l.Share, StatementExplainer.Summary(l), StatementExplainer.Details(l)))
            .ToList();
        var balances = tenant.Groups.Where(g => g.Calculated)
            .Select(g => new PortalBalance(g.Group == CostGroup.Heating ? "Heizkosten" : "Betriebskosten", g.Share, g.PrepaymentsPaid, g.Balance))
            .ToList();

        return new PortalStatement(
            header,
            tenant.Term.Intersect(summary.BillingPeriod) ?? tenant.Term,
            tenant.Days,
            summary.Revision > 1 ? summary.Note : null,
            lines,
            balances,
            tenant.Balance,
            await PreviousYearAsync(summary, dispatch.LeaseId, tenant, cancellationToken),
            await ReceiptsAsync(stored, tenant, cancellationToken));
    }

    public async Task<OpenedFile?> OpenReceiptFileAsync(ClaimsPrincipal user, Guid dispatchId, Guid receiptId, Guid fileId, CancellationToken cancellationToken = default)
    {
        // The same function that decides what the page shows decides what can be fetched.
        var statement = await GetAsync(user, dispatchId, cancellationToken);
        var receipt = statement?.Receipts.SelectMany(g => g.Receipts).FirstOrDefault(r => r.ReceiptId == receiptId);
        if (statement is null || receipt is null || receipt.Files.All(f => f.FileId != fileId))
        {
            return null;
        }

        var opened = await files.OpenAsync(fileId, cancellationToken);
        if (opened is null)
        {
            return null;
        }

        await accessLog.LogAsync("Receipt", receiptId, $"Beleg im Mieterportal abgerufen (Abrechnung {Format(statement.Header.BillingPeriod)}, Datei {fileId})", cancellationToken);
        return opened;
    }

    /// <summary>
    /// Dispatches to leases of the signed-in tenant. Only dispatched revisions exist as far as the tenant is concerned: a newer revision
    /// that was not sent yet has no dispatch and stays invisible.
    /// </summary>
    private async Task<List<StatementDispatch>> OwnDispatchesAsync(ClaimsPrincipal user, Guid? dispatchId, CancellationToken cancellationToken)
    {
        var (viewer, persons) = await TenantScope.ViewerAsync(db, user, cancellationToken);
        if (viewer != NoticeViewer.Tenant)
        {
            return [];
        }

        var leaseIds = db.Set<Lease>().Where(v => v.Parties.Any(p => persons.Contains(p.PersonId) && p.Role == PartyRole.Tenant)).Select(v => v.Id);
        var query = db.Set<StatementDispatch>().AsNoTracking().Where(d => leaseIds.Contains(d.LeaseId));
        if (dispatchId is { } id)
        {
            query = query.Where(d => d.Id == id);
        }

        return await query.ToListAsync(cancellationToken);
    }

    private async Task<List<PortalStatementRow>> RowsAsync(List<StatementDispatch> dispatches, CancellationToken cancellationToken)
    {
        var leaseIds = dispatches.Select(d => d.LeaseId).Distinct().ToList();
        // All dispatches of these leases: a revision counts as superseded when a newer one was sent to the lease as well.
        var all = await db.Set<StatementDispatch>().AsNoTracking().Where(d => leaseIds.Contains(d.LeaseId)).Select(d => new { d.LeaseId, d.StatementId }).ToListAsync(cancellationToken);
        var statementIds = all.Select(d => d.StatementId).Distinct().ToList();
        var meta = await db.Set<BillingStatement>().AsNoTracking().Where(s => statementIds.Contains(s.Id))
            .Select(s => new { s.Id, s.PropertyId, s.BillingPeriod, s.Revision }).ToDictionaryAsync(s => s.Id, cancellationToken);
        var leases = await db.Set<Lease>().AsNoTracking().IgnoreQueryFilters().Where(v => leaseIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, cancellationToken);
        var units = await RentalUnit.GetAsync(db, cancellationToken);

        return [.. dispatches
            .Where(d => meta.ContainsKey(d.StatementId))
            .Select(d =>
            {
                var statement = meta[d.StatementId];
                var unit = units.GetValueOrDefault(leases[d.LeaseId].RentalUnitId);
                // Superseded only by a revision that was sent to this lease as well; an unsent newer one stays unknown to the tenant.
                var superseded = all.Any(o => o.LeaseId == d.LeaseId && meta.TryGetValue(o.StatementId, out var other)
                    && other.PropertyId == statement.PropertyId && other.BillingPeriod == statement.BillingPeriod && other.Revision > statement.Revision);
                return new PortalStatementRow(d.Id, d.LeaseId, d.FileId, unit?.Name ?? "", unit?.Property ?? "", statement.BillingPeriod, statement.Revision, d.SentOn, d.Balance, superseded);
            })
            .OrderByDescending(r => r.BillingPeriod.From).ThenBy(r => r.RentalUnit, StringComparer.CurrentCulture).ThenByDescending(r => r.Revision)];
    }

    /// <summary>Last statement of the previous year that was sent to the same lease; <c>null</c> if there is none.</summary>
    private async Task<YearComparison?> PreviousYearAsync(StatementSummary summary, Guid leaseId, TenantStatement tenant, CancellationToken cancellationToken)
    {
        var period = summary.BillingPeriod;
        if (period.To is not { } end)
        {
            return null;
        }

        var previousPeriod = new Period(period.From.AddYears(-1), end.AddYears(-1));
        var candidates = await db.Set<BillingStatement>().AsNoTracking()
            .Where(s => s.PropertyId == summary.PropertyId && s.BillingPeriod.From == previousPeriod.From && s.BillingPeriod.To == previousPeriod.To)
            .Select(s => new { s.Id, s.Revision }).ToListAsync(cancellationToken);
        var ids = candidates.Select(c => c.Id).ToList();
        var sent = (await db.Set<StatementDispatch>().AsNoTracking().Where(d => d.LeaseId == leaseId && ids.Contains(d.StatementId)).Select(d => d.StatementId).ToListAsync(cancellationToken)).ToHashSet();
        var last = candidates.Where(c => sent.Contains(c.Id)).OrderByDescending(c => c.Revision).FirstOrDefault();
        if (last is null)
        {
            return null;
        }

        var previous = (await statements.GetAsync(last.Id, cancellationToken))?.Result.Tenants.FirstOrDefault(t => t.LeaseId == leaseId);
        return previous is null ? null : StatementComparer.Compare(previousPeriod, tenant, previous);
    }

    /// <summary>The receipts the landlord released for this statement, by the cost types the tenant is charged for.</summary>
    private async Task<List<PortalReceiptGroup>> ReceiptsAsync(StoredStatement stored, TenantStatement tenant, CancellationToken cancellationToken)
    {
        var shared = await db.Set<StatementReceiptShare>().AsNoTracking().Where(s => s.StatementId == stored.Summary.Id).Select(s => s.ReceiptId).ToListAsync(cancellationToken);
        if (shared.Count == 0)
        {
            return [];
        }

        var ownTypes = tenant.Lines.Select(l => l.CostType).ToHashSet(StringComparer.Ordinal);
        var items = (await StatementReceiptLinks.ItemsAsync(db, stored.Result, cancellationToken))
            .Where(i => shared.Contains(i.ReceiptId) && ownTypes.Contains(i.CostType)).ToList();
        var receiptIds = items.Select(i => i.ReceiptId).Distinct().ToList();
        var receipts = await db.Set<Receipt>().AsNoTracking().Include(r => r.Files).Where(r => receiptIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);
        var suppliers = await StatementReceiptLinks.SuppliersAsync(db, receipts.Values, cancellationToken);
        var fileIds = receipts.Values.SelectMany(r => r.Files).Select(f => f.FileId).Distinct().ToList();
        var names = await db.Set<StoredFile>().AsNoTracking().Where(f => fileIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Name, cancellationToken);

        var order = tenant.Lines.Select((l, index) => (l.CostType, index)).GroupBy(x => x.CostType).ToDictionary(g => g.Key, g => g.Min(x => x.index));
        return [.. items
            .Where(i => receipts.ContainsKey(i.ReceiptId))
            .GroupBy(i => (i.CostType, i.CostTypeName))
            .Select(g => new PortalReceiptGroup(
                g.Key.CostType,
                g.Key.CostTypeName,
                [.. g.GroupBy(i => i.ReceiptId).Select(r =>
                {
                    var receipt = receipts[r.Key];
                    return new PortalReceipt(
                        receipt.Id,
                        suppliers[receipt.Id],
                        receipt.InvoiceNumber,
                        receipt.InvoiceDate,
                        r.Sum(i => i.Amount),
                        [.. receipt.Files.OrderBy(f => f.Page).Where(f => names.ContainsKey(f.FileId)).Select(f => new PortalReceiptFile(f.FileId, f.Page, names[f.FileId]))]);
                }).OrderBy(r => r.InvoiceDate).ThenBy(r => r.Supplier, StringComparer.CurrentCulture)]))
            .OrderBy(g => order[g.CostType])];
    }

    private static string Format(Period period) =>
        period.To is { } to ? $"{period.From:dd.MM.yyyy}–{to:dd.MM.yyyy}" : $"ab {period.From:dd.MM.yyyy}";
}
