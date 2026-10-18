using MietHahn.Application.Betrieb;

namespace MietHahn.Infrastructure.Jobs;

/// <summary>Verwendet, wenn der Scheduler abgeschaltet ist (<see cref="JobsOptionen.Aktiv"/> = false).</summary>
internal sealed class LeereJobUebersicht : IJobUebersicht
{
    public Task<IReadOnlyList<JobStatus>> LadenAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<JobStatus>>([]);
}
