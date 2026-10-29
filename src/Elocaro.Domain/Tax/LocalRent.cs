using Elocaro.Domain.Common;

namespace Elocaro.Domain.Tax;

/// <summary>
/// Local comparative rent of a property for a tax year (per year, including allocable costs): reference for the
/// cheap-letting rule of § 21 Abs. 2 EStG.
/// </summary>
public sealed class LocalRent : Entity
{
    private LocalRent()
    {
    }

    public LocalRent(Guid propertyId, int taxYear, decimal annualRent)
    {
        PropertyId = propertyId;
        TaxYear = taxYear;
        SetRent(annualRent);
    }

    public Guid PropertyId { get; private set; }

    public int TaxYear { get; private set; }

    /// <summary>Local comparative rent for the whole year in the rented period, including allocable costs.</summary>
    public decimal AnnualRent { get; private set; }

    public void SetRent(decimal annualRent)
    {
        if (annualRent <= 0)
        {
            throw new RuleViolationException("Die ortsübliche Jahresmiete muss größer als 0 sein.");
        }

        AnnualRent = annualRent;
    }
}
