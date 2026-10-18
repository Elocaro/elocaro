using System.Globalization;

namespace MietHahn.Domain.Gemeinsam;

/// <summary>
/// Gültigkeitszeitraum mit inklusiven Grenzen. <see cref="Bis"/> = <c>null</c> bedeutet „unbefristet“.
/// Grundlage aller zeitabhängigen Werte (Miete, Flächen, Personen, Verträge) und der taggenauen Abrechnung.
/// </summary>
public readonly record struct Zeitraum
{
    public Zeitraum(DateOnly von, DateOnly? bis = null)
    {
        if (bis < von)
        {
            throw new ArgumentException($"Das Ende ({bis:dd.MM.yyyy}) liegt vor dem Beginn ({von:dd.MM.yyyy}).", nameof(bis));
        }

        Von = von;
        Bis = bis;
    }

    public DateOnly Von { get; init; }

    public DateOnly? Bis { get; init; }

    public bool IstUnbefristet => Bis is null;

    /// <summary>Anzahl Kalendertage inklusive beider Grenzen (nur für befristete Zeiträume).</summary>
    public int Tage => Bis is { } bis
        ? bis.DayNumber - Von.DayNumber + 1
        : throw new InvalidOperationException("Ein unbefristeter Zeitraum hat keine Tagesanzahl.");

    public static Zeitraum Kalenderjahr(int jahr) => new(new DateOnly(jahr, 1, 1), new DateOnly(jahr, 12, 31));

    public static Zeitraum Monat(int jahr, int monat) =>
        new(new DateOnly(jahr, monat, 1), new DateOnly(jahr, monat, DateTime.DaysInMonth(jahr, monat)));

    public bool Enthaelt(DateOnly tag) => tag >= Von && (Bis is null || tag <= Bis);

    public bool UeberschneidetSich(Zeitraum anderer) =>
        (Bis is null || anderer.Von <= Bis) && (anderer.Bis is null || Von <= anderer.Bis);

    /// <summary>Gemeinsamer Teil beider Zeiträume oder <c>null</c>, wenn sie sich nicht überschneiden.</summary>
    public Zeitraum? Schnitt(Zeitraum anderer)
    {
        if (!UeberschneidetSich(anderer))
        {
            return null;
        }

        var von = Von > anderer.Von ? Von : anderer.Von;
        DateOnly? bis = (Bis, anderer.Bis) switch
        {
            (null, null) => null,
            (null, var b) => b,
            (var a, null) => a,
            (var a, var b) => a < b ? a : b,
        };
        return new Zeitraum(von, bis);
    }

    /// <summary>Tage, die in den Begrenzungszeitraum fallen (z. B. Mietdauer innerhalb eines Abrechnungsjahres).</summary>
    public int TageIn(Zeitraum begrenzung)
    {
        if (begrenzung.IstUnbefristet)
        {
            throw new ArgumentException("Die Begrenzung muss befristet sein.", nameof(begrenzung));
        }

        return Schnitt(begrenzung)?.Tage ?? 0;
    }

    /// <summary>Beendet den Zeitraum zum angegebenen Tag (inklusive).</summary>
    public Zeitraum BeendenZum(DateOnly letzterTag) => new(Von, letzterTag);

    /// <summary>Prüft, dass sich keine zwei der Zeiträume überschneiden.</summary>
    public static bool SindUeberlappungsfrei(IEnumerable<Zeitraum> zeitraeume)
    {
        ArgumentNullException.ThrowIfNull(zeitraeume);
        var sortiert = zeitraeume.OrderBy(z => z.Von).ToList();
        for (var i = 1; i < sortiert.Count; i++)
        {
            if (sortiert[i - 1].UeberschneidetSich(sortiert[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override string ToString() =>
        Bis is { } bis
            ? string.Create(CultureInfo.InvariantCulture, $"{Von:dd.MM.yyyy} – {bis:dd.MM.yyyy}")
            : string.Create(CultureInfo.InvariantCulture, $"ab {Von:dd.MM.yyyy}");
}
