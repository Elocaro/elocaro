using Elocaro.Domain.Common;

namespace Elocaro.Domain.Receipts;

/// <summary>
/// Plausibility checks for an extraction result. Models make mistakes (and invent things), so everything that
/// does not add up is either removed or marked as uncertain and explained in <see cref="ReceiptSuggestion.Warnings"/>.
/// Pure logic, no I/O.
/// </summary>
public static class SuggestionChecks
{
    /// <summary>Same limit as the receipt form.</summary>
    public const decimal MaxAmount = 1_000_000m;

    private const double Doubtful = 0.4;
    private static readonly DateOnly EarliestDate = new(2000, 1, 1);
    private static readonly decimal[] VatRates = [0m, 0.07m, 0.19m];

    public static ReceiptSuggestion Check(ReceiptSuggestion suggestion, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        var warnings = new List<string>(suggestion.Warnings);
        var s = suggestion;

        s = s with
        {
            Gross = Amount(s.Gross, "Brutto", warnings, allowNegative: true),
            Net = Amount(s.Net, "Netto", warnings, allowNegative: true),
            Vat = Amount(s.Vat, "Umsatzsteuer", warnings, allowNegative: true),
            LaborCosts = Amount(s.LaborCosts, "Arbeitskosten", warnings, allowNegative: false),
            InvoiceDate = Date(s.InvoiceDate, "Rechnungsdatum", today.AddDays(1), warnings),
            ServiceFrom = Date(s.ServiceFrom, "Leistung von", today.AddYears(1), warnings),
            ServiceTo = Date(s.ServiceTo, "Leistung bis", today.AddYears(1), warnings),
            DueOn = Date(s.DueOn, "Fälligkeit", today.AddYears(2), warnings),
        };

        s = Totals(s, warnings);
        s = Dates(s, warnings);
        s = Details(s, warnings);
        s = Lines(s, warnings);
        return s with { Warnings = warnings.Distinct(StringComparer.Ordinal).ToList() };
    }

    private static ReceiptSuggestion Totals(ReceiptSuggestion s, List<string> warnings)
    {
        var gross = s.Gross;
        var net = s.Net;
        var vat = s.Vat;
        if (gross.Value is { } g && net.Value is { } n && vat.Value is { } v)
        {
            if (n + v != g)
            {
                warnings.Add($"Netto ({GermanText.Money(n)}) + Umsatzsteuer ({GermanText.Money(v)}) ergibt nicht den Bruttobetrag ({GermanText.Money(g)}). Bitte die Beträge prüfen.");
                gross = gross.CappedAt(Doubtful);
                net = net.CappedAt(Doubtful);
                vat = vat.CappedAt(Doubtful);
            }
            else
            {
                CheckRate(n, v, warnings);
            }
        }
        else if (gross.Value is { } g2 && net.Value is { } n2 && !vat.HasValue)
        {
            vat = new Suggested<decimal?>(g2 - n2, Math.Min(gross.Confidence, net.Confidence) * 0.9);
            CheckRate(n2, g2 - n2, warnings);
        }
        else if (gross.Value is { } g3 && vat.Value is { } v3 && !net.HasValue)
        {
            net = new Suggested<decimal?>(g3 - v3, Math.Min(gross.Confidence, vat.Confidence) * 0.9);
        }
        else if (net.Value is { } n4 && vat.Value is { } v4 && !gross.HasValue)
        {
            gross = new Suggested<decimal?>(n4 + v4, Math.Min(net.Confidence, vat.Confidence) * 0.9);
        }

        if (gross.Value == 0)
        {
            gross = default;
            warnings.Add("Der erkannte Bruttobetrag war 0 und wurde verworfen.");
        }

        return s with { Gross = gross, Net = net, Vat = vat };
    }

    private static void CheckRate(decimal net, decimal vat, List<string> warnings)
    {
        if (net == 0)
        {
            return;
        }

        var rate = vat / net;
        if (!VatRates.Any(r => Math.Abs(rate - r) <= 0.005m))
        {
            warnings.Add($"Der Umsatzsteuersatz ({rate:P1}) ist ungewöhnlich (üblich: 0 %, 7 %, 19 %).");
        }
    }

