using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Domain.Vertraege;

/// <summary>Wie Betriebs- und Heizkosten mit dem Mieter abgerechnet werden.</summary>
public enum Abrechnungsart
{
    /// <summary>Monatliche Vorauszahlungen, jährliche Nebenkostenabrechnung.</summary>
    Vorauszahlung = 0,

    /// <summary>Betriebskostenpauschale ohne Abrechnung (z. B. WG-Zimmer); intern Ist/Pauschale-Vergleich.</summary>
    Pauschale = 1,

    /// <summary>Nebenkosten sind in der Miete enthalten (Inklusivmiete).</summary>
    Inklusiv = 2,
}

public enum Zahlungsart
{
    Ueberweisung = 0,
    Lastschrift = 1,
}

public enum Parteirolle
{
    Mieter = 0,
    Buerge = 1,
}

public enum Kuendigender
{
    Mieter = 0,
    Vermieter = 1,
    Einvernehmlich = 2,
}

public enum Vertragsstatus
{
    /// <summary>Beginnt in der Zukunft.</summary>
    Kuenftig = 0,
    Aktiv = 1,

    /// <summary>Gekündigt oder befristet, Ende steht fest und liegt in der Zukunft.</summary>
    Gekuendigt = 2,
    Beendet = 3,
}

/// <summary>
/// Mietvertrag über ein Mietobjekt (Einheit ohne Zimmer oder WG-Zimmer) mit einem oder mehreren Mietern
/// (gesamtschuldnerisch). Miete, Vorauszahlungen und Personenanzahl sind zeitabhängig und werden nie überschrieben.
/// </summary>
public sealed class Mietvertrag : Entitaet
{
    private Mietvertrag()
    {
    }

    /// <param name="mietobjektId">Id der Einheit bzw. des Zimmers.</param>
    /// <param name="einheitId">Einheit des Mietobjekts (bei Zimmern die WG-Wohnung).</param>
    public Mietvertrag(Guid mietobjektId, Guid einheitId, Guid? zimmerId, DateOnly beginn, DateOnly? befristetBis = null)
    {
        if (befristetBis < beginn)
        {
            throw new RegelverletzungException("Das Ende der Befristung liegt vor dem Mietbeginn.");
        }

        MietobjektId = mietobjektId;
        EinheitId = einheitId;
        ZimmerId = zimmerId;
        BefristetBis = befristetBis;
        Laufzeit = new Zeitraum(beginn, befristetBis);
    }

    /// <summary>Einheit- oder Zimmer-Id; je Mietobjekt darf zu jedem Tag nur ein Vertrag laufen.</summary>
    public Guid MietobjektId { get; private set; }

    public Guid EinheitId { get; private set; }

    public Guid? ZimmerId { get; private set; }

    /// <summary>Tatsächliche Laufzeit: Beginn bis Befristung bzw. Ende nach Kündigung (<c>null</c> = unbefristet).</summary>
    public Zeitraum Laufzeit { get; private set; }

    /// <summary>Zeitmietvertrag (§ 575 BGB) bis zu diesem Tag.</summary>
    public DateOnly? BefristetBis { get; private set; }

    public DateOnly Beginn => Laufzeit.Von;

    public DateOnly? Ende => Laufzeit.Bis;

    public DateOnly? KuendigungEingegangenAm { get; private set; }

    public Kuendigender? KuendigungDurch { get; private set; }

    /// <summary>Kündigungsfrist für den Mieter in Monaten (gesetzlich 3, § 573c Abs. 1 BGB).</summary>
    public int KuendigungsfristMonate { get; set; } = 3;

    public Abrechnungsart Abrechnungsart { get; set; }

    /// <summary>Heizung/Warmwasser trotz Pauschale verbrauchsabhängig abrechnen (HeizkostenV hat Vorrang, siehe docs/fachlichkeit.md).</summary>
    public bool HeizkostenVerbrauchsabhaengig { get; set; }

