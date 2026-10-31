using Elocaro.Application.Common;
using Elocaro.Application.Statements;
using Elocaro.Domain.Common;
using Elocaro.Domain.Receipts;
using Elocaro.Domain.Statements;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Statements;

/// <summary>Release of receipts for the tenants of a released statement (nothing is released by default).</summary>
internal sealed class StatementReceiptShareService(ElocaroDbContext db, IStatements statements) : IStatementReceiptShares
{
    public async Task<ReceiptShareOverview?> OverviewAsync(Guid statementId, CancellationToken cancellationToken = default)
    {
        var statement = await statements.GetAsync(statementId, cancellationToken);
        if (statement is null)
        {
            return null;
        }

        var items = await StatementReceiptLinks.ItemsAsync(db, statement.Result, cancellationToken);
        var receiptIds = items.Select(i => i.ReceiptId).Distinct().ToList();
        var receipts = await db.Set<Receipt>().AsNoTracking().Include(r => r.Files).Where(r => receiptIds.Contains(r.Id)).ToListAsync(cancellationToken);
        var suppliers = await StatementReceiptLinks.SuppliersAsync(db, receipts, cancellationToken);
        var shares = await db.Set<StatementReceiptShare>().AsNoTracking().Where(s => s.StatementId == statementId).ToDictionaryAsync(s => s.ReceiptId, cancellationToken);

        var rows = receipts
            .OrderBy(r => suppliers[r.Id], StringComparer.CurrentCulture).ThenBy(r => r.InvoiceDate)
            .Select(r =>
            {
                var own = items.Where(i => i.ReceiptId == r.Id).ToList();
                shares.TryGetValue(r.Id, out var share);
                return new ReceiptShareRow(
                    r.Id,
                    suppliers[r.Id],
                    r.InvoiceNumber,
                    r.InvoiceDate,
                    own.Sum(i => i.Amount),
                    [.. own.Select(i => i.CostTypeName).Distinct()],
                    r.Files.Count,
                    share is not null,
                    share?.CreatedAt,
                    share?.CreatedBy);
            })
            .ToList();
        return new ReceiptShareOverview(statementId, rows);
    }

    public Task<Result> SetSharedAsync(Guid statementId, IReadOnlyCollection<Guid> receiptIds, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(receiptIds);
            var overview = await OverviewAsync(statementId, cancellationToken) ?? throw new NotFoundException();
            var wanted = receiptIds.ToHashSet();
            foreach (var id in wanted)
            {
                var row = overview.Receipts.FirstOrDefault(r => r.ReceiptId == id)
                    ?? throw new RuleViolationException("Ein ausgewählter Beleg gehört nicht zu dieser Abrechnung.");
                if (row.FileCount == 0)
                {
                    throw new RuleViolationException($"Der Beleg von {row.Supplier} hat keine Datei und kann nicht freigegeben werden.");
                }
            }

            var existing = await db.Set<StatementReceiptShare>().Where(s => s.StatementId == statementId).ToListAsync(cancellationToken);
            foreach (var share in existing.Where(s => !wanted.Contains(s.ReceiptId)))
            {
                db.Remove(share);
            }

            foreach (var id in wanted.Where(id => existing.All(s => s.ReceiptId != id)))
            {
                db.Add(new StatementReceiptShare(statementId, id));
            }

            await db.SaveChangesAsync(cancellationToken);
            return statementId;
        });
}
