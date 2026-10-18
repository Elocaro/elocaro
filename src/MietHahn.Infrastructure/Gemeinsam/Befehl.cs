using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Gemeinsam;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Infrastructure.Gemeinsam;

/// <summary>Führt einen Befehl aus und übersetzt erwartbare Fehler in ein <see cref="Ergebnis"/> für die Oberfläche.</summary>
internal static class Befehl
{
    public static Ergebnis NichtGefunden => Ergebnis.Fehlgeschlagen("Der Datensatz wurde nicht gefunden.");

    public static async Task<Ergebnis> AusfuehrenAsync(Func<Task<Guid>> befehl, string ueberlappung = "Für diesen Zeitraum gibt es bereits einen Stand.")
    {
        ArgumentNullException.ThrowIfNull(befehl);
        try
        {
            return Ergebnis.Ok(await befehl());
        }
        catch (NichtGefundenException)
        {
            return NichtGefunden;
        }
        catch (RegelverletzungException fehler)
        {
            return Ergebnis.Fehlgeschlagen(fehler.Message);
        }
        catch (Exception fehler) when (Datenbankfehler.IstUeberlappung(fehler))
        {
            return Ergebnis.Fehlgeschlagen(ueberlappung);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Ergebnis.Fehlgeschlagen("Der Datensatz wurde zwischenzeitlich geändert. Bitte die Seite neu laden.");
        }
    }
}

/// <summary>Innerhalb von <see cref="Befehl.AusfuehrenAsync"/>: der Datensatz existiert nicht.</summary>
internal sealed class NichtGefundenException : Exception
{
    public NichtGefundenException()
    {
    }

    public NichtGefundenException(string message)
        : base(message)
    {
    }

    public NichtGefundenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