    /// <summary>Fällig am n-ten Werktag des Monats (gesetzlich: 3., § 556b BGB).</summary>
    public int FaelligAmWerktag { get; set; } = 3;

    public Zahlungsart Zahlungsart { get; set; }

    /// <summary>Kaution: nur Betrag und Notiz (keine Kautionskontoverwaltung).</summary>
    public decimal? Kaution { get; set; }

    public string? KautionNotiz { get; set; }

    public string? Notizen { get; set; }

    public Mietmodell Mietmodell { get; private set; }

    /// <summary>Indexmiete: Monat des Ausgangsindex (erster Tag des Monats).</summary>
    public DateOnly? IndexBasisMonat { get; private set; }

    /// <summary>Indexmiete: Indexstand des Ausgangsmonats (VPI, Basis 2020 = 100).</summary>
    public decimal? IndexBasisWert { get; private set; }

    public List<Mietstaffel> Staffeln { get; private set; } = [];

    public List<Vertragspartei> Parteien { get; private set; } = [];

    public List<Mietbestandteil> Bestandteile { get; private set; } = [];

    public List<Personenstand> Personen { get; private set; } = [];

    public IEnumerable<Vertragspartei> Mieter => Parteien.Where(p => p.Rolle == Parteirolle.Mieter).OrderBy(p => p.Reihenfolge);

    /// <summary>Nur Verträge mit Vorauszahlungen bekommen eine Nebenkostenabrechnung.</summary>
    public bool ErhaeltNebenkostenabrechnung => Abrechnungsart == Abrechnungsart.Vorauszahlung;

    /// <summary>Heizkosten werden verbrauchsabhängig abgerechnet (auch bei Pauschale, wenn so konfiguriert).</summary>
    public bool ErhaeltHeizkostenabrechnung => Abrechnungsart == Abrechnungsart.Vorauszahlung || HeizkostenVerbrauchsabhaengig;

    public Vertragsstatus StatusAm(DateOnly tag) =>
        tag < Beginn ? Vertragsstatus.Kuenftig
        : Ende is { } ende && tag > ende ? Vertragsstatus.Beendet
        : Ende is not null ? Vertragsstatus.Gekuendigt
        : Vertragsstatus.Aktiv;

    public void ParteiHinzufuegen(Guid personId, Parteirolle rolle)
    {
        if (Parteien.Any(p => p.PersonId == personId))
        {
            throw new RegelverletzungException("Die Person ist bereits Vertragspartei.");
        }

        Parteien.Add(new Vertragspartei(Id, personId, rolle, Parteien.Count == 0 ? 1 : Parteien.Max(p => p.Reihenfolge) + 1));
    }

    public void ParteiEntfernen(Guid personId)
    {
        var partei = Parteien.SingleOrDefault(p => p.PersonId == personId) ?? throw new RegelverletzungException("Die Person ist keine Vertragspartei.");
        if (partei.Rolle == Parteirolle.Mieter && Mieter.Count() == 1)
        {
            throw new RegelverletzungException("Ein Vertrag braucht mindestens einen Mieter.");
        }

        Parteien.Remove(partei);
    }

    /// <summary>Setzt einen Bestandteil (z. B. neue Kaltmiete) ab Stichtag; der bisherige Betrag bleibt bis zum Vortag.</summary>
    public Mietbestandteil BestandteilSetzen(Bestandteilart art, DateOnly ab, decimal betrag, string? bezeichnung = null)
    {
        PruefeImVertrag(ab);
        if (betrag < 0)
        {
            throw new RegelverletzungException("Beträge dürfen nicht negativ sein.");
        }

        var reihe = Bestandteile.Where(b => b.Art == art).ToList();
        var neu = Zeitreihe.Fortschreiben(reihe, ab, g => new Mietbestandteil(Id, art, g, decimal.Round(betrag, 2), bezeichnung));
        Abgleichen(Bestandteile, b => b.Art == art, reihe);
        return neu;
    }

