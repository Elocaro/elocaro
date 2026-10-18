using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MietHahn.Application.Audit;
using MietHahn.Application.Gemeinsam;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Infrastructure.Audit;

internal sealed class Zugriffsprotokoll(
    MietHahnDbContext db,
    IAktuellerBenutzer benutzer,
    TimeProvider uhr,
    IOptions<AuditOptionen> optionen) : IZugriffsprotokoll
{
    /// <summary>Innerhalb dieses Zeitraums wird derselbe Zugriff (Benutzer, Datensatz, Zweck) nur einmal protokolliert.</summary>
    public static readonly TimeSpan Zeitfenster = TimeSpan.FromMinutes(30);

    public async Task ProtokollierenAsync(string entitaetTyp, Guid entitaetId, string zweck, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entitaetTyp);
        ArgumentException.ThrowIfNullOrWhiteSpace(zweck);
        if (!optionen.Value.ZugriffeProtokollieren)
        {
            return;
        }

        // Wiederholtes Öffnen (z. B. nach jedem Speichern) nicht jedes Mal protokollieren: ein Eintrag je Zeitfenster genügt als Nachweis.
        var jetzt = uhr.GetUtcNow();
        var seit = jetzt - Zeitfenster;
        var kennung = benutzer.Kennung;
        if (await db.Set<AuditEintrag>().AnyAsync(
                a => a.EntitaetId == entitaetId && a.Aktion == AuditAktion.Gelesen && a.Benutzer == kennung && a.Zweck == zweck && a.Zeitpunkt >= seit,
                cancellationToken))
        {
            return;
        }

        db.Add(new AuditEintrag(jetzt, kennung, entitaetTyp, entitaetId, AuditAktion.Gelesen, "[]", zweck));
        await db.SaveChangesAsync(cancellationToken);
    }
}
