using MietHahn.Domain.Common;
using MietHahn.Domain.Heating;

namespace MietHahn.Domain.Receipts;

/// <summary>Share under § 35a EStG (tax reduction for the tenant, shown in the utility cost statement).</summary>
public enum Section35a
{
    None = 0,
    HouseholdService = 1,
    TradesmanService = 2,
}

/// <summary>
/// Incoming invoice or cost receipt. The files are stored immutably in the file store (SHA-256); data changes
/// are recorded in the change log, deletion is logical only.
/// </summary>
public sealed class Receipt : Entity
{
    private Receipt()
    {
    }

    /// <summary>Set the supplier and remaining details afterwards and call <see cref="Validate"/>.</summary>
    public Receipt(DateOnly invoiceDate, decimal gross)
    {
        InvoiceDate = invoiceDate;
        Gross = gross;
    }

    /// <summary>Supplier from the contacts; alternatively just a name.</summary>
    public Guid? SupplierId { get; set; }

    public string? SupplierName { get; set; }

    public string? InvoiceNumber { get; set; }

    public DateOnly InvoiceDate { get; set; }

    public DateOnly? ServiceFrom { get; private set; }

    public DateOnly? ServiceTo { get; private set; }

    /// <summary>Stored as two columns (optional period).</summary>
    public Period? ServicePeriod => ServiceFrom is { } from ? new Period(from, ServiceTo) : null;

    public decimal Gross { get; set; }

    public decimal? Net { get; set; }

    public decimal? Vat { get; set; }

    public DateOnly? DueOn { get; set; }

    /// <summary>Manually entered payment date (e.g. cash or direct debit without bank import).</summary>
    public DateOnly? PaidOn { get; set; }

    /// <summary>Fuels: delivered quantity, unit (l, kWh, kg, m³) and CO₂ emissions according to the supplier (CO2KostAufG).</summary>
    public decimal? FuelQuantity { get; set; }

    public string? FuelUnit { get; set; }

    public decimal? Co2Kg { get; set; }

    public string? Note { get; set; }

    /// <summary>Received without amount (e.g. by mail): waits to be completed. Cleared as soon as an amount is entered.</summary>
    public bool Incomplete { get; private set; }

    public List<ReceiptFile> Files { get; private set; } = [];

    public List<CostItem> Items { get; private set; } = [];

    public decimal Allocated => Items.Sum(p => p.Amount);

    public bool IsFullyAllocated => Items.Count > 0 && Allocated == Gross;

    public void MarkIncomplete() => Incomplete = true;

    public void SetServicePeriod(DateOnly? from, DateOnly? to)
    {
        if (from is null && to is not null)
        {
            throw new RuleViolationException("Bitte auch den Beginn des Leistungszeitraums angeben.");
        }

        if (to < from)
        {
            throw new RuleViolationException("Der Leistungszeitraum endet vor seinem Beginn.");
        }

        ServiceFrom = from;
        ServiceTo = to;
    }

    public void AddFile(Guid fileId)
    {
        if (Files.All(d => d.FileId != fileId))
        {
            Files.Add(new ReceiptFile(Id, fileId, Files.Count == 0 ? 1 : Files.Max(d => d.Page) + 1));
        }
    }

    public void RemoveFile(Guid fileId) => Files.RemoveAll(d => d.FileId == fileId);

    public CostItem AddCostItem(string costType, decimal amount, Guid propertyId, Period billingPeriod, bool? allocable = null)
    {
        var kind = CostTypes.Find(costType) ?? throw new RuleViolationException("Unbekannte Kostenart.");
        if (amount == 0 || decimal.Round(amount, 2) != amount)
        {
            throw new RuleViolationException("Bitte einen Betrag ungleich 0 mit höchstens zwei Nachkommastellen angeben.");
        }

        if (Math.Abs(Allocated + amount) > Math.Abs(Gross) || Math.Sign(amount) != Math.Sign(Gross))
        {
            throw new RuleViolationException($"Die Positionen übersteigen den Rechnungsbetrag (offen: {GermanText.Money(Gross - Allocated)}).");
        }

        var position = new CostItem(Id, kind.Key, amount, propertyId, billingPeriod, allocable ?? kind.Allocable);
        Items.Add(position);
        return position;
    }