    /// <summary>Beendet einen Bestandteil (z. B. Stellplatz gekündigt) zum angegebenen Tag.</summary>
    public void BestandteilBeenden(Bestandteilart art, DateOnly letzterTag)
    {
        var aktuell = Zeitreihe.Am(Bestandteile.Where(b => b.Art == art), letzterTag)
            ?? throw new RegelverletzungException("Zu diesem Tag gibt es den Bestandteil nicht.");
        aktuell.GueltigkeitBeenden(letzterTag);
    }

    public Personenstand PersonenSetzen(DateOnly ab, int anzahl)
    {
        PruefeImVertrag(ab);
        if (anzahl is < 1 or > 20)
        {
            throw new RegelverletzungException("Bitte eine Personenanzahl zwischen 1 und 20 angeben.");
        }

        return Zeitreihe.Fortschreiben(Personen, ab, g => new Personenstand(Id, g, anzahl));
    }

    public IReadOnlyList<Mietbestandteil> BestandteileAm(DateOnly tag) =>
        Bestandteile.Where(b => b.Gueltigkeit.Enthaelt(tag)).OrderBy(b => b.Art).ToList();

    /// <summary>Monatliche Gesamtmiete (Kaltmiete + Vorauszahlungen/Pauschale + Zuschläge) an einem Tag.</summary>
    public decimal MonatsmieteAm(DateOnly tag) => BestandteileAm(tag).Sum(b => b.Betrag);

    public decimal? BetragAm(Bestandteilart art, DateOnly tag) => Zeitreihe.Am(Bestandteile.Where(b => b.Art == art), tag)?.Betrag;

    public int? PersonenAm(DateOnly tag) => Zeitreihe.Am(Personen, tag)?.Anzahl;

    /// <summary>Erfasst eine Kündigung; das Vertragsende ist der letzte Tag des Mietverhältnisses.</summary>
    public void Kuendigen(DateOnly eingegangenAm, Kuendigender durch, DateOnly zum)
    {
        if (zum < Beginn)
        {
            throw new RegelverletzungException("Das Vertragsende liegt vor dem Mietbeginn.");
        }

        if (eingegangenAm > zum)
        {
            throw new RegelverletzungException("Die Kündigung muss vor dem Vertragsende eingehen.");
        }

        KuendigungEingegangenAm = eingegangenAm;
        KuendigungDurch = durch;
        EndeSetzen(zum);
    }

    /// <summary>
    /// Hebt eine erfasste Kündigung auf (z. B. zurückgenommen): wieder unbefristet bzw. bis zur Befristung.
    /// Bestandteile und Personenanzahl, die zum bisherigen Ende ausliefen, laufen weiter.
    /// </summary>
    public void KuendigungAufheben()
    {
        if (KuendigungEingegangenAm is null)
        {
            throw new RegelverletzungException("Es ist keine Kündigung erfasst.");
        }

        var bisherigesEnde = Ende;
        KuendigungEingegangenAm = null;
        KuendigungDurch = null;
        Laufzeit = new Zeitraum(Beginn, BefristetBis);
        foreach (var bestandteil in Bestandteile.Where(b => b.Gueltigkeit.Bis == bisherigesEnde))
        {
            bestandteil.BisSetzen(BefristetBis);
        }

        foreach (var stand in Personen.Where(p => p.Gueltigkeit.Bis == bisherigesEnde))
        {
            stand.BisSetzen(BefristetBis);
        }
    }

