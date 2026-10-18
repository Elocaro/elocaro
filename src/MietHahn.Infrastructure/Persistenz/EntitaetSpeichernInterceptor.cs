using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MietHahn.Application.Gemeinsam;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Infrastructure.Audit;

namespace MietHahn.Infrastructure.Persistenz;

/// <summary>
/// Setzt beim Speichern die Audit-Felder, wandelt physisches Löschen in logisches Löschen um und schreibt
/// die Änderungen in derselben Transaktion ins Audit-Log (<see cref="AuditEintrag"/>).
/// </summary>
internal sealed class EntitaetSpeichernInterceptor(TimeProvider uhr, IAktuellerBenutzer benutzer) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Vorbereiten(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Vorbereiten(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Vorbereiten(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();
        var jetzt = uhr.GetUtcNow();
        var wer = benutzer.Kennung;

        foreach (var eintrag in context.ChangeTracker.Entries<Entitaet>())
        {
            switch (eintrag.State)
            {
                case EntityState.Added:
                    eintrag.Property(e => e.ErstelltAm).CurrentValue = jetzt;
                    eintrag.Property(e => e.ErstelltVon).CurrentValue = wer;
                    break;

                case EntityState.Modified:
                    eintrag.Property(e => e.GeaendertAm).CurrentValue = jetzt;
                    eintrag.Property(e => e.GeaendertVon).CurrentValue = wer;
                    break;

                case EntityState.Deleted:
                    eintrag.State = EntityState.Modified;
                    eintrag.Property(e => e.Geloescht).CurrentValue = true;
                    eintrag.Property(e => e.GeaendertAm).CurrentValue = jetzt;
                    eintrag.Property(e => e.GeaendertVon).CurrentValue = wer;
                    break;

                case EntityState.Detached:
                case EntityState.Unchanged:
                default:
                    break;
            }
        }

        context.AddRange(AuditErfassung.Erfassen(context, jetzt, wer));
    }
}
