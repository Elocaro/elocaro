namespace MietHahn.Domain.Heating;

/// <summary>A heating oil delivery in the billing period.</summary>
public sealed record OilDelivery(DateOnly Date, decimal Liters, decimal Cost);

/// <summary>Oil consumption of the billing period with its valuation.</summary>
public sealed record OilConsumption(decimal Liters, decimal Cost, string Calculation);

/// <summary>Consumption and cost of heating oil from the tank stock: opening stock + deliveries − closing stock (pure).</summary>
public static class OilConsumptionCalculator
{
    /// <param name="openingPricePerLiter">Price of the opening stock; <c>null</c> = price per liter of the first delivery.</param>
    public static OilConsumption Calculate(
        decimal openingLiters,
        decimal closingLiters,
        decimal? openingPricePerLiter,
        IReadOnlyList<OilDelivery> deliveries,
        OilValuation valuation)
    {
        ArgumentNullException.ThrowIfNull(deliveries);
        var ordered = deliveries.OrderBy(d => d.Date).ToList();
        var delivered = ordered.Sum(d => d.Liters);
        var available = openingLiters + delivered;
        var consumed = available - closingLiters;
        if (consumed < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(closingLiters), "Der Endbestand ist größer als Anfangsbestand und Lieferungen.");
        }

        var firstPrice = ordered.FirstOrDefault(d => d.Liters > 0) is { } first ? first.Cost / first.Liters : 0m;
        var openingPrice = openingPricePerLiter ?? firstPrice;
        var calculation = $"{Num(openingLiters)} l Anfangsbestand + {Num(delivered)} l Lieferungen − {Num(closingLiters)} l Endbestand = {Num(consumed)} l";

        if (valuation == OilValuation.WeightedAverage)
        {
            var value = (openingLiters * openingPrice) + ordered.Sum(d => d.Cost);
            var cost = available == 0 ? 0m : consumed * value / available;
            return new OilConsumption(consumed, cost, $"{calculation}; gewichteter Durchschnitt: {Num(consumed)} l × {value:#,##0.00} € ÷ {Num(available)} l");
        }

        var layers = new List<(decimal Liters, decimal Price)>();
        if (openingLiters > 0)
        {
            layers.Add((openingLiters, openingPrice));
        }

        layers.AddRange(ordered.Where(d => d.Liters > 0).Select(d => (d.Liters, d.Cost / d.Liters)));
        var remaining = consumed;
        decimal fifoCost = 0;
        foreach (var (liters, price) in layers)
        {
            var take = Math.Min(remaining, liters);
            fifoCost += take * price;
            remaining -= take;
            if (remaining <= 0)
            {
                break;
            }
        }

        return new OilConsumption(consumed, fifoCost, $"{calculation}; FIFO: die ältesten Bestände zuerst");
    }

    private static string Num(decimal value) => value.ToString("#,##0.###", System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
}
