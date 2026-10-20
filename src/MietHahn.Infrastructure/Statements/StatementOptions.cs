namespace MietHahn.Infrastructure.Statements;

/// <summary>Configuration section <c>Statements</c>.</summary>
public sealed class StatementOptions
{
    public const string Section = "Statements";

    /// <summary>Days after dispatch until an additional payment is due or a credit is paid out.</summary>
    public int PaymentDays { get; set; } = 30;

    /// <summary>Safety margin in percent on the proposal for new prepayments.</summary>
    public decimal PrepaymentSafetyPercent { get; set; } = 5m;
}
