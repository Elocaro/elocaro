using MietHahn.Application.Operations;

namespace MietHahn.Infrastructure.Jobs;

/// <summary>Verwendet, wenn der Scheduler abgeschaltet ist (<see cref="JobsOptions.Active"/> = false).</summary>
internal sealed class EmptyJobOverview : IJobOverview
{
    public Task<IReadOnlyList<JobStatus>> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<JobStatus>>([]);
}