    /// <summary>
    /// Kündigungstermin bei Kündigung durch den Mieter (§ 573c Abs. 1 BGB): Zugang spätestens am 3. Werktag eines Monats
    /// → Ende mit Ablauf des übernächsten Monats (bei 3 Monaten Frist). Abweichende Fristen verschieben entsprechend.
    /// </summary>
    public static DateOnly KuendigungsterminFuerMieter(DateOnly eingang, int fristMonate = 3)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fristMonate, 1);
        var dritterWerktag = Werktage.NterWerktag(eingang.Year, eingang.Month, 3);
        var monate = eingang <= dritterWerktag ? fristMonate - 1 : fristMonate;
        var monat = new DateOnly(eingang.Year, eingang.Month, 1).AddMonths(monate);
        return new DateOnly(monat.Year, monat.Month, DateTime.DaysInMonth(monat.Year, monat.Month));
    }

    /// <summary>
    /// Kündigungsfrist des Vermieters (§ 573c Abs. 1 Satz 2 BGB): 3 Monate, nach 5 Jahren Mietdauer 6, nach 8 Jahren 9 Monate.
    /// </summary>
    public int KuendigungsfristVermieterMonate(DateOnly eingang)
    {
        var jahre = eingang.Year - Beginn.Year - (eingang < Beginn.AddYears(eingang.Year - Beginn.Year) ? 1 : 0);
        return jahre >= 8 ? 9 : jahre >= 5 ? 6 : 3;
    }

    /// <summary>Wechselt das Mietmodell. Beim Wechsel weg von der Staffelmiete müssen offene Staffeln entfernt sein.</summary>
    public void MietmodellSetzen(Mietmodell modell, DateOnly? indexBasisMonat = null, decimal? indexBasisWert = null)
    {
        if (modell != Mietmodell.Staffel && Staffeln.Any(s => s.AngewendetAm is null))
        {
            throw new RegelverletzungException("Es gibt noch offene Staffeln. Bitte zuerst entfernen.");
        }

        if (modell == Mietmodell.Index && (indexBasisMonat is null || indexBasisWert is not > 0))
        {
            throw new RegelverletzungException("Für die Indexmiete bitte Monat und Wert des Ausgangsindex angeben.");
        }

        Mietmodell = modell;
        IndexBasisMonat = modell == Mietmodell.Index ? new DateOnly(indexBasisMonat!.Value.Year, indexBasisMonat.Value.Month, 1) : null;
        IndexBasisWert = modell == Mietmodell.Index ? decimal.Round(indexBasisWert!.Value, 1) : null;
    }

    /// <summary>
    /// Fügt eine Staffel hinzu (§ 557a BGB): Die Miete muss jeweils mindestens ein Jahr unverändert bleiben, der Betrag
    /// ist als Geldbetrag ausgewiesen.
    /// </summary>
    public Mietstaffel StaffelHinzufuegen(DateOnly ab, decimal kaltmiete)
    {
        if (Mietmodell != Mietmodell.Staffel)
        {
            throw new RegelverletzungException("Staffeln gibt es nur bei einer Staffelmiete.");
        }

        PruefeImVertrag(ab);
        if (kaltmiete <= 0)
        {
            throw new RegelverletzungException("Bitte die neue Kaltmiete angeben.");
        }

        var termine = Staffeln.Select(s => s.Ab).Append(Beginn).Order().ToList();
        if (termine.Any(t => t != ab && Math.Abs(MonateZwischen(t, ab)) < 12))
        {
            throw new RegelverletzungException("Zwischen zwei Staffeln (und ab Mietbeginn) muss die Miete mindestens ein Jahr unverändert bleiben.");
        }

        if (Staffeln.Any(s => s.Ab == ab))
        {
            throw new RegelverletzungException("Zu diesem Termin gibt es bereits eine Staffel.");
        }

        var staffel = new Mietstaffel(Id, ab, decimal.Round(kaltmiete, 2));
        Staffeln.Add(staffel);
        return staffel;
    }

    public void StaffelEntfernen(Guid staffelId)
    {
        var staffel = Staffeln.SingleOrDefault(s => s.Id == staffelId) ?? throw new RegelverletzungException("Die Staffel wurde nicht gefunden.");
        if (staffel.AngewendetAm is not null)
        {
            throw new RegelverletzungException("Bereits übernommene Staffeln bleiben erhalten; die Miete bitte über die Bestandteile korrigieren.");
        }

        Staffeln.Remove(staffel);
    }

    /// <summary>Übernimmt fällige Staffeln als Kaltmiete (täglicher Job). Liefert die übernommenen Staffeln.</summary>
    public IReadOnlyList<Mietstaffel> FaelligeStaffelnAnwenden(DateOnly heute, DateTimeOffset zeitpunkt)
    {
        var angewendet = new List<Mietstaffel>();
        foreach (var staffel in Staffeln.Where(s => s.AngewendetAm is null && s.Ab <= heute && Laufzeit.Enthaelt(s.Ab)).OrderBy(s => s.Ab))
        {
            BestandteilSetzen(Bestandteilart.Kaltmiete, staffel.Ab, staffel.Kaltmiete);
            staffel.Angewendet(zeitpunkt);
            angewendet.Add(staffel);
        }

        return angewendet;
    }

    /// <summary>Übernimmt eine Indexanpassung: neue Kaltmiete ab Wirksamkeit und neuer Ausgangsindex.</summary>
    public decimal IndexAnpassen(DateOnly wirksamAb, DateOnly vpiMonat, decimal vpiWert)
    {
        if (Mietmodell != Mietmodell.Index || IndexBasisWert is not { } basis)
        {
            throw new RegelverletzungException("Der Vertrag ist keine Indexmiete.");
        }

        var letzteAenderung = LetzteKaltmieteAenderung() ?? Beginn;
        if (wirksamAb < Indexmiete.FruehestesWirksamwerden(letzteAenderung))
        {
            throw new RegelverletzungException(
                $"Die Miete muss mindestens ein Jahr unverändert bleiben: frühestens ab {Indexmiete.FruehestesWirksamwerden(letzteAenderung):dd.MM.yyyy}.");
        }

        var bisher = BetragAm(Bestandteilart.Kaltmiete, wirksamAb) ?? throw new RegelverletzungException("Zum Stichtag ist keine Kaltmiete hinterlegt.");
        var neu = Indexmiete.NeueMiete(bisher, basis, vpiWert);
        BestandteilSetzen(Bestandteilart.Kaltmiete, wirksamAb, neu);
        IndexBasisMonat = new DateOnly(vpiMonat.Year, vpiMonat.Month, 1);
        IndexBasisWert = decimal.Round(vpiWert, 1);
        return neu;
    }

    /// <summary>Beginn der aktuell gültigen Kaltmiete, falls sie nach Mietbeginn geändert wurde.</summary>
    public DateOnly? LetzteKaltmieteAenderung(DateOnly? stichtag = null)
    {
        var tag = stichtag ?? DateOnly.MaxValue;
        return Bestandteile.Where(b => b.Art == Bestandteilart.Kaltmiete && b.Gueltigkeit.Von > Beginn && b.Gueltigkeit.Von <= tag)
            .Select(b => (DateOnly?)b.Gueltigkeit.Von).Max();
    }

    private static int MonateZwischen(DateOnly von, DateOnly bis) =>
        ((bis.Year - von.Year) * 12) + bis.Month - von.Month - (bis.Day < von.Day ? 1 : 0);

    private void EndeSetzen(DateOnly ende)
    {
        if (Bestandteile.Any(b => b.Gueltigkeit.Von > ende) || Personen.Any(p => p.Gueltigkeit.Von > ende))
        {
            throw new RegelverletzungException("Nach dem neuen Vertragsende gibt es noch Mietbestandteile. Bitte zuerst diese korrigieren.");
        }

        Laufzeit = new Zeitraum(Beginn, ende);
        foreach (var stand in Bestandteile.Concat<IZeitabhaengig>(Personen).Where(s => s.Gueltigkeit.Bis is null || s.Gueltigkeit.Bis > ende))
        {
            stand.GueltigkeitBeenden(ende);
        }
    }

    private void PruefeImVertrag(DateOnly tag)
    {
        if (!Laufzeit.Enthaelt(tag))
        {
            throw new RegelverletzungException($"Der Stichtag {tag:dd.MM.yyyy} liegt außerhalb der Vertragslaufzeit ({Laufzeit}).");
        }
    }

    /// <summary>Überträgt Hinzufügen/Entfernen aus der gefilterten Teilliste auf die Gesamtliste.</summary>
    private static void Abgleichen<T>(List<T> gesamt, Func<T, bool> filter, List<T> teil)
    {
        gesamt.RemoveAll(e => filter(e) && !teil.Contains(e));
        gesamt.AddRange(teil.Where(e => !gesamt.Contains(e)));
    }
}

