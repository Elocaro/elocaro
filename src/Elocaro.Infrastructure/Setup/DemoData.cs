using System.Text;
using Elocaro.Application.Admin;
using Elocaro.Application.Documents;
using Elocaro.Application.Finance;
using Elocaro.Application.Metering;
using Elocaro.Application.Setup;
using Elocaro.Application.Tax;
using Elocaro.Domain.Common;
using Elocaro.Domain.Leases;
using Elocaro.Domain.Metering;
using Elocaro.Domain.People;
using Elocaro.Domain.Properties;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Setup;

internal sealed class DemoData(
    ElocaroDbContext db,
    IDataImport import,
    ILandlordProfileService landlord,
    IFinanceSettings finance,
    ITenantAccounts accounts,
    IDocumentTemplates templates,
    ILoanService loans,
    IMeterService meters,
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
        var currentMonth = new DateOnly(today.Year, today.Month, 1);

        // Two full calendar years back, so that statements with a previous year can be released for these leases.
        var start = new DateOnly(today.Year - 2, 1, 1);
        var recent = currentMonth.AddMonths(-6);
        var rows = new StringBuilder("Objekt;Straße;PLZ;Ort;Einheit;Lage;Wohnfläche;Vorname;Nachname;E-Mail;Mietbeginn;Kaltmiete;Betriebskosten;Heizkosten;Kaution;Saldo\r\n");

        // Gas heating per flat with the tenant's own supply contract: no heating prepayment and no statement under the HeizkostenV.
        rows.Append(Row(start, "Beispielhaus (Demo);Lindenweg 3;50667;Köln;Wohnung 1;EG links;54,5;Anna;Musterfrau;anna@beispiel.invalid;{0};640;140;;1920;0"));
        rows.Append(Row(start, "Beispielhaus (Demo);;;;Wohnung 2;EG rechts;61;Ben;Mustermann;ben@beispiel.invalid;{0};720;160;;2160;0"));
        rows.Append(Row(start, "Beispielhaus (Demo);;;;Wohnung 3;1. OG;78;Clara;Beispiel;clara@beispiel.invalid;{0};890;190;;2670;0"));
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

        // Everybody paid in full until half a year ago. Since then Anna pays on time, Ben stopped paying after three months and Clara pays only the base rent: the dashboard has something to show.
        var leases = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).Include(v => v.Components).AsSplitQuery().ToListAsync(cancellationToken);
        var names = await db.Set<Person>().AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.LastName, cancellationToken);
        foreach (var lease in leases)
        {
            var tenant = names[lease.Tenant.First().PersonId];
            var rent = lease.MonthlyRentOn(today);
            var months = Enumerable.Range(0, 36).Select(i => start.AddMonths(i)).Where(m => m <= currentMonth).ToList();
            foreach (var month in months)
            {
                var date = month.AddDays(2);
                if (date > today)
                {
                    continue;
                }

                var amount = tenant switch
                {
                    _ when month < recent => rent,
                    "Musterfrau" => rent,
                    "Mustermann" when month < recent.AddMonths(3) => rent,
                    "Beispiel" => lease.Components.Where(c => c.Kind == RentComponentType.BaseRent).Sum(c => c.Amount),
                    _ => 0m,
                };
                if (amount > 0)
                {
                    await accounts.RecordPaymentAsync(lease.Id, new PaymentInput { Date = date, Amount = amount, Purpose = "Miete " + month.ToString("MM/yyyy", System.Globalization.CultureInfo.InvariantCulture) }, cancellationToken);
                }
            }
        }

        await AddLoanAndMeterAsync(start, today, cancellationToken);
        return imported;
    }

    /// <summary>A loan and a meter with yearly readings, so that the demo shows repayment and consumption over time.</summary>
    private async Task AddLoanAndMeterAsync(DateOnly start, DateOnly today, CancellationToken cancellationToken)
    {
        var property = await db.Set<Property>().AsNoTracking().OrderBy(p => p.CreatedAt).Select(p => p.Id).FirstAsync(cancellationToken);
        var unit = await db.Set<Unit>().AsNoTracking().Where(u => u.PropertyId == property).OrderBy(u => u.Order).Select(u => u.Id).FirstAsync(cancellationToken);
        await loans.CreateAsync(
            new LoanInput
            {
                PropertyId = property,
                Bank = "Beispielbank (Demo)",
                Principal = 180_000m,
                DisbursementDate = start,
                FirstPaymentDate = start.AddMonths(1),
                InterestRatePercent = 3.2m,
                InitialRepaymentPercent = 2m,
                FixedUntil = start.AddYears(10),
            },
            cancellationToken);
        var created = await meters.CreateAsync(
            property,
            new MeterCreation
            {
                Data = new MeterInput { Type = MeterType.ColdWater, Name = "Kaltwasser Wohnung 1", Number = "DEMO-1", AssignmentId = unit },
                InstalledOn = start,
                InitialReading = 100m,
            },
            cancellationToken);
        if (created.Id is { } meter)
        {
            var value = 100m;
            for (var year = start.Year; year < today.Year; year++)
            {
                value += 38m + ((year % 3) * 4m);
                await meters.RecordReadingAsync(meter, new ReadingInput { Date = new DateOnly(year, 12, 31), Value = value, Kind = ReadingType.Reading, Confirmed = true }, null, cancellationToken);
            }
        }
    }

    private static string Row(DateOnly start, string row) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, row, start.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture)) + "\r\n";
}
