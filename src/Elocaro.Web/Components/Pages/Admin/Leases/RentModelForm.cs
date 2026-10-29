using System.Globalization;
using Elocaro.Application.Leases;
using Elocaro.Domain.Leases;

namespace Elocaro.Web.Components.Pages.Admin.Leases;

/// <summary>
/// Form model for the rent model. The starting month comes from <c>&lt;input type="month"&gt;</c> as "2026-01";
/// <see cref="DateOnly"/> cannot be bound from it directly.
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
