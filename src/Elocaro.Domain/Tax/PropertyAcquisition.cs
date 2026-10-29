using Elocaro.Domain.Common;

namespace Elocaro.Domain.Tax;

/// <summary>Acquisition data of a property (one per property): basis of the building depreciation.</summary>
public sealed class PropertyAcquisition : Entity
{
    private PropertyAcquisition()
    {
    }

    public PropertyAcquisition(Guid propertyId) => PropertyId = propertyId;

    public Guid PropertyId { get; private set; }

    public DateOnly PurchaseDate { get; private set; }

    public decimal PurchasePrice { get; private set; }

    public decimal Notary { get; private set; }

    public decimal LandRegistry { get; private set; }

    public decimal TransferTax { get; private set; }

    public decimal Broker { get; private set; }

    /// <summary>Share of land in the purchase price in percent (0–100).</summary>
    public decimal LandSharePercent { get; private set; }

    public int CompletionYear { get; private set; }

    public decimal? RateOverridePercent { get; private set; }

    public decimal? SpecialRatePercent { get; private set; }

    public int SpecialYears { get; private set; } = 4;

    public string? Notes { get; private set; }

    public decimal AncillaryCosts => Notary + LandRegistry + TransferTax + Broker;

    public void Set(
        DateOnly purchaseDate,
        decimal purchasePrice,
        (decimal Notary, decimal LandRegistry, decimal TransferTax, decimal Broker) ancillary,
        decimal landSharePercent,
        int completionYear,
        decimal? rateOverridePercent,
        decimal? specialRatePercent,
        int specialYears,
        string? notes)
    {
        if (purchasePrice <= 0)
        {
            throw new RuleViolationException("Der Kaufpreis muss größer als 0 sein.");
        }

        if (ancillary.Notary < 0 || ancillary.LandRegistry < 0 || ancillary.TransferTax < 0 || ancillary.Broker < 0)
        {
            throw new RuleViolationException("Anschaffungsnebenkosten dürfen nicht negativ sein.");
        }

        if (landSharePercent is < 0 or >= 100)
        {
            throw new RuleViolationException("Der Anteil Grund und Boden muss zwischen 0 und unter 100 % liegen.");
        }

        if (completionYear is < 1700 or > 2100)
        {
            throw new RuleViolationException("Bitte ein gültiges Baujahr bzw. Fertigstellungsjahr angeben.");
        }

        if (rateOverridePercent is <= 0 or > 100 || specialRatePercent is <= 0 or > 100)
        {
            throw new RuleViolationException("Abschreibungssätze müssen zwischen 0 und 100 % liegen.");
        }

        PurchaseDate = purchaseDate;
        PurchasePrice = purchasePrice;
        (Notary, LandRegistry, TransferTax, Broker) = ancillary;
        LandSharePercent = landSharePercent;
        CompletionYear = completionYear;
        RateOverridePercent = rateOverridePercent;
        SpecialRatePercent = specialRatePercent;
        SpecialYears = specialYears < 1 ? 1 : specialYears;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public BuildingAcquisition ToCalculation() =>
        new(PurchaseDate, PurchasePrice, AncillaryCosts, LandSharePercent, CompletionYear, RateOverridePercent, SpecialRatePercent, SpecialYears);
}

/// <summary>Subsequent cost of a building (production cost to be capitalised or renovation within the 15 % limit).</summary>
public sealed class PropertyCost : Entity
{
    private PropertyCost()
    {
        Description = "";
    }

    public PropertyCost(Guid propertyId, DateOnly date, decimal amount, string description, bool isMaintenance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (amount <= 0)
        {
            throw new RuleViolationException("Der Betrag muss größer als 0 sein.");
        }

        PropertyId = propertyId;
        Date = date;
        Amount = amount;
        Description = description.Trim();
        IsMaintenance = isMaintenance;
    }

    public Guid PropertyId { get; private set; }

    public DateOnly Date { get; private set; }

    /// <summary>Net amount.</summary>
    public decimal Amount { get; private set; }

    public string Description { get; private set; }

    public bool IsMaintenance { get; private set; }

    public void Change(DateOnly date, decimal amount, string description, bool isMaintenance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (amount <= 0)
        {
            throw new RuleViolationException("Der Betrag muss größer als 0 sein.");
        }

        Date = date;
        Amount = amount;
        Description = description.Trim();
        IsMaintenance = isMaintenance;
    }

    public SubsequentCost ToCalculation() => new(Date, Amount, Description, IsMaintenance);
}

/// <summary>Movable asset with own useful life, e.g. a fitted kitchen.</summary>
public sealed class PropertyAsset : Entity
{
    private PropertyAsset()
    {
        Name = "";
    }

    public PropertyAsset(Guid propertyId, string name, DateOnly acquisitionDate, decimal cost, int usefulLifeYears)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (cost <= 0)
        {
            throw new RuleViolationException("Die Anschaffungskosten müssen größer als 0 sein.");
        }

        if (usefulLifeYears is < 1 or > 100)
        {
            throw new RuleViolationException("Die Nutzungsdauer muss zwischen 1 und 100 Jahren liegen.");
        }

        PropertyId = propertyId;
        Name = name.Trim();
        AcquisitionDate = acquisitionDate;
        Cost = cost;
        UsefulLifeYears = usefulLifeYears;
    }

    public Guid PropertyId { get; private set; }

    public string Name { get; private set; }

    public DateOnly AcquisitionDate { get; private set; }

    public decimal Cost { get; private set; }

    public int UsefulLifeYears { get; private set; }

    public void Change(string name, DateOnly acquisitionDate, decimal cost, int usefulLifeYears)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (cost <= 0)
        {
            throw new RuleViolationException("Die Anschaffungskosten müssen größer als 0 sein.");
        }

        if (usefulLifeYears is < 1 or > 100)
        {
            throw new RuleViolationException("Die Nutzungsdauer muss zwischen 1 und 100 Jahren liegen.");
        }

        Name = name.Trim();
        AcquisitionDate = acquisitionDate;
        Cost = cost;
        UsefulLifeYears = usefulLifeYears;
    }

    public MovableAsset ToCalculation() => new(Name, AcquisitionDate, Cost, UsefulLifeYears);
}
