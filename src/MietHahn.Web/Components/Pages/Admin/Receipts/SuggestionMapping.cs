using MietHahn.Application.Receipts;
using MietHahn.Domain.Receipts;

namespace MietHahn.Web.Components.Pages.Admin.Receipts;

/// <summary>Pre-fills the form models from an extraction suggestion. Nothing is saved; the user confirms the form.</summary>
internal static class SuggestionMapping
{
    /// <summary>Values from the suggestion replace those in <paramref name="input"/>; fields without a suggestion keep their value.</summary>
    public static ReceiptInput Apply(ReceiptInput input, ReceiptSuggestion s, Guid? supplierId)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(s);
        if (supplierId is not null)
        {
            input.SupplierId = supplierId;
            input.SupplierName = null;
        }
        else if (s.SupplierName.HasValue)
        {
            input.SupplierName = s.SupplierName.Value;
        }

        input.InvoiceNumber = s.InvoiceNumber.Value ?? input.InvoiceNumber;
        input.InvoiceDate = s.InvoiceDate.Value ?? input.InvoiceDate;
        input.ServiceFrom = s.ServiceFrom.Value ?? input.ServiceFrom;
        input.ServiceTo = s.ServiceTo.Value ?? input.ServiceTo;
        input.Gross = s.Gross.Value ?? input.Gross;
        input.Net = s.Net.Value ?? input.Net;
        input.Vat = s.Vat.Value ?? input.Vat;
        input.DueOn = s.DueOn.Value ?? input.DueOn;
        input.FuelQuantity = s.FuelQuantity.Value ?? input.FuelQuantity;
        input.FuelUnit = s.FuelUnit.Value ?? input.FuelUnit;
        input.Co2Kg = s.Co2Kg.Value ?? input.Co2Kg;
        if (s.Iban.HasValue)
        {
            var line = $"IBAN des Lieferanten laut Beleg: {s.Iban.Value}";
            input.Note = string.IsNullOrWhiteSpace(input.Note) ? line : input.Note.Contains(line, StringComparison.Ordinal) ? input.Note : input.Note + "\n" + line;
        }

        return input;
    }

    public static ReceiptInput Copy(ReceiptInput source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new ReceiptInput
        {
            SupplierId = source.SupplierId,
            SupplierName = source.SupplierName,
            InvoiceNumber = source.InvoiceNumber,
            InvoiceDate = source.InvoiceDate,
            ServiceFrom = source.ServiceFrom,
            ServiceTo = source.ServiceTo,
            Gross = source.Gross,
            Net = source.Net,
            Vat = source.Vat,
            DueOn = source.DueOn,
            PaidOn = source.PaidOn,
            FuelQuantity = source.FuelQuantity,
            FuelUnit = source.FuelUnit,
            Co2Kg = source.Co2Kg,
            Note = source.Note,
        };
    }

    /// <summary>Cost type, § 35a and (if there is only one property) the property.</summary>
    public static CostItemInput ToPosition(ReceiptSuggestion s, ReceiptOptions options)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(options);
        var position = new CostItemInput { CostType = s.CostType.Value ?? "" };
        if (options.Properties.Count == 1)
        {
            position.PropertyId = options.Properties[0].Id;
        }

        if (s.Section35a.Value is { } kind and not Section35a.None && s.LaborCosts.Value is { } labor)
        {
            position.Section35a = kind;
            position.LaborCosts = labor;
        }

        return position;
    }
}
