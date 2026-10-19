namespace MietHahn.Infrastructure.Jobs;

/// <summary>Configuration section <c>Jobs</c>.</summary>
public sealed class JobsOptions
{
    public const string Section = "Jobs";

    /// <summary>Start the scheduler (can be disabled in tests or for web-only instances).</summary>
    public bool Active { get; set; } = true;

    /// <summary>How long entries in the job run log are retained.</summary>
    public int LogRetentionDays { get; set; } = 30;
}
