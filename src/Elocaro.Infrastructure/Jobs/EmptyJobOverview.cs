using Elocaro.Application.Operations;

namespace Elocaro.Infrastructure.Jobs;

/// <summary>Used when the scheduler is disabled (<see cref="JobsOptions.Active"/> = false).</summary>
internal sealed class EmptyJobOverview : IJobOverview
{
    public Task<IReadOnlyList<JobStatus>> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<JobStatus>>([]);
}
