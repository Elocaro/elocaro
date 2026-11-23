namespace Elocaro.Application.Prospects;

/// <summary>Vacant units with how long they stand empty, what that costs and how far the marketing is.</summary>
public interface IVacancies
{
    /// <summary>Rentable units without a lease today (and without a lease that starts later), the longest vacancy first.</summary>
    Task<IReadOnlyList<VacancyRow>> ListAsync(CancellationToken cancellationToken = default);
}

/// <param name="VacantSince">First day without a lease; <c>null</c> if the unit was never rented.</param>
/// <param name="Days">Days since <paramref name="VacantSince"/>.</param>
/// <param name="LastBaseRent">Base rent of the last lease on its last day.</param>
/// <param name="LostRent">Base rent that was not earned since the vacancy began (by the day, 365.25 / 12 days per month).</param>
/// <param name="OpenProspects">Prospects that are neither accepted nor rejected.</param>
public sealed record VacancyRow(
    Guid RentalUnitId,
    string RentalUnit,
    string Property,
    DateOnly? VacantSince,
    int? Days,
    decimal? LastBaseRent,
    decimal? LostRent,
    int OpenProspects,
    DateOnly? NextViewing,
    string? Hint);
