namespace Elocaro.Infrastructure.Heating;

/// <summary>Configuration section <c>DegreeDays</c>: monthly degree days of the Deutscher Wetterdienst.</summary>
public sealed class DegreeDayOptions
{
    public const string Section = "DegreeDays";

    /// <summary>Fetch the monthly degree days from the DWD open data server (switch off for instances without internet access).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Directory with the files <c>gradtage_JJJJMM.csv</c> (VDI 3807, indoor temperature 20 °C).</summary>
    public Uri BaseUrl { get; set; } = new("https://opendata.dwd.de/climate_environment/CDC/derived_germany/techn/monthly/heating_degreedays/hdd_3807/recent/");

    /// <summary>How many years back the first import goes.</summary>
    public int YearsBack { get; set; } = 3;
}