public sealed class Vertragspartei : Entitaet
{
    private Vertragspartei()
    {
    }

    internal Vertragspartei(Guid mietvertragId, Guid personId, Parteirolle rolle, int reihenfolge)
    {
        MietvertragId = mietvertragId;
        PersonId = personId;
        Rolle = rolle;
        Reihenfolge = reihenfolge;
    }

    public Guid MietvertragId { get; private set; }

    public Guid PersonId { get; private set; }

    public Parteirolle Rolle { get; private set; }

    public int Reihenfolge { get; private set; }
}

public enum Bestandteilart
{
    Kaltmiete = 0,
    BetriebskostenVorauszahlung = 1,
    HeizkostenVorauszahlung = 2,
    Betriebskostenpauschale = 3,
    Stellplatz = 4,
    Moeblierungszuschlag = 5,
    Sonstiges = 9,
}

/// <summary>Ein monatlicher Betrag (Kaltmiete, Vorauszahlung, Pauschale, Zuschlag) in einem Zeitraum.</summary>
public sealed class Mietbestandteil : Entitaet, IZeitabhaengig
{
    private Mietbestandteil()
    {
    }

    internal Mietbestandteil(Guid mietvertragId, Bestandteilart art, Zeitraum gueltigkeit, decimal betrag, string? bezeichnung)
    {
        MietvertragId = mietvertragId;
        Art = art;
        Gueltigkeit = gueltigkeit;
        Betrag = betrag;
        Bezeichnung = string.IsNullOrWhiteSpace(bezeichnung) ? null : bezeichnung.Trim();
    }

