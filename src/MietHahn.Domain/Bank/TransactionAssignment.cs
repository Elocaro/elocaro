using MietHahn.Domain.Common;

namespace MietHahn.Domain.Bank;

/// <summary>
/// One part of an assigned transaction: either a payment of a lease (income) or a cost item of a receipt (expense).
/// The parts of a transaction add up to its amount exactly (<see cref="TransactionSplit"/>); undoing the assignment
/// removes everything the parts created.
/// </summary>
public sealed class TransactionAssignment : Entity
{
    private TransactionAssignment()
    {
    }

    public TransactionAssignment(Guid transactionId, decimal amount)
    {
        TransactionId = transactionId;
        Amount = amount;
    }

    public Guid TransactionId { get; private set; }

    /// <summary>Signed part of the transaction amount (negative = outflow).</summary>
    public decimal Amount { get; private set; }

    public Guid? PaymentId { get; set; }

    public Guid? ReceiptId { get; set; }

    public Guid? CostItemId { get; set; }

    /// <summary>The receipt was created with the assignment (and is removed again on undo if it stays empty).</summary>
    public bool ReceiptCreated { get; set; }

    /// <summary>The assignment set the payment date of the receipt.</summary>
    public bool PaidOnSet { get; set; }
}

/// <summary>Splits the amount of a transaction into parts that must add up exactly.</summary>
public static class TransactionSplit
{
    /// <summary>
    /// Resolves the parts entered as positive amounts to signed amounts of the transaction. One part may stay empty
    /// and receives the remainder. The sum of all parts must equal the transaction amount to the cent.
    /// </summary>
    public static IReadOnlyList<decimal> Resolve(decimal transactionAmount, IReadOnlyList<decimal?> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count == 0)
        {
            throw new RuleViolationException("Bitte mindestens eine Zuordnung angeben.");
        }

        if (transactionAmount == 0)
        {
            throw new RuleViolationException("Ein Umsatz über 0 € lässt sich nicht zuordnen.");
        }

        var total = Math.Abs(transactionAmount);
        var sign = Math.Sign(transactionAmount);
        if (parts.Count(p => p is null) > 1)
        {
            throw new RuleViolationException("Nur ein Teilbetrag darf leer bleiben (er erhält den Rest).");
        }

        foreach (var part in parts.OfType<decimal>())
        {
            if (part <= 0 || decimal.Round(part, 2) != part)
            {
                throw new RuleViolationException("Teilbeträge müssen größer als 0 sein und höchstens zwei Nachkommastellen haben.");
            }
        }

        var sum = parts.OfType<decimal>().Sum();
        if (parts.Any(p => p is null))
        {
            var rest = total - sum;
            if (rest <= 0)
            {
                throw new RuleViolationException($"Die Teilbeträge ({Money(sum)}) lassen keinen Rest vom Umsatz ({Money(total)}) übrig.");
            }

            return parts.Select(p => sign * (p ?? rest)).ToList();
        }

        if (sum != total)
        {
            throw new RuleViolationException($"Die Teilbeträge ergeben {Money(sum)}, der Umsatz beträgt {Money(total)} (Differenz {Money(total - sum)}).");
        }

        return parts.Select(p => sign * p!.Value).ToList();
    }

    private static string Money(decimal amount) => amount.ToString("#,##0.00", System.Globalization.CultureInfo.GetCultureInfo("de-DE")) + " €";
}
