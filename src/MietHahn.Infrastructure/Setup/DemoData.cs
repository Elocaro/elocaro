using System.Text;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Admin;
using MietHahn.Application.Documents;
using MietHahn.Application.Finance;
using MietHahn.Application.Setup;
using MietHahn.Domain.Common;
using MietHahn.Domain.Leases;
using MietHahn.Domain.People;
using MietHahn.Domain.Properties;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Setup;

internal sealed class DemoData(
    MietHahnDbContext db,
    IDataImport import,
    ILandlordProfileService landlord,
    IFinanceSettings finance,
    ITenantAccounts accounts,
    IDocumentTemplates templates,
    TimeProvider clock) : IDemoData
{
    public async Task<bool> CanLoadAsync(CancellationToken cancellationToken = default) =>
        !await db.Set<Property>().AnyAsync(cancellationToken)
        && !await db.Set<Person>().AnyAsync(cancellationToken)
        && !await db.Set<Lease>().AnyAsync(cancellationToken);

    public async Task<DataImportResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!await CanLoadAsync(cancellationToken))
        {
            return new DataImportResult(false, DataImportSummary.Empty, ["Beispieldaten lassen sich nur auf einer leeren Installation laden."]);
        }

        var today = GermanTime.Today(clock.GetUtcNow());
        var start = new DateOnly(today.Year, today.Month, 1).AddMonths(-6);
        var rows = new StringBuilder("Objekt;Straße;PLZ;Ort;Einheit;Lage;Wohnfläche;Vorname;Nachname;E-Mail;Mietbeginn;Kaltmiete;Betriebskosten;Heizkosten;Kaution;Saldo\r\n");
        rows.Append(Row(start, "Beispielhaus (Demo);Lindenweg 3;50667;Köln;Wohnung 1;EG links;54,5;Anna;Musterfrau;anna@beispiel.invalid;{0};640;140;75;1920;0"));
        rows.Append(Row(start, "Beispielhaus (Demo);;;;Wohnung 2;EG rechts;61;Ben;Mustermann;ben@beispiel.invalid;{0};720;160;85;2160;0"));
        rows.Append(Row(start, "Beispielhaus (Demo);;;;Wohnung 3;1. OG;78;Clara;Beispiel;clara@beispiel.invalid;{0};890;190;100;2670;0"));
        rows.Append("Beispielhaus (Demo);;;;Wohnung 4;DG;48;;;;;;;;;\r\n");

        var imported = await import.ImportAsync(Encoding.UTF8.GetBytes(rows.ToString()), cancellationToken);
        if (!imported.Succeeded)
        {
            return imported;
        }

        if (await landlord.GetAsync(cancellationToken) is null)
        {
            await landlord.SaveAsync(new LandlordProfileInput { Name = "Beispiel-Vermieter (Demo)", Street = "Musterweg 1", PostalCode = "50667", City = "Köln", Email = "vermieter@beispiel.invalid" }, cancellationToken);
        }

        await templates.InstallDefaultsAsync(cancellationToken);
        var settings = await finance.GetAsync(cancellationToken);
        settings.AccountingStart = start;
        await finance.SaveAsync(settings, cancellationToken);
        await accounts.GenerateChargesAsync(null, cancellationToken);

        // Anna always pays on time, Ben stopped paying after three months, Clara pays only the base rent: the dashboard has something to show.
        var leases = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).Include(v => v.Components).AsSplitQuery().ToListAsync(cancellationToken);
        var names = await db.Set<Person>().AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.LastName, cancellationToken);
        foreach (var lease in leases)
        {
            var tenant = names[lease.Tenant.First().PersonId];
            var rent = lease.MonthlyRentOn(today);
            var months = Enumerable.Range(0, 7).Select(i => start.AddMonths(i)).Where(m => m <= new DateOnly(today.Year, today.Month, 1)).ToList();
            foreach (var month in months)
            {
                var date = month.AddDays(2);
                if (date > today)
                {
                    continue;
                }

                var amount = tenant switch
                {
                    "Musterfrau" => rent,
                    "Mustermann" when months.IndexOf(month) < 3 => rent,
                    "Beispiel" => lease.Components.Where(c => c.Kind == RentComponentType.BaseRent).Sum(c => c.Amount),
                    _ => 0m,
                };
                if (amount > 0)
                {
                    await accounts.RecordPaymentAsync(lease.Id, new PaymentInput { Date = date, Amount = amount, Purpose = "Miete " + month.ToString("MM/yyyy", System.Globalization.CultureInfo.InvariantCulture) }, cancellationToken);
                }
            }
        }

        return imported;
    }

    private static string Row(DateOnly start, string row) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, row, start.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture)) + "\r\n";
}
