using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Elocaro.Application.Deadlines;
using Elocaro.Application.Emergency;
using Elocaro.Application.Files;
using Elocaro.Application.Finance;
using Elocaro.Application.Operations;
using Elocaro.Application.Todos;
using Elocaro.Domain.Admin;
using Elocaro.Domain.Bank;
using Elocaro.Domain.Common;
using Elocaro.Domain.Deposits;
using Elocaro.Domain.Documents;
using Elocaro.Domain.Leases;
using Elocaro.Domain.People;
using Elocaro.Domain.Properties;
using Elocaro.Domain.Receipts;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Elocaro.Infrastructure.Emergency;

/// <summary>A document that is carried inside the emergency folder.</summary>
internal sealed record EmbeddedFile(string Name, string ContentType, byte[] Content);

/// <summary>The readable pages of the emergency folder and the documents to embed; everything is built in memory.</summary>
internal sealed record EmergencyFolderPages(string Html, IReadOnlyList<EmbeddedFile> Files, bool TooLarge);

/// <summary>
/// Collects the data and renders it as one HTML document. Every value from the database is HTML-encoded here when it is
/// written (<see cref="E"/>), so names or free texts cannot inject markup; the document is also shown in a sandboxed frame.
/// Secrets (passwords, tokens, keys, bank access data) are never read.
/// </summary>
internal sealed class EmergencyFolderContent(
    ElocaroDbContext db,
    IBackupStatus backup,
    ITenantAccounts accounts,
    IDeadlines deadlines,
    IEnumerable<ITodoSource> todoSources,
    IFileStore files,
    IOptions<ApplicationOptions> application,
    TimeProvider clock)
{
    public const string PageLink = "/verwaltung/system/notfallmappe";

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Encodes markup characters (&lt; &gt; &amp; quotes) and leaves umlauts readable.</summary>
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    public async Task<EmergencyFolderPages> BuildAsync(EmergencyFolder notes, bool includeLeaseDocuments, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(user);
        var now = clock.GetUtcNow();
        var today = GermanTime.Today(now);

        var landlords = await db.Set<LandlordProfile>().AsNoTracking().OrderBy(l => l.CreatedAt).ToListAsync(cancellationToken);
        var properties = await db.Set<Property>().AsNoTracking().Include(p => p.Units).ThenInclude(u => u.Areas).Include(p => p.Units).ThenInclude(u => u.Room)
            .OrderBy(p => p.Name).ToListAsync(cancellationToken);
        var allLeases = await db.Set<Lease>().AsNoTracking().Include(l => l.Parties).Include(l => l.Components).ToListAsync(cancellationToken);
        var leases = allLeases.Where(l => l.StatusAt(today) != LeaseStatus.Ended).ToList();
        var personIds = leases.SelectMany(l => l.Parties).Select(p => p.PersonId).Distinct().ToList();
        var persons = await db.Set<Person>().AsNoTracking().Include(p => p.Addresses).Where(p => personIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        var leaseIds = leases.Select(l => l.Id).ToList();
        var depositEntries = (await db.Set<DepositEntry>().AsNoTracking().Where(e => leaseIds.Contains(e.LeaseId)).ToListAsync(cancellationToken)).ToLookup(e => e.LeaseId);
        var openItems = await accounts.OpenItemsAsync(cancellationToken);
        var upcoming = await deadlines.ListAsync(user, today.AddMonths(12), cancellationToken);
        var tasks = await TasksAsync(user, cancellationToken);
        var bankAccounts = await db.Set<BankAccount>().AsNoTracking().OrderBy(b => b.Name).ToListAsync(cancellationToken);
        var providers = await ProvidersAsync(today, cancellationToken);
        var backupState = await backup.QueryAsync(cancellationToken);

        var units = properties.SelectMany(p => p.Units.Select(u => (Property: p, Unit: u))).ToDictionary(x => x.Unit.Id);
        var tenantNames = leases.ToDictionary(l => l.Id, l => string.Join(", ", l.Tenant.Select(t => persons.TryGetValue(t.PersonId, out var person) ? person.DisplayName : "unbekannt")));

        var html = new StringBuilder();
        Begin(html, now);
        FirstSteps(html);
        Notes(html, notes);
        Landlords(html, landlords);
        Properties(html, properties, leases, today);
        Leases(html, leases, persons, depositEntries, units, today);
        OpenItems(html, openItems, tenantNames);
        Deadlines(html, upcoming, tasks, today);
        BankAccounts(html, bankAccounts);
        Providers(html, providers);
        Technology(html, backupState);
        End(html);

        var embedded = new List<EmbeddedFile>();
        var size = Encoding.UTF8.GetByteCount(html.ToString());
        if (size > EmergencyFolderLimits.MaxContentBytes)
        {
            return new EmergencyFolderPages("", [], TooLarge: true);
        }

        if (includeLeaseDocuments)
        {
            var budget = EmergencyFolderLimits.MaxContentBytes - size;
            var contractFiles = await ContractFilesAsync(leaseIds, tenantNames, units, leases, cancellationToken);
            foreach (var (name, fileId) in contractFiles)
            {
                await using var opened = await files.OpenAsync(fileId, cancellationToken);
                if (opened is null)
                {
                    continue;
                }

                if (opened.StoredFile.Size > budget)
                {
                    return new EmergencyFolderPages("", [], TooLarge: true);
                }

                var buffer = new MemoryStream(checked((int)opened.StoredFile.Size));
                await opened.Content.CopyToAsync(buffer, cancellationToken);
                budget -= buffer.Length;
                if (budget < 0)
                {
                    return new EmergencyFolderPages("", [], TooLarge: true);
                }

                // The stream was created with the exact size, so its buffer is the content (no second copy).
                var content = buffer.Length == buffer.GetBuffer().Length ? buffer.GetBuffer() : buffer.ToArray();
                embedded.Add(new EmbeddedFile(name + Path.GetExtension(opened.StoredFile.Name), opened.StoredFile.MimeType, content));
            }
        }

        return new EmergencyFolderPages(html.ToString(), embedded, TooLarge: false);
    }

    private async Task<List<TodoItem>> TasksAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var result = new List<TodoItem>();
        foreach (var source in todoSources)
        {
            result.AddRange((await source.TodosAsync(user, cancellationToken)).Where(t => t.Link != PageLink));
        }

        return result.OrderByDescending(t => t.Urgency).ThenBy(t => t.Due ?? DateOnly.MaxValue).ThenBy(t => t.Title, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>Suppliers of the receipts of the last two years with the cost types they billed (name and cost type only).</summary>
    private async Task<List<Provider>> ProvidersAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var since = today.AddYears(-2);
        var receipts = await db.Set<Receipt>().AsNoTracking().Include(r => r.Items).Where(r => r.InvoiceDate >= since).ToListAsync(cancellationToken);
        var supplierIds = receipts.Where(r => r.SupplierId is not null).Select(r => r.SupplierId!.Value).Distinct().ToList();
        var suppliers = await db.Set<Person>().AsNoTracking().Where(p => supplierIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);

        var byName = new Dictionary<string, Provider>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var receipt in receipts)
        {
            Person? person = receipt.SupplierId is { } id && suppliers.TryGetValue(id, out var found) ? found : null;
            var name = person?.DisplayName ?? receipt.SupplierName;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            name = name.Trim();
            if (!byName.TryGetValue(name, out var provider))
            {
                byName[name] = provider = new Provider(name);
            }

            if (person?.ContactType == ContactType.Insurance)
            {
                provider.Insurance = true;
            }

            foreach (var item in receipt.Items)
            {
                provider.CostTypes.Add(CostTypes.Find(item.CostType)?.Name ?? item.CostType);
                provider.Insurance |= item.CostType == "betrkv-13";
            }
        }

        return byName.Values.OrderBy(p => p.Name, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>The signed contracts of the current leases with a file name for the embedded copy.</summary>
    private async Task<List<(string Name, Guid FileId)>> ContractFilesAsync(
        List<Guid> leaseIds, Dictionary<Guid, string> tenantNames, Dictionary<Guid, (Property Property, Unit Unit)> units, List<Lease> leases, CancellationToken cancellationToken)
    {
        var contracts = await db.Set<LeaseContract>().AsNoTracking()
            .Where(c => leaseIds.Contains(c.LeaseId) && c.Status == ContractStatus.Concluded && c.SignedDocumentId != null)
            .OrderBy(c => c.SignedOn).ToListAsync(cancellationToken);
        var documentIds = contracts.Select(c => c.SignedDocumentId!.Value).ToList();
        var documents = await db.Set<Document>().AsNoTracking().Where(d => documentIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.FileId, cancellationToken);

        var result = new List<(string, Guid)>();
        foreach (var contract in contracts)
        {
            if (!documents.TryGetValue(contract.SignedDocumentId!.Value, out var fileId))
            {
                continue;
            }

            var lease = leases.First(l => l.Id == contract.LeaseId);
            var place = units.TryGetValue(lease.UnitId, out var unit) ? $"{unit.Property.Name} {unit.Unit.Name}" : "Einheit";
            result.Add((FileName($"Mietvertrag {place} {tenantNames[lease.Id]}"), fileId));
        }

        return result;
    }

    private static string FileName(string text)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(text.Select(z => invalid.Contains(z) || char.IsControl(z) ? '_' : z).ToArray()).Trim();
        return cleaned.Length > 120 ? cleaned[..120] : cleaned;
    }

    // ---- Rendering ----------------------------------------------------------------------------------------------

    private static void Begin(StringBuilder html, DateTimeOffset now) =>
        html.Append("""
            <!DOCTYPE html>
            <html lang="de"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <style>
            :root { color-scheme: light dark; --bg: #fdfcf8; --text: #1c1b1f; --muted: #5d5b62; --line: #cac4d0; --card: #f4f1ea; --warn: #8a5a00; }
            @media (prefers-color-scheme: dark) { :root { --bg: #1c1b1f; --text: #e6e1e5; --muted: #b0acb4; --line: #49454f; --card: #26242a; --warn: #f0c36d; } }
            * { box-sizing: border-box; }
            body { margin: 0; padding: 0.5rem 0; background: var(--bg); color: var(--text); font: 16px/1.5 system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; overflow-wrap: anywhere; }
            h1 { font-size: 1.6rem; margin: 0 0 0.25rem; }
            h2 { font-size: 1.25rem; margin: 2rem 0 0.5rem; padding-top: 0.5rem; border-top: 2px solid var(--line); break-after: avoid; }
            h3 { font-size: 1.05rem; margin: 1rem 0 0.25rem; break-after: avoid; }
            p, ul, ol { margin: 0 0 0.75rem; }
            ul, ol { padding-left: 1.4rem; }
            li { margin-bottom: 0.25rem; }
            .muted { color: var(--muted); }
            .note { padding: 0.75rem 1rem; background: var(--card); border-left: 4px solid var(--warn); border-radius: 4px; }
            .text { white-space: pre-wrap; }
            .card { padding: 0.75rem 1rem; margin-bottom: 0.75rem; background: var(--card); border: 1px solid var(--line); border-radius: 8px; break-inside: avoid; }
            .card h3 { margin-top: 0; }
            dl { display: grid; grid-template-columns: max-content 1fr; gap: 0.15rem 1rem; margin: 0.5rem 0 0; }
            dt { color: var(--muted); }
            dd { margin: 0; }
            .scroll { overflow-x: auto; }
            table { border-collapse: collapse; width: 100%; margin-bottom: 0.75rem; }
            th, td { padding: 0.35rem 0.6rem; text-align: left; vertical-align: top; border-bottom: 1px solid var(--line); }
            td.number, th.number { text-align: right; white-space: nowrap; }
            tr { break-inside: avoid; }
            @media (max-width: 480px) { dl { grid-template-columns: 1fr; } dt { margin-top: 0.4rem; } }
            </style></head><body>
            <h1>Notfallmappe Vermietung</h1>
            """)
            .Append("<p class=\"muted\">Stand: ").Append(E(Stamp(now))).Append(" · erzeugt mit Elocaro. Die Angaben sind eine Momentaufnahme und werden nicht automatisch aktualisiert.</p>\n");

    private static void End(StringBuilder html) => html.Append("</body></html>\n");

    private static void FirstSteps(StringBuilder html) =>
        html.Append("""
            <h2>Erste Schritte: die ersten 30 Tage</h2>
            <p class="note">Diese Liste ist eine allgemeine Orientierung und keine Rechtsberatung. Sie nennt bewusst keine Fristen und Gesetze. Wie es rechtlich weitergeht (Erbe, Vollmachten, Nachfolge in den Mietverhältnissen), klären Sie mit einer Rechtsanwältin, einem Notar oder einer Steuerberaterin.</p>
            <ol>
            <li>Verschaffen Sie sich Überblick, bevor Sie handeln. Lesen Sie diese Mappe einmal ganz durch und drucken Sie sie bei Bedarf aus.</li>
            <li>Sichern Sie die Unterlagen: Wo sie liegen, steht bei den Hinweisen unten. Ändern oder vernichten Sie nichts.</li>
            <li>Holen Sie fachlichen Rat: Steuerberatung, Rechtsberatung und gegebenenfalls eine Hausverwaltung. Ansprechpartner stehen bei den Hinweisen und bei den Dienstleistern.</li>
            <li>Informieren Sie die Mieterinnen und Mieter kurz und sachlich, wer künftig Ansprechpartner ist und wohin Fragen gehen. Prüfen Sie, ob Mietzahlungen weiter auf den bekannten Konten eingehen.</li>
            <li>Klären Sie den Zugriff auf die Bankkonten (siehe Konten) und lassen Sie laufende Zahlungen für Versicherungen, Strom, Wasser und Heizung nicht ins Leere laufen.</li>
            <li>Melden Sie sich bei den Versicherungen und Dienstleistern, damit Verträge nicht unbemerkt enden und Schäden gemeldet werden können.</li>
            <li>Gehen Sie die offenen Posten, Fristen und Aufgaben weiter unten durch und besprechen Sie sie mit Ihrer Beratung.</li>
            <li>Sichern Sie die Daten von Elocaro (siehe Technik) und entscheiden Sie in Ruhe, ob Sie die Anwendung weiterbetreiben.</li>
            </ol>

            """);

    private static void Notes(StringBuilder html, EmergencyFolder notes)
    {
        html.Append("<h2>Hinweise des Vermieters</h2>\n");
        Free(html, "Wichtige Kontakte", notes.ImportantContacts);
        Free(html, "Wo liegen die Unterlagen?", notes.DocumentLocation);
        Free(html, "Weitere Hinweise", notes.FurtherNotes);
    }

    private static void Free(StringBuilder html, string heading, string? text)
    {
        html.Append("<h3>").Append(E(heading)).Append("</h3>\n");
        html.Append(string.IsNullOrWhiteSpace(text) ? "<p class=\"muted\">Keine Angabe.</p>\n" : $"<p class=\"text\">{E(text.Trim())}</p>\n");
    }

    private static void Landlords(StringBuilder html, List<LandlordProfile> landlords)
    {
        html.Append("<h2>Vermieter</h2>\n");
        if (landlords.Count == 0)
        {
            html.Append("<p class=\"muted\">Kein Vermieterprofil angelegt.</p>\n");
            return;
        }

        foreach (var landlord in landlords)
        {
            html.Append("<div class=\"card\"><h3>").Append(E(landlord.Name)).Append("</h3>\n<dl>");
            Row(html, "Anschrift", string.Join(", ", new[] { landlord.AddressLine2, landlord.Street, $"{landlord.PostalCode} {landlord.City}" }.Where(t => !string.IsNullOrWhiteSpace(t))));
            Row(html, "Telefon", landlord.Phone);
            Row(html, "E-Mail", landlord.Email);
            html.Append("</dl></div>\n");
        }
    }

    private static void Properties(StringBuilder html, List<Property> properties, List<Lease> leases, DateOnly today)
    {
        html.Append("<h2>Objekte und Einheiten</h2>\n");
        if (properties.Count == 0)
        {
            html.Append("<p class=\"muted\">Keine Objekte angelegt.</p>\n");
            return;
        }

        foreach (var property in properties)
        {
            html.Append("<h3>").Append(E(property.Name)).Append("</h3>\n<p>").Append(E(property.Address)).Append("</p>\n");
            html.Append("<div class=\"scroll\"><table><thead><tr><th>Einheit</th><th>Art</th><th class=\"number\">Wohnfläche</th><th>Aktuell</th></tr></thead><tbody>\n");
            foreach (var unit in property.Units.OrderBy(u => u.Order).ThenBy(u => u.Name, StringComparer.CurrentCulture))
            {
                var area = unit.AreaOn(today) ?? unit.Areas.OrderByDescending(a => a.Validity.From).FirstOrDefault();
                var current = leases.Where(l => l.UnitId == unit.Id && l.StatusAt(today) is LeaseStatus.Active or LeaseStatus.Terminated).ToList();
                var place = unit.Location is null ? unit.Name : $"{unit.Name} ({unit.Location})";
                html.Append("<tr><td>").Append(E(place)).Append("</td><td>").Append(E(UnitKind(unit.Type)))
                    .Append("</td><td class=\"number\">").Append(area is null ? "–" : E(area.LivingArea.ToString("N2", German) + " m²"))
                    .Append("</td><td>").Append(current.Count > 0 ? "vermietet" : leases.Any(l => l.UnitId == unit.Id) ? "vermietet ab " + E(Day(leases.First(l => l.UnitId == unit.Id).Start)) : "nicht vermietet")
                    .Append("</td></tr>\n");
            }

            html.Append("</tbody></table></div>\n");
        }
    }

    private static void Leases(
        StringBuilder html,
        List<Lease> leases,
        Dictionary<Guid, Person> persons,
        ILookup<Guid, DepositEntry> depositEntries,
        Dictionary<Guid, (Property Property, Unit Unit)> units,
        DateOnly today)
    {
        html.Append("<h2>Mietverhältnisse</h2>\n");
        if (leases.Count == 0)
        {
            html.Append("<p class=\"muted\">Keine laufenden oder künftigen Mietverträge.</p>\n");
            return;
        }

        html.Append("<p class=\"muted\">Laufende und künftige Mietverträge mit Kontaktdaten, Miete und Kaution.</p>\n");
        foreach (var lease in leases.OrderBy(l => units.TryGetValue(l.UnitId, out var u) ? u.Property.Name : "", StringComparer.CurrentCulture).ThenBy(l => l.Start))
        {
            var place = units.TryGetValue(lease.UnitId, out var unit) ? $"{unit.Property.Name}, {unit.Unit.Name}" : "Einheit";
            var status = lease.StatusAt(today);
            var rooms = unit.Unit is null ? "" : string.Join(", ", unit.Unit.Room.Where(r => lease.RoomIds.Contains(r.Id)).Select(r => r.Name));
            html.Append("<div class=\"card\"><h3>").Append(E(rooms.Length > 0 ? $"{place} ({rooms})" : place)).Append("</h3>\n");
            html.Append("<ul>");
            foreach (var party in lease.Parties.OrderBy(p => p.Role).ThenBy(p => p.Order))
            {
                var role = party.Role == PartyRole.Guarantor ? "Bürge" : "Mieter";
                if (!persons.TryGetValue(party.PersonId, out var person))
                {
                    html.Append("<li>").Append(role).Append(": unbekannt</li>");
                    continue;
                }

                var address = person.AddressAt(today)?.SingleLine;
                var contact = string.Join(" · ", new[] { person.Phone, person.Mobile, person.Email, address }.Where(t => !string.IsNullOrWhiteSpace(t)));
                html.Append("<li><strong>").Append(E(person.DisplayName)).Append("</strong> (").Append(role).Append(')');
                if (contact.Length > 0)
                {
                    html.Append("<br>").Append(E(contact));
                }

                html.Append("</li>");
            }

            html.Append("</ul>\n<dl>");
            Row(html, "Laufzeit", Term(lease, status));
            var components = lease.ComponentsAt(status == LeaseStatus.Future ? lease.Start : today);
            foreach (var component in components)
            {
                Row(html, ComponentName(component), Money(component.Amount) + " monatlich");
            }

            if (components.Count > 0)
            {
                Row(html, "Gesamtmiete", Money(components.Sum(c => c.Amount)) + " monatlich");
            }

            Row(html, "Zahlung", (lease.PaymentMethod == PaymentMethod.DirectDebit ? "Lastschrift" : "Überweisung") + $", fällig am {lease.DueOnWorkingDay}. Werktag");
            var deposit = DepositAccount.Of(lease.Deposit, depositEntries[lease.Id]);
            if (lease.Deposit is not null || deposit.Balance != 0)
            {
                Row(html, "Kaution", $"vereinbart {Money(lease.Deposit ?? 0)}, eingegangen {Money(deposit.Balance)}" + (deposit.Outstanding > 0 ? $", noch offen {Money(deposit.Outstanding)}" : ""));
            }

            Row(html, "Art der Kaution", lease.DepositNote);
            html.Append("</dl></div>\n");
        }
    }

    private static void OpenItems(StringBuilder html, IReadOnlyList<OpenItem> items, Dictionary<Guid, string> tenantNames)
    {
        html.Append("<h2>Offene Posten</h2>\n");
        if (items.Count == 0)
        {
            html.Append("<p class=\"muted\">Keine offenen Posten.</p>\n");
            return;
        }

        html.Append("<div class=\"scroll\"><table><thead><tr><th>Mieter</th><th>Einheit</th><th>Posten</th><th>Fällig</th><th class=\"number\">Offen</th></tr></thead><tbody>\n");
        foreach (var item in items.OrderBy(i => i.Tenant, StringComparer.CurrentCulture).ThenBy(i => i.Due))
        {
            html.Append("<tr><td>").Append(E(tenantNames.GetValueOrDefault(item.LeaseId, item.Tenant))).Append("</td><td>").Append(E(item.RentalUnit))
                .Append("</td><td>").Append(E(item.Text)).Append("</td><td>").Append(E(Day(item.Due)))
                .Append("</td><td class=\"number\">").Append(E(Money(item.Open))).Append("</td></tr>\n");
        }

        html.Append("<tr><td colspan=\"4\"><strong>Summe</strong></td><td class=\"number\"><strong>").Append(E(Money(items.Sum(i => i.Open)))).Append("</strong></td></tr>\n");
        html.Append("</tbody></table></div>\n");
    }

    private static void Deadlines(StringBuilder html, IReadOnlyList<DeadlineEntry> deadlines, List<TodoItem> tasks, DateOnly today)
    {
        html.Append("<h2>Fristen und Aufgaben</h2>\n<h3>Fristen der nächsten zwölf Monate</h3>\n");
        if (deadlines.Count == 0)
        {
            html.Append("<p class=\"muted\">Keine Fristen.</p>\n");
        }
        else
        {
            html.Append("<div class=\"scroll\"><table><thead><tr><th>Datum</th><th>Frist</th><th>Bereich</th></tr></thead><tbody>\n");
            foreach (var entry in deadlines.OrderBy(d => d.Due))
            {
                html.Append("<tr><td>").Append(E(Day(entry.Due))).Append(entry.Due < today ? " (überfällig)" : "").Append("</td><td>").Append(E(entry.Title));
                if (!string.IsNullOrWhiteSpace(entry.Details))
                {
                    html.Append("<br><span class=\"muted\">").Append(E(entry.Details)).Append("</span>");
                }

                html.Append("</td><td>").Append(E(entry.Area)).Append("</td></tr>\n");
            }

            html.Append("</tbody></table></div>\n");
        }

        html.Append("<h3>Offene Aufgaben</h3>\n");
        if (tasks.Count == 0)
        {
            html.Append("<p class=\"muted\">Keine offenen Aufgaben.</p>\n");
            return;
        }

        html.Append("<ul>");
        foreach (var task in tasks)
        {
            html.Append("<li><strong>").Append(E(task.Title)).Append("</strong>");
            if (task.Due is { } due)
            {
                html.Append(" (bis ").Append(E(Day(due))).Append(')');
            }

            if (!string.IsNullOrWhiteSpace(task.Details))
            {
                html.Append("<br><span class=\"muted\">").Append(E(task.Details)).Append("</span>");
            }

            html.Append("</li>");
        }

        html.Append("</ul>\n");
    }

    private static void BankAccounts(StringBuilder html, List<BankAccount> accounts)
    {
        html.Append("<h2>Konten</h2>\n<p class=\"muted\">Nur Kontoverbindungen. Zugangsdaten, PINs und TANs gehören nicht in diese Mappe.</p>\n");
        if (accounts.Count == 0)
        {
            html.Append("<p class=\"muted\">Keine Konten angelegt.</p>\n");
            return;
        }

        html.Append("<div class=\"scroll\"><table><thead><tr><th>Konto</th><th>Art</th><th>IBAN</th><th>Bank</th></tr></thead><tbody>\n");
        foreach (var account in accounts)
        {
            var bank = string.Join(", ", new[] { account.Bank, account.Bic }.Where(t => !string.IsNullOrWhiteSpace(t)));
            html.Append("<tr><td>").Append(E(account.Name)).Append("</td><td>").Append(E(AccountKind(account.Type))).Append("</td><td>").Append(E(account.Iban))
                .Append("</td><td>").Append(E(bank)).Append("</td></tr>\n");
        }

        html.Append("</tbody></table></div>\n");
    }

    private static void Providers(StringBuilder html, List<Provider> providers)
    {
        html.Append("<h2>Dienstleister und Versicherungen</h2>\n<p class=\"muted\">Aus den Belegen der letzten zwei Jahre: Name und Kostenart.</p>\n");
        Providers(html, "Versicherungen", providers.Where(p => p.Insurance).ToList());
        Providers(html, "Dienstleister und Lieferanten", providers.Where(p => !p.Insurance).ToList());
    }

    private static void Providers(StringBuilder html, string heading, List<Provider> providers)
    {
        html.Append("<h3>").Append(E(heading)).Append("</h3>\n");
        if (providers.Count == 0)
        {
            html.Append("<p class=\"muted\">Keine Belege gefunden.</p>\n");
            return;
        }

        html.Append("<div class=\"scroll\"><table><thead><tr><th>Name</th><th>Kostenart</th></tr></thead><tbody>\n");
        foreach (var provider in providers)
        {
            html.Append("<tr><td>").Append(E(provider.Name)).Append("</td><td>").Append(E(string.Join("; ", provider.CostTypes.Order(StringComparer.CurrentCulture)))).Append("</td></tr>\n");
        }

        html.Append("</tbody></table></div>\n");
    }

    private void Technology(StringBuilder html, BackupSnapshot backupState)
    {
        var version = typeof(EmergencyFolderContent).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        html.Append("<h2>Technik</h2>\n<dl>");
        Row(html, "Adresse von Elocaro", application.Value.BaseUrl.ToString());
        Row(html, "Version", version?.Split('+')[0]);
        Row(html, "Datensicherung", backupState.Description + (backupState.Timestamp is { } at ? $" (Stand {Stamp(at)})" : ""));
        html.Append("</dl>\n");
        html.Append("""
            <p>Elocaro läuft auf einem eigenen Server. Wenn die Adresse oben nicht mehr erreichbar ist, haben Sie zwei Wege:</p>
            <ul>
            <li>Die Datensicherung (Backup) auf dem Server oder an dem Ort, an den sie kopiert wird, lässt sich auf einem neuen Server wiederherstellen. Die Anleitung steht in der Dokumentation von Elocaro (<em>docs/betrieb.md</em>, Abschnitt Wiederherstellung).</li>
            <li>Ein Vollexport (ZIP mit allen Daten als JSON und CSV) ist ohne Elocaro lesbar. Er lässt sich in Elocaro unter System, Datensicherung, erzeugen.</li>
            </ul>
            <p class="note">Zugangsdaten zu Server, Domain, Konten oder zum Passwortmanager stehen bewusst nicht in dieser Mappe. Bewahren Sie diese Datei und das Passwort an getrennten Orten auf.</p>

            """);
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------

    /// <summary>The only way a value from the database or a free text reaches the markup.</summary>
    private static string E(string? text) => Encoder.Encode(text ?? "");

    private static void Row(StringBuilder html, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            html.Append("<dt>").Append(E(label)).Append("</dt><dd>").Append(E(value)).Append("</dd>");
        }
    }

    private static string Money(decimal amount) => amount.ToString("N2", German) + " €";

    private static string Day(DateOnly day) => day.ToString("dd.MM.yyyy", German);

    private static string Stamp(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, GermanTime.Germany).ToString("dd.MM.yyyy HH:mm", German) + " Uhr";

    private static string Term(Lease lease, LeaseStatus status)
    {
        var text = status == LeaseStatus.Future ? $"beginnt am {Day(lease.Start)}" : $"seit {Day(lease.Start)}";
        if (lease.End is { } end)
        {
            text += lease.TerminationReceivedOn is null ? $", befristet bis {Day(end)}" : $", gekündigt zum {Day(end)}";
        }
        else
        {
            text += ", unbefristet";
        }

        return text;
    }

    private static string ComponentName(RentComponent component) => component.Kind switch
    {
        RentComponentType.BaseRent => "Kaltmiete",
        RentComponentType.OperatingCostPrepayment => "Betriebskosten-Vorauszahlung",
        RentComponentType.HeatingCostPrepayment => "Heizkosten-Vorauszahlung",
        RentComponentType.OperatingCostFlatRate => "Betriebskostenpauschale",
        RentComponentType.ParkingSpace => "Stellplatz",
        RentComponentType.FurnishingSurcharge => "Möblierungszuschlag",
        _ => component.Name ?? "Sonstiges",
    };

    private static string UnitKind(UnitType type) => type switch
    {
        UnitType.Apartment => "Wohnung",
        UnitType.Commercial => "Gewerbe",
        UnitType.ParkingSpace => "Stellplatz",
        _ => "Sonstiges",
    };

    private static string AccountKind(AccountType type) => type switch
    {
        AccountType.RentAccount => "Mietkonto",
        AccountType.Loan => "Darlehen",
        AccountType.Reserve => "Rücklage",
        _ => "Sonstiges",
    };

    private sealed class Provider(string name)
    {
        public string Name { get; } = name;

        public bool Insurance { get; set; }

        public SortedSet<string> CostTypes { get; } = new(StringComparer.CurrentCulture);
    }
}