    private static ReceiptSuggestion Dates(ReceiptSuggestion s, List<string> warnings)
    {
        if (s.ServiceFrom.Value is { } from && s.ServiceTo.Value is { } to && to < from)
        {
            warnings.Add("Der Leistungszeitraum endet vor seinem Beginn und wurde verworfen.");
            s = s with { ServiceFrom = default, ServiceTo = default };
        }
        else if (s.ServiceFrom.Value is null && s.ServiceTo.Value is not null)
        {
            warnings.Add("Der Beginn des Leistungszeitraums fehlt; das Ende wurde verworfen.");
            s = s with { ServiceTo = default };
        }

        if (s.DueOn.Value is { } due && s.InvoiceDate.Value is { } invoice && due < invoice)
        {
            warnings.Add("Das Fälligkeitsdatum liegt vor dem Rechnungsdatum.");
            s = s with { DueOn = s.DueOn.CappedAt(Doubtful) };
        }

        return s;
    }

    private static ReceiptSuggestion Details(ReceiptSuggestion s, List<string> warnings)
    {
        if (s.Iban.HasValue)
        {
            if (Iban.IsValid(s.Iban.Value))
            {
                s = s with { Iban = new Suggested<string>(Iban.Format(s.Iban.Value!), s.Iban.Confidence) };
            }
            else
            {
                warnings.Add("Die erkannte IBAN ist ungültig (Prüfsumme) und wurde verworfen.");
                s = s with { Iban = default };
            }
        }

        if (s.CostType.HasValue && !CostTypes.IsKnown(s.CostType.Value!))
        {
            warnings.Add("Die vorgeschlagene Kostenart ist unbekannt und wurde verworfen.");
            s = s with { CostType = default };
        }

        if (s.LaborCosts.Value is { } labor && s.Gross.Value is { } gross && labor > Math.Abs(gross))
        {
            warnings.Add("Die Arbeitskosten übersteigen den Rechnungsbetrag und wurden verworfen.");
            s = s with { LaborCosts = default };
        }

        if (s.Section35a.Value is not null and not Section35a.None && s.LaborCosts.Value is null)
        {
            warnings.Add("§ 35a EStG erkannt, aber ohne Arbeitskostenanteil. Bitte den Anteil von Hand eintragen.");
            s = s with { Section35a = s.Section35a.CappedAt(Doubtful) };
        }

        if (s.FuelQuantity.Value < 0)
        {
            s = s with { FuelQuantity = default };
        }

        if (s.Co2Kg.Value < 0)
        {
            s = s with { Co2Kg = default };
        }

        return s;
    }

    private static ReceiptSuggestion Lines(ReceiptSuggestion s, List<string> warnings)
    {
        var lines = s.Lines
            .Where(l => !string.IsNullOrWhiteSpace(l.Description))
            .Select(l => l with
            {
                Amount = l.Amount is { } a && Math.Abs(a) <= MaxAmount ? decimal.Round(a, 2, MidpointRounding.AwayFromZero) : null,
                CostType = l.CostType is { } k && CostTypes.IsKnown(k) ? k : null,
            })
            .ToList();
        if (s.Gross.Value is { } gross && lines.Count > 0 && lines.All(l => l.Amount is not null))
        {
            var sum = lines.Sum(l => l.Amount!.Value);

            // Lines are often net amounts, so a match with net or gross is fine.
            if (sum != gross && (s.Net.Value is not { } net || sum != net))
            {
                warnings.Add($"Die Summe der erkannten Positionen ({GermanText.Money(sum)}) passt weder zum Brutto- noch zum Nettobetrag.");
            }
        }

        return s with { Lines = lines };
    }

    private static Suggested<decimal?> Amount(Suggested<decimal?> value, string name, List<string> warnings, bool allowNegative)
    {
        if (value.Value is not { } amount)
        {
            return value;
        }

        if (Math.Abs(amount) > MaxAmount || (!allowNegative && amount < 0))
        {
            warnings.Add($"Der erkannte Betrag „{name}“ ({GermanText.Money(amount)}) ist unplausibel und wurde verworfen.");
            return default;
        }

        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        return rounded == amount ? value : value.CappedAt(Doubtful) with { Value = rounded };
    }

    private static Suggested<DateOnly?> Date(Suggested<DateOnly?> value, string name, DateOnly latest, List<string> warnings)
    {
        if (value.Value is not { } date)
        {
            return value;
        }

        if (date < EarliestDate)
        {
            warnings.Add($"Das erkannte Datum „{name}“ ({date:dd.MM.yyyy}) ist unplausibel und wurde verworfen.");
            return default;
        }

        if (date > latest)
        {
            warnings.Add($"Das Datum „{name}“ ({date:dd.MM.yyyy}) liegt weit in der Zukunft. Bitte prüfen.");
            return value.CappedAt(Doubtful);
        }

        return value;
    }
}
