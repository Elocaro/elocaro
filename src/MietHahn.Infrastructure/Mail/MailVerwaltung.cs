using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Kommunikation;
using MietHahn.Domain.Kommunikation;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Infrastructure.Mail;

internal sealed class MailVerwaltung(MietHahnDbContext db, TimeProvider uhr) : IMailVerwaltung
{
    public async Task<IReadOnlyList<MailUebersicht>> LetzteAsync(int anzahl, CancellationToken cancellationToken = default) =>
        await db.Set<AusgehendeMail>()
            .AsNoTracking()
            .OrderByDescending(m => m.ErstelltAm)
            .Take(anzahl)
            .Select(m => new MailUebersicht(m.Id, m.An, m.Betreff, m.Status, m.Versuche, m.ErstelltAm, m.GesendetAm, m.NaechsterVersuchAm, m.LetzterFehler))
            .ToListAsync(cancellationToken);

    public async Task ErneutVersuchenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var mail = await db.Set<AusgehendeMail>().SingleOrDefaultAsync(m => m.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"Mail {id} nicht gefunden.");
        mail.ErneutVersuchen(uhr.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }
}
