namespace Elocaro.Infrastructure.Privacy;

/// <summary>Configuration section <c>Privacy</c>.</summary>
public sealed class PrivacyOptions
{
    public const string Section = "Privacy";

    /// <summary>
    /// Retention after the end of the last relationship (e.g. lease end) before anonymization is proposed.
    /// 10 years corresponds to the longest tax retention period (§ 147 AO).
    /// </summary>
    public int RetentionYears { get; set; } = 10;

    /// <summary>Persons without any relationship (e.g. prospects) are proposed after this time without changes.</summary>
    public int MonthsWithoutRelation { get; set; } = 12;
}
