using Elocaro.Domain.Common;

namespace Elocaro.Domain.Heating;

/// <summary>
/// Monthly degree days (Gradtage nach VDI 3807, indoor temperature 20 °C) of a weather station of the Deutscher Wetterdienst.
/// Imported data, hence overwritten by the next import and not audited.
/// </summary>
[NoAudit]
public sealed class DegreeDayValue : Entity
{
    private DegreeDayValue()
    {
        StationId = "";
        StationName = "";
    }

    public DegreeDayValue(string stationId, string stationName, DateOnly month, decimal degreeDays, decimal? longTermMean, decimal? latitude = null, decimal? longitude = null)
    {
        Latitude = latitude;
        Longitude = longitude;
        StationId = stationId;
        StationName = stationName;
        Month = new DateOnly(month.Year, month.Month, 1);
        DegreeDays = degreeDays;
        LongTermMean = longTermMean;
    }

    /// <summary>Station number of the DWD.</summary>
    public string StationId { get; private set; }

    public string StationName { get; private set; }

    /// <summary>Position of the station (degrees); <c>null</c> for values imported before the position was kept.</summary>
    public decimal? Latitude { get; private set; }

    public decimal? Longitude { get; private set; }

    /// <summary>First day of the month.</summary>
    public DateOnly Month { get; private set; }

    public decimal DegreeDays { get; private set; }

    /// <summary>Mean of the same month over the reference years of the DWD (2009 to 2018); <c>null</c> if the DWD gives none.</summary>
    public decimal? LongTermMean { get; private set; }

    public void Update(string stationName, decimal degreeDays, decimal? longTermMean, decimal? latitude = null, decimal? longitude = null)
    {
        StationName = stationName;
        Latitude = latitude ?? Latitude;
        Longitude = longitude ?? Longitude;
        DegreeDays = degreeDays;
        LongTermMean = longTermMean;
    }
}
