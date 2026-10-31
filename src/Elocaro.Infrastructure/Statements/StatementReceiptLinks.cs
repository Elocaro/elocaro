using Elocaro.Domain.OperatingCosts;
using Elocaro.Domain.People;
using Elocaro.Domain.Receipts;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Statements;

/// <summary>A cost item of the frozen statement and the receipt it comes from.</summary>
/// <param name="Amount">The part of the item that enters the billing period (rounded to cents).</param>
internal sealed record StatementReceiptItem(string CostType, string CostTypeName, Guid ReceiptId, decimal Amount);

/// <summary>Connects the cost items frozen in a statement snapshot with their receipts.</summary>
internal static class StatementReceiptLinks
{
    /// <summary>
    /// The items of the snapshot that come from a receipt (items derived from supply contracts have no receipt). The assignment of
    /// item to cost type is the frozen one, not the current one of the receipt.
    /// </summary>
    public static async Task<List<StatementReceiptItem>> ItemsAsync(ElocaroDbContext db, BillingResult result, CancellationToken cancellationToken)
    {
        var lines = result.CostTypes.SelectMany(t => t.Items.Select(i => (Type: t, Item: i))).ToList();
        var ids = lines.Select(l => l.Item.Id).Distinct().ToList();
        var receiptOf = await db.Set<CostItem>().AsNoTracking().IgnoreQueryFilters()
            .Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.ReceiptId, cancellationToken);
        return [.. lines
            .Where(l => receiptOf.ContainsKey(l.Item.Id))
            .Select(l => new StatementReceiptItem(l.Type.CostType, l.Type.Name, receiptOf[l.Item.Id], decimal.Round(l.Item.Prorated, 2, MidpointRounding.AwayFromZero)))];
    }

    /// <summary>Supplier name of each receipt: the contact if there is one, otherwise the entered name.</summary>
    public static async Task<Dictionary<Guid, string>> SuppliersAsync(ElocaroDbContext db, IEnumerable<Receipt> receipts, CancellationToken cancellationToken)
    {
        var list = receipts.ToList();
        var supplierIds = list.Select(r => r.SupplierId).OfType<Guid>().Distinct().ToList();
        var names = await db.Set<Person>().AsNoTracking().Where(p => supplierIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.DisplayName, cancellationToken);
        return list.ToDictionary(r => r.Id, r => (r.SupplierId is { } id ? names.GetValueOrDefault(id) : null) ?? r.SupplierName ?? "–");
    }
}
