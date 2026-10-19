using MietHahn.Domain.Common;

namespace MietHahn.Infrastructure.Jobs;

/// <summary>Protokolleintrag einer Jobausführung (für die Admin-Übersicht).</summary>
[NoAudit]
public sealed class JobRun : Entity
{
    private JobRun()
    {
        JobName = "";
    }

    public JobRun(string jobName, DateTimeOffset startedAt)
    {
        JobName = jobName;
        StartedAt = startedAt;
    }

    public string JobName { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public bool? Succeeded { get; private set; }

    public string? Errors { get; private set; }

    public void End(DateTimeOffset endedAt, Exception? errors)
    {
        EndedAt = endedAt;
        Succeeded = errors is null;
        Errors = errors is null ? null : Truncate(errors.GetBaseException().Message, 2000);
    }

    private static string Truncate(string text, int length) => text.Length > length ? text[..length] : text;
}
