using System.Globalization;
using MietHahn.Application.Vertraege;
using MietHahn.Domain.Vertraege;

namespace MietHahn.Web.Components.Pages.Verwaltung.Vertraege;

/// <summary>
/// Formularmodell für das Mietmodell. Der Ausgangsmonat kommt aus <c>&lt;input type="month"&gt;</c> als „2026-01“;
/// <see cref="DateOnly"/> lässt sich daraus nicht direkt binden.
/// </summary>
public sealed class MietmodellFormular
{
    public Mietmodell Modell { get; set; }

    public string? BasisMonat { get; set; }

    public decimal? BasisWert { get; set; }

    public static MietmodellFormular Aus(MietmodellAnsicht ansicht)
    {
        ArgumentNullException.ThrowIfNull(ansicht);
        return new()
        {
            Modell = ansicht.Modell,
            BasisMonat = ansicht.IndexBasisMonat?.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            BasisWert = ansicht.IndexBasisWert,
        };
    }

    public MietmodellEingabe NachEingabe() => new()
    {
        Modell = Modell,
        IndexBasisMonat = DateOnly.TryParseExact(BasisMonat + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var monat) ? monat : null,
        IndexBasisWert = BasisWert,
    };
}