    public void RemoveCostItem(Guid positionId) => Items.RemoveAll(p => p.Id == positionId);

    /// <summary>New invoice amount: must not be below the sum of the items.</summary>
    public void SetGross(decimal gross)
    {
        if (Items.Count > 0 && (Math.Abs(Allocated) > Math.Abs(gross) || Math.Sign(gross) != Math.Sign(Gross)))
        {
            throw new RuleViolationException("Der Betrag ist kleiner als die Summe der Positionen; erst die Positionen anpassen.");
        }

        Gross = gross;
        Validate();
    }

    public void Validate()
    {
        if (Gross != 0)
        {
            Incomplete = false;
        }

        if ((Gross == 0 && !Incomplete) || decimal.Round(Gross, 2) != Gross)
        {
            throw new RuleViolationException("Bitte einen Rechnungsbetrag ungleich 0 mit höchstens zwei Nachkommastellen angeben.");
        }

        if (Net is { } net && Vat is { } vat && net + vat != Gross)
        {
            throw new RuleViolationException($"Netto + Umsatzsteuer ({GermanText.Money(net + vat)}) ergibt nicht den Bruttobetrag ({GermanText.Money(Gross)}).");
        }

        if (string.IsNullOrWhiteSpace(SupplierName) && SupplierId is null)
        {
            throw new RuleViolationException("Bitte den Lieferanten angeben.");
        }

        if (Co2Kg < 0 || FuelQuantity < 0)
        {
            throw new RuleViolationException("Menge und CO₂ dürfen nicht negativ sein.");
        }
    }
}

/// <summary>A page or file of a receipt.</summary>
public sealed class ReceiptFile : Entity
{
    private ReceiptFile()
    {
    }

    internal ReceiptFile(Guid receiptId, Guid fileId, int page)
    {
        ReceiptId = receiptId;
        FileId = fileId;
        Page = page;
    }

    public Guid ReceiptId { get; private set; }

    public Guid FileId { get; private set; }

    public int Page { get; private set; }
}

/// <summary>Part of a receipt for one cost type, one property (optionally a unit) and one billing period.</summary>
public sealed class CostItem : Entity
{
    private CostItem()
    {
        CostType = "";
    }

    internal CostItem(Guid receiptId, string costType, decimal amount, Guid propertyId, Period billingPeriod, bool allocable)
    {
        if (billingPeriod.IsOpenEnded)
        {
            throw new RuleViolationException("Der Abrechnungszeitraum braucht ein Ende.");
        }

        ReceiptId = receiptId;
        CostType = costType;
        Amount = amount;
        PropertyId = propertyId;
        BillingPeriod = billingPeriod;
        Allocable = allocable;
    }

    public Guid ReceiptId { get; private set; }

    /// <summary>Key from <see cref="CostTypes"/>.</summary>
    public string CostType { get; private set; }

    public decimal Amount { get; private set; }

    public Guid PropertyId { get; private set; }

    /// <summary>Only for costs of a single unit (direct assignment).</summary>
    public Guid? UnitId { get; set; }

    /// <summary>Own subtype of the cost type (<see cref="CostSubtype"/>), optional.</summary>
    public Guid? SubtypeId { get; set; }

    public Period BillingPeriod { get; private set; }

    public bool Allocable { get; set; }

    /// <summary>For heating costs (cost types 4 to 6): what the item is; without it fuel receipts count as fuel and everything else as other costs.</summary>
    public HeatingCostKind? HeatingKind { get; set; }

    public Section35a Section35a { get; private set; }

    /// <summary>Labor, machine and travel costs incl. VAT; only these are eligible under § 35a.</summary>
    public decimal? LaborCosts { get; private set; }

    public string? Note { get; set; }

    public void SetSection35a(Section35a kind, decimal? laborCosts)
    {
        if (kind == Section35a.None)
        {
            Section35a = kind;
            LaborCosts = null;
            return;
        }

        if (laborCosts is not { } costs || costs <= 0 || Math.Abs(costs) > Math.Abs(Amount))
        {
            throw new RuleViolationException("Bitte den Arbeitskostenanteil angeben (höchstens der Positionsbetrag).");
        }

        Section35a = kind;
        LaborCosts = costs;
    }
}
