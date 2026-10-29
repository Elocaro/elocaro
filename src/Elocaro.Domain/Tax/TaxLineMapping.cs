using Elocaro.Domain.Common;

namespace Elocaro.Domain.Tax;

/// <summary>Line of the Anlage V for a category in a tax year. The line numbers of the form change annually, hence per year.</summary>
public sealed class TaxLineMapping : Entity
{
    private TaxLineMapping()
    {
    }

    public TaxLineMapping(int taxYear, AnlageVCategory category, int lineNumber)
    {
        TaxYear = taxYear;
        Category = category;
        SetLine(lineNumber);
    }

    public int TaxYear { get; private set; }

    public AnlageVCategory Category { get; private set; }

    public int LineNumber { get; private set; }

    public void SetLine(int lineNumber)
    {
        if (lineNumber is < 1 or > 200)
        {
            throw new RuleViolationException("Die Zeilennummer muss zwischen 1 und 200 liegen.");
        }

        LineNumber = lineNumber;
    }
}
