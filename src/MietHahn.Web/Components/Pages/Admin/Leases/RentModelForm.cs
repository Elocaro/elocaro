using System.Globalization;
using MietHahn.Application.Leases;
using MietHahn.Domain.Leases;

namespace MietHahn.Web.Components.Pages.Admin.Leases;

/// <summary>
/// Formularmodell für das Mietmodell. Der Ausgangsmonat kommt aus <c>&lt;input type="month"&gt;</c> als „2026-01“;
/// <see cref="DateOnly"/> lässt sich daraus nicht direkt binden.
/// </summary>
public sealed class RentModelForm
{
    public RentModel Model { get; set; }

    public string? BaseMonth { get; set; }

    public decimal? BaseValue { get; set; }

    public static RentModelForm From(RentModelView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new()
        {
            Model = view.Model,
            BaseMonth = view.IndexBaseMonth?.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            BaseValue = view.IndexBaseValue,
        };
    }

    public RentModelInput AfterInput() => new()
    {
        Model = Model,
        IndexBaseMonth = DateOnly.TryParseExact(BaseMonth + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month) ? month : null,
        IndexBaseValue = BaseValue,
    };
}
