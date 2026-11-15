using Elocaro.Application.Admin;
using Elocaro.Domain.Leases;

namespace Elocaro.Application.Leases;

/// <summary>Which switchable option (<see cref="AppOption"/>) a rent model or billing type of a lease belongs to. Lives with the leases so the module catalog does not know them.</summary>
public static class LeaseOptions
{
    public static AppOption Of(RentModel model) => model switch
    {
        RentModel.Graduated => AppOption.RentModelGraduated,
        RentModel.Index => AppOption.RentModelIndex,
        _ => throw new ArgumentOutOfRangeException(nameof(model), "Die Vergleichsmiete ist immer verfügbar."),
    };

    public static AppOption Of(BillingType type) => type switch
    {
        BillingType.Prepayment => AppOption.BillingPrepayment,
        BillingType.FlatRate => AppOption.BillingFlatRate,
        _ => AppOption.BillingInclusive,
    };
}
