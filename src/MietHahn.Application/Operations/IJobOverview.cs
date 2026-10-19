namespace MietHahn.Application.Operations;

/// <summary>Status der geplanten Hintergrundjobs für die Admin-Übersicht.</summary>
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
