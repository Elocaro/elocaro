using MietHahn.Domain.Common;

namespace MietHahn.Domain.Tax;

/// <summary>
/// Election to spread a maintenance expense (cost item of the type "Instandhaltung") evenly over two to five years
/// (§ 82b EStDV). Cost items without a distribution are deducted immediately.
/// </summary>
public sealed class MaintenanceDistribution : Entity
{
    public const int MinYears = 2;

    public const int MaxYears = 5;

    private MaintenanceDistribution()
    {
    }

    public MaintenanceDistribution(Guid costItemId, Guid propertyId, decimal amount, int startYear, int years)
    {
        if (amount <= 0)
        {
            throw new RuleViolationException("Nur Aufwendungen mit positivem Betrag können verteilt werden.");
        }

        CostItemId = costItemId;
        PropertyId = propertyId;
        Amount = amount;
        StartYear = startYear;
        SetYears(years);
    }

    public Guid CostItemId { get; private set; }

    public Guid PropertyId { get; private set; }

    /// <summary>Amount of the cost item at the time of the election.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Year of payment: the first year of the distribution.</summary>
    public int StartYear { get; private set; }

    public int Years { get; private set; }

    public void SetYears(int years)
    {
        if (years is < MinYears or > MaxYears)
        {
            throw new RuleViolationException($"Der Erhaltungsaufwand kann auf {MinYears} bis {MaxYears} Jahre verteilt werden.");
        }

        Years = years;
    }

    public IReadOnlyList<MaintenanceInstalment> Schedule() => MaintenanceCalculator.Schedule(Amount, StartYear, Years);
}
