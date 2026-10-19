namespace MietHahn.Application.Operations;

/// <summary>Status of the scheduled background jobs for the admin overview.</summary>
public interface IJobOverview
{
    Task<IReadOnlyList<JobStatus>> GetAsync(CancellationToken cancellationToken = default);
}

public sealed record JobStatus(
    string Name,
    string? Description,
    DateTimeOffset? NextRun,
    DateTimeOffset? LastRunAt,
    bool? LastRunSucceeded,
    string? LastError);