    public Guid MietvertragId { get; private set; }

    public Bestandteilart Art { get; private set; }

    public Zeitraum Gueltigkeit { get; private set; }

    /// <summary>Monatsbetrag in Euro.</summary>
    public decimal Betrag { get; private set; }

    /// <summary>Für „Sonstiges“, z. B. „Gartennutzung“.</summary>
    public string? Bezeichnung { get; private set; }

    public void GueltigkeitBeenden(DateOnly letzterTag) => Gueltigkeit = Gueltigkeit.BeendenZum(letzterTag);

    internal void BisSetzen(DateOnly? bis) => Gueltigkeit = new Zeitraum(Gueltigkeit.Von, bis);
}

/// <summary>Anzahl der im Mietobjekt lebenden Personen (Umlage nach Personen).</summary>
public sealed class Personenstand : Entitaet, IZeitabhaengig
{
    private Personenstand()
    {
    }

    internal Personenstand(Guid mietvertragId, Zeitraum gueltigkeit, int anzahl)
    {
        MietvertragId = mietvertragId;
        Gueltigkeit = gueltigkeit;
        Anzahl = anzahl;
    }

    public Guid MietvertragId { get; private set; }

    public Zeitraum Gueltigkeit { get; private set; }

    public int Anzahl { get; private set; }

    public void GueltigkeitBeenden(DateOnly letzterTag) => Gueltigkeit = Gueltigkeit.BeendenZum(letzterTag);

    internal void BisSetzen(DateOnly? bis) => Gueltigkeit = new Zeitraum(Gueltigkeit.Von, bis);
}
