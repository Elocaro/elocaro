using Elocaro.Application.Audit;
using Elocaro.Domain.Common;
using Elocaro.Domain.Finance;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Finance;

/// <summary>
/// A manually created claim is taken back by a counter claim with the same due date and the opposite amount (the open balance cancels out at once);
/// the original stays untouched. Automatic monthly rents and reductions come from the lease and cannot be compensated.
/// </summary>
internal sealed class ChargeCompensation(ElocaroDbContext db) : IUndoCompensation
{
    public string EntityType => nameof(Charge);

    public async Task<string?> CheckAsync(Guid entityId, CancellationToken cancellationToken = default)
    {
        var charge = await db.Set<Charge>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(c => c.Id == entityId, cancellationToken);
        return charge switch
        {
            null => "Die Sollstellung gibt es nicht mehr.",
            { Deleted: true } => "Die Sollstellung ist schon gelöscht.",
            { Generated: true } => "Automatisch erzeugte Monatsmieten und Minderungen ergeben sich aus dem Vertrag und lassen sich nicht ausgleichen.",
            _ => null,
        };
    }

    public async Task<string?> CompensateAsync(Guid entityId, CancellationToken cancellationToken = default)
    {
        if (await CheckAsync(entityId, cancellationToken) is { } reason)
        {
            return reason;
        }

        var charge = await db.Set<Charge>().AsNoTracking().SingleAsync(c => c.Id == entityId, cancellationToken);
        db.Add(new Charge(charge.LeaseId, ChargeType.Miscellaneous, charge.Due, -charge.Amount, Shorten("Storno: " + charge.Name, 200))
        {
            Note = "Gegenbuchung zu einer rückgängig gemachten Sollstellung.",
        });
        return null;
    }

    internal static string Shorten(string text, int max) => text.Length <= max ? text : text[..max];
}

/// <summary>
/// A manually recorded payment is taken back by a booking of the opposite amount dated today (the payment date counts for the cash-basis principle, so a
/// reversal in a past period is not rewritten). Payments from the bank import are changed through the assignment of the transaction.
/// </summary>
internal sealed class PaymentCompensation(ElocaroDbContext db, TimeProvider clock) : IUndoCompensation
{
    public string EntityType => nameof(Payment);

    public async Task<string?> CheckAsync(Guid entityId, CancellationToken cancellationToken = default)
    {
        var payment = await db.Set<Payment>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(p => p.Id == entityId, cancellationToken);
        return payment switch
        {
            null => "Die Zahlung gibt es nicht mehr.",
            { Deleted: true } => "Die Zahlung ist schon gelöscht.",
            { Origin: not PaymentOrigin.Manual } => "Zahlungen aus dem Bankimport werden über die Zuordnung des Umsatzes geändert.",
            _ => null,
        };
    }

    public async Task<string?> CompensateAsync(Guid entityId, CancellationToken cancellationToken = default)
    {
        if (await CheckAsync(entityId, cancellationToken) is { } reason)
        {
            return reason;
        }

        var payment = await db.Set<Payment>().AsNoTracking().SingleAsync(p => p.Id == entityId, cancellationToken);
        db.Add(new Payment(payment.LeaseId, GermanTime.Today(clock.GetUtcNow()), -payment.Amount)
        {
            Purpose = ChargeCompensation.Shorten("Rückbuchung" + (payment.Purpose is null ? "" : ": " + payment.Purpose), 500),
        });
        return null;
    }
}
