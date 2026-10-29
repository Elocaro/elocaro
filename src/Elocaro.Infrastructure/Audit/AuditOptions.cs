namespace Elocaro.Infrastructure.Audit;

/// <summary>Configuration section <c>Audit</c>.</summary>
public sealed class AuditOptions
{
    public const string Section = "Audit";

    /// <summary>Log read access to personal data.</summary>
    public bool LogAccess { get; set; } = true;

    /// <summary>
    /// Retention of changes in years. Default 10 years, modeled on tax retention periods
    /// (statements, receipts). A daily job deletes older entries.
    /// </summary>
    public int RetentionYears { get; set; } = 10;

    /// <summary>Retention of access logs in months (data minimization).</summary>
    public int AccessLogRetentionMonths { get; set; } = 12;
}
