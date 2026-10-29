namespace Elocaro.Domain.Receipts;

/// <summary>A suggested value with the confidence (0 to 1) the extraction reported for it.</summary>
public readonly record struct Suggested<T>(T? Value, double Confidence)
{
    /// <summary>Below this confidence the user is asked to check the field twice.</summary>
    public const double Uncertain = 0.75;

    public bool HasValue => Value is not null && !(Value is string text && string.IsNullOrWhiteSpace(text));

    /// <summary>A value that is present but not trustworthy enough to be accepted without a second look.</summary>
    public bool IsUncertain => HasValue && Confidence < Uncertain;

    public Suggested<T> WithConfidence(double confidence) => this with { Confidence = Math.Clamp(confidence, 0, 1) };

    public Suggested<T> CappedAt(double ceiling) => this with { Confidence = Math.Min(Confidence, ceiling) };
}

/// <summary>One line of the invoice as read from the document.</summary>
public sealed record SuggestedLine(string Description, decimal? Amount, string? CostType);

/// <summary>
/// What an extractor read from a receipt document. This is only a suggestion: it pre-fills the form,
/// the user always confirms (or corrects) the values.
/// </summary>
public sealed record ReceiptSuggestion
{
    public Suggested<string> SupplierName { get; init; }

    public Suggested<string> Iban { get; init; }

    public Suggested<string> InvoiceNumber { get; init; }

    public Suggested<DateOnly?> InvoiceDate { get; init; }

    public Suggested<DateOnly?> ServiceFrom { get; init; }

    public Suggested<DateOnly?> ServiceTo { get; init; }

    public Suggested<DateOnly?> DueOn { get; init; }

    public Suggested<decimal?> Gross { get; init; }

    public Suggested<decimal?> Net { get; init; }

    public Suggested<decimal?> Vat { get; init; }

    /// <summary>Key from <see cref="CostTypes"/>.</summary>
    public Suggested<string> CostType { get; init; }

    public Suggested<Section35a?> Section35a { get; init; }

    /// <summary>Labor costs including VAT (§ 35a EStG).</summary>
    public Suggested<decimal?> LaborCosts { get; init; }

    public Suggested<decimal?> FuelQuantity { get; init; }

    public Suggested<string> FuelUnit { get; init; }

    public Suggested<decimal?> Co2Kg { get; init; }

    public IReadOnlyList<SuggestedLine> Lines { get; init; } = [];

    /// <summary>Plausibility findings and remarks phrased for the user.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Why <see cref="CostType"/> came out as it did, e.g. "aus früheren Belegen dieses Lieferanten".</summary>
    public string? CostTypeReason { get; init; }

    /// <summary>The fields that are present but uncertain, by their property name (for highlighting in the form).</summary>
    public IReadOnlySet<string> UncertainFields()
    {
        var fields = new HashSet<string>(StringComparer.Ordinal);
        void Add(string name, bool uncertain)
        {
            if (uncertain)
            {
                fields.Add(name);
            }
        }

        Add(nameof(SupplierName), SupplierName.IsUncertain);
        Add(nameof(Iban), Iban.IsUncertain);
        Add(nameof(InvoiceNumber), InvoiceNumber.IsUncertain);
        Add(nameof(InvoiceDate), InvoiceDate.IsUncertain);
        Add(nameof(ServiceFrom), ServiceFrom.IsUncertain);
        Add(nameof(ServiceTo), ServiceTo.IsUncertain);
        Add(nameof(DueOn), DueOn.IsUncertain);
        Add(nameof(Gross), Gross.IsUncertain);
        Add(nameof(Net), Net.IsUncertain);
        Add(nameof(Vat), Vat.IsUncertain);
        Add(nameof(CostType), CostType.IsUncertain);
        Add(nameof(Section35a), Section35a.IsUncertain);
        Add(nameof(LaborCosts), LaborCosts.IsUncertain);
        Add(nameof(FuelQuantity), FuelQuantity.IsUncertain);
        Add(nameof(FuelUnit), FuelUnit.IsUncertain);
        Add(nameof(Co2Kg), Co2Kg.IsUncertain);
        return fields;
    }
}
