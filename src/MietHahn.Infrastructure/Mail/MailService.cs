using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Communication;
using MietHahn.Domain.Communication;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Mail;

internal sealed class MailService(MietHahnDbContext db, TimeProvider clock) : IMailService
{
    public async Task<IReadOnlyList<MailOverview>> LatestAsync(int count, CancellationToken cancellationToken = default) =>
        await db.Set<OutgoingMail>()
            .AsNoTracking()
            .OrderByDescending(m => m.CreatedAt)
            .Take(count)
            .Select(m => new MailOverview(m.Id, m.To, m.Subject, m.Status, m.Attempts, m.CreatedAt, m.SentAt, m.NextAttemptAt, m.LastError))
            .ToListAsync(cancellationToken);

    public async Task RetryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var mail = await db.Set<OutgoingMail>().SingleOrDefaultAsync(m => m.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"Mail {id} nicht gefunden.");
        mail.Retry(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }
}
