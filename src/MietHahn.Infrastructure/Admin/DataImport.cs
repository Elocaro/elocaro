using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Admin;
using MietHahn.Application.Finance;
using MietHahn.Application.Leases;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Domain.Common;
using MietHahn.Domain.Deposits;
using MietHahn.Domain.Finance;
using MietHahn.Domain.Leases;
using MietHahn.Domain.People;
using MietHahn.Domain.Properties;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Admin;

/// <summary>
/// Spreadsheet import (see <see cref="IDataImport"/>). The file is read and planned against the existing data first; the import
/// then uses the normal services (so validation and the change log apply) inside one transaction: everything or nothing.
/// </summary>
internal sealed class DataImport(
    MietHahnDbContext db,
    IPropertyService properties,
    IPersonService people,
    ILeaseService leases,
    ITenantAccounts accounts,
    IDeposits deposits,
    TimeProvider clock) : IDataImport
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public byte[] TemplateCsv() => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(ImportSheet.Template)];

    public async Task<DataImportPreview> PreviewAsync(byte[] content, CancellationToken cancellationToken = default)
    {
        var (plans, fileErrors) = await PlanAsync(content, cancellationToken);
        return new DataImportPreview(
            plans.Select(p => new DataImportRow(p.Line.Line, p.Line.Property, p.Line.Unit, p.Tenant, p.Actions, p.Errors)).ToList(),
            Summary(plans), fileErrors);
    }

    public async Task<DataImportResult> ImportAsync(byte[] content, CancellationToken cancellationToken = default)
    {
        var (plans, fileErrors) = await PlanAsync(content, cancellationToken);
        var summary = Summary(plans);
        if (fileErrors.Count > 0 || plans.Count == 0 || plans.Any(p => p.Errors.Count > 0))
        {
            var errors = fileErrors.Concat(plans.SelectMany(p => p.Errors.Select(e => $"Zeile {p.Line.Line}: {e}"))).ToList();
            return new DataImportResult(false, DataImportSummary.Empty, errors.Count > 0 ? errors : ["Die Datei enthält nichts zu importieren."]);
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            db.ChangeTracker.Clear();
            try
            {
                await ApplyAsync(plans, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new DataImportResult(true, summary, []);
            }
            catch (ImportFailedException ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                return new DataImportResult(false, DataImportSummary.Empty, [ex.Message]);
            }
        });
    }

    // ---- planning -----------------------------------------------------------------------------------------------

    private async Task<(IReadOnlyList<RowPlan> Plans, IReadOnlyList<string> FileErrors)> PlanAsync(byte[] content, CancellationToken cancellationToken)
    {
        var (lines, fileErrors) = ImportSheet.Read(content);
        if (fileErrors.Count > 0)
        {
            return ([], fileErrors);
        }

        var existingProperties = (await db.Set<Property>().AsNoTracking().Include(p => p.Units).ThenInclude(u => u.Areas).AsSplitQuery().ToListAsync(cancellationToken))
            .GroupBy(p => Key(p.Name)).ToDictionary(g => g.Key, g => g.First());
        var existingPeople = await db.Set<Person>().AsNoTracking().Where(p => p.AnonymizedAt == null)
            .Select(p => new PersonRef(p.Id, p.FirstName, p.LastName, p.Email)).ToListAsync(cancellationToken);
        var existingLeases = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).AsSplitQuery().ToListAsync(cancellationToken);

        var plans = new List<RowPlan>();
        var propertiesInFile = new HashSet<string>();
        var unitsInFile = new HashSet<string>();
        var peopleInFile = new HashSet<string>();
        var leasesInFile = new Dictionary<string, List<DateOnly>>();
        foreach (var line in lines)
        {
            var errors = new List<string>(line.Errors);
            var actions = new List<string>();
            var propertyKey = Key(line.Property);
            var unitKey = propertyKey + "|" + Key(line.Unit);

            // property
            existingProperties.TryGetValue(propertyKey, out var property);
            var newProperty = property is null && propertiesInFile.Add(propertyKey);
            if (property is not null)
            {
                actions.Add("Objekt vorhanden");
            }
            else if (!newProperty)
            {
                actions.Add("Objekt wie oben");
            }
            else
            {
                actions.Add("Objekt neu");
                if (string.IsNullOrWhiteSpace(line.Street) || string.IsNullOrWhiteSpace(line.PostalCode) || string.IsNullOrWhiteSpace(line.City))
                {
                    errors.Add($"Das Objekt „{line.Property}“ ist neu: Straße, PLZ und Ort müssen in dieser Zeile stehen.");
                }
                else if (line.PostalCode.Length != 5 || !line.PostalCode.All(char.IsDigit))
                {
                    errors.Add($"Die Postleitzahl „{line.PostalCode}“ hat nicht 5 Ziffern.");
                }
            }

            // unit and area
            var existingUnit = property?.Units.FirstOrDefault(u => string.Equals(u.Name, line.Unit.Trim(), StringComparison.OrdinalIgnoreCase));
            var newUnit = existingUnit is null && unitsInFile.Add(unitKey);
            var setArea = false;
            if (existingUnit is null && !newUnit)
            {
                actions.Add("Einheit wie oben");
            }
            else if (existingUnit is null)
            {
                actions.Add("Einheit neu");
                setArea = line.Area is not null;
            }
            else
            {
                actions.Add("Einheit vorhanden");
                if (line.Area is not null && existingUnit.Areas.Count == 0)
                {
                    setArea = true;
                }
                else if (line.Area is not null)
                {
                    actions.Add("Fläche bleibt unverändert");
                }
            }

            if (setArea)
            {
                actions.Add($"Fläche {line.Area!.Value.ToString("0.##", German)} m²");
            }

            // tenant, lease and opening balance
            string? tenant = null;
            var newPerson = false;
            var newLease = false;
            Guid? existingPerson = null;
            if (line.HasTenant)
            {
                tenant = $"{line.FirstName} {line.LastName}".Trim();
                existingPerson = existingPeople.FirstOrDefault(p => Matches(p, line))?.Id;
                var personKey = string.IsNullOrWhiteSpace(line.Email) ? Key(tenant) : Key(line.Email);
                if (existingPerson is not null)
                {
                    actions.Add("Mieter vorhanden");
                }
                else if (peopleInFile.Add(personKey))
                {
                    actions.Add("Mieter neu");
                    newPerson = true;
                }
                else
                {
                    actions.Add("Mieter wie oben");
                }

                if (line.Start is { } start && errors.Count == 0)
                {
                    var onUnit = existingUnit is null ? [] : existingLeases.Where(v => v.UnitId == existingUnit.Id).ToList();
                    var sameLease = existingPerson is { } pid && onUnit.Any(v => v.Start == start && v.Parties.Any(p => p.PersonId == pid));
                    if (sameLease)
                    {
                        actions.Add("Vertrag vorhanden");
                    }
                    else if (onUnit.FirstOrDefault(v => v.End is null || v.End >= start) is { } clash)
                    {
                        errors.Add($"Für die Einheit gibt es schon einen Vertrag ab {clash.Start.ToString("dd.MM.yyyy", German)}, der sich mit dem Mietbeginn {start.ToString("dd.MM.yyyy", German)} überschneidet.");
                    }
                    else if (leasesInFile.TryGetValue(unitKey, out var starts) && starts.Count > 0)
                    {
                        errors.Add("Die Einheit kommt mit einem weiteren laufenden Vertrag in der Datei vor; je Einheit ist nur ein laufender Vertrag möglich.");
                    }
                    else
                    {
                        newLease = true;
                        actions.Add($"Vertrag neu ab {start.ToString("dd.MM.yyyy", German)}");
                        if (line.Model == RentModel.Index)
                        {
                            actions.Add($"Indexmiete (Basis {line.IndexMonth?.ToString("MM'/'yyyy", CultureInfo.InvariantCulture)} = {line.IndexValue?.ToString("0.0", German)})");
                        }
                        else if (line.Model == RentModel.Graduated)
                        {
                            actions.Add($"Staffelmiete mit {line.Steps.Count} Staffel(n)");
                        }

                        if (line.FixedUntil is { } until)
                        {
                            actions.Add($"befristet bis {until.ToString("dd.MM.yyyy", German)}");
                        }
                        leasesInFile[unitKey] = [start];
                    }
                }
            }

            if (newLease && DepositReceivedOf(line) is { } received and > 0)
            {
                actions.Add($"Kaution {received.ToString("0.00", German)} € als eingegangen");
            }

            var balance = newLease && line.Balance is { } value && value != 0;
            if (balance)
            {
                actions.Add(line.Balance > 0 ? $"Saldovortrag {line.Balance:0.00} € offen" : $"Guthaben {-line.Balance:0.00} €");
            }

            plans.Add(new RowPlan(line, property?.Id, existingUnit?.Id, existingPerson, newProperty, newUnit, setArea, newPerson, newLease, balance, tenant, actions, errors));
        }

        return (plans, []);
    }

    private static DataImportSummary Summary(IReadOnlyList<RowPlan> plans) => new(
        plans.Count(p => p.NewProperty), plans.Count(p => p.NewUnit), plans.Count(p => p.NewPerson), plans.Count(p => p.NewLease), plans.Count(p => p.Balance),
        plans.Count(p => p.Actions.Any(a => a.EndsWith("vorhanden", StringComparison.Ordinal))));

    private static bool Matches(PersonRef person, ImportLine line) =>
        !string.IsNullOrWhiteSpace(line.Email) && !string.IsNullOrWhiteSpace(person.Email)
            ? string.Equals(person.Email, line.Email, StringComparison.OrdinalIgnoreCase)
            : string.Equals(person.LastName, line.LastName, StringComparison.OrdinalIgnoreCase)
              && string.Equals(person.FirstName ?? "", line.FirstName ?? "", StringComparison.OrdinalIgnoreCase);

    /// <summary>Deposit paid in: the column "Kaution eingegangen", or without it the whole agreed deposit (a stock taken over has usually been paid).</summary>
    private static decimal? DepositReceivedOf(ImportLine line) => line.DepositReceived ?? line.Deposit;

    private static string Key(string text) => text.Trim().ToUpperInvariant();

    // ---- applying -----------------------------------------------------------------------------------------------

    private async Task ApplyAsync(IReadOnlyList<RowPlan> plans, CancellationToken cancellationToken)
    {
        var today = GermanTime.Today(clock.GetUtcNow());
        var propertyIds = new Dictionary<string, Guid>();
        var unitIds = new Dictionary<string, Guid>();
        var personIds = new Dictionary<string, Guid>();
        var firstStartOfUnit = plans.Where(p => p.Line.Start is not null).GroupBy(p => Key(p.Line.Property) + "|" + Key(p.Line.Unit))
            .ToDictionary(g => g.Key, g => g.Min(p => p.Line.Start!.Value));

        foreach (var plan in plans)
        {
            var line = plan.Line;
            var propertyKey = Key(line.Property);
            var unitKey = propertyKey + "|" + Key(line.Unit);
            string Where(string what) => $"Zeile {line.Line} ({line.Property}, {line.Unit}): {what}";

            Guid propertyId;
            if (plan.PropertyId is { } existing)
            {
                propertyId = existing;
            }
            else if (!propertyIds.TryGetValue(propertyKey, out propertyId))
            {
                var created = await properties.CreateAsync(new PropertyInput { Name = line.Property.Trim(), Street = line.Street!, PostalCode = line.PostalCode!, City = line.City! }, cancellationToken);
                propertyId = Require(created, Where("Objekt"));
                propertyIds[propertyKey] = propertyId;
            }

            Guid unitId;
            if (plan.UnitId is { } existingUnit)
            {
                unitId = existingUnit;
            }
            else if (!unitIds.TryGetValue(unitKey, out unitId))
            {
                var created = await properties.CreateUnitAsync(propertyId, new UnitInput { Name = line.Unit.Trim(), Location = line.Location }, cancellationToken);
                unitId = Require(created, Where("Einheit"));
                unitIds[unitKey] = unitId;
            }

            if (plan.SetArea)
            {
                var from = new DateOnly(Math.Min(firstStartOfUnit.TryGetValue(unitKey, out var first) ? first.Year : today.Year, today.Year) - 1, 1, 1);
                Require(await properties.SetUnitAreaAsync(unitId, new AreaInput { From = from, Area = line.Area }, cancellationToken), Where("Wohnfläche"));
            }

            if (!line.HasTenant)
            {
                continue;
            }

            var personKey = string.IsNullOrWhiteSpace(line.Email) ? Key($"{line.FirstName} {line.LastName}".Trim()) : Key(line.Email);
            var personId = plan.PersonId ?? (personIds.TryGetValue(personKey, out var known) ? known : Guid.Empty);
            if (personId == Guid.Empty)
            {
                personId = Require(await people.CreateAsync(new PersonInput { FirstName = line.FirstName, LastName = line.LastName!, Email = line.Email }, null, cancellationToken), Where("Mieter"));
                personIds[personKey] = personId;
            }

            if (!plan.NewLease)
            {
                continue;
            }

            var leaseId = Require(await leases.CreateAsync(new LeaseCreation
            {
                RentalUnitId = unitId,
                Start = line.Start,
                TenantIds = [personId],
                BaseRent = line.BaseRent,
                OperatingCostPrepayment = line.OperatingCosts,
                HeatingCostPrepayment = line.HeatingCosts,
                People = line.People ?? 1,
                FixedTermUntil = line.FixedUntil,
                Data = new LeaseInput { Deposit = line.Deposit },
            }, cancellationToken), Where("Vertrag"));

            if (line.Model != RentModel.Standard)
            {
                Require(await leases.SetRentModelAsync(leaseId, new RentModelInput { Model = line.Model, IndexBaseMonth = line.IndexMonth, IndexBaseValue = line.IndexValue }, cancellationToken), Where("Mietmodell"));
            }

            foreach (var (from, rent) in line.Steps)
            {
                Require(await leases.AddRentStepAsync(leaseId, new RentStepInput { From = from, BaseRent = rent }, cancellationToken), Where("Staffel"));
            }

            if (DepositReceivedOf(line) is { } received and > 0)
            {
                Require(await deposits.AddAsync(leaseId, new DepositEntryInput
                {
                    Kind = DepositEntryKind.Received,
                    Date = line.Start,
                    Amount = received,
                    Note = "Aus dem Import übernommen",
                }, cancellationToken), Where("Kaution"));
            }

            if (plan.Balance)
            {
                var amount = Math.Abs(line.Balance!.Value);
                Require(await accounts.CreateChargeAsync(leaseId, new ChargeInput
                {
                    Kind = line.Balance > 0 ? ChargeType.BalanceCarriedForward : ChargeType.Refund,
                    Due = line.Start,
                    Amount = amount,
                    Name = line.Balance > 0 ? "Saldovortrag aus der Zeit vor MietHahn" : "Guthaben aus der Zeit vor MietHahn",
                }, cancellationToken), Where("Saldovortrag"));
            }
        }
    }

    private static Guid Require(MietHahn.Application.Common.Result result, string context) =>
        result.Succeeded ? result.Id ?? Guid.Empty : throw new ImportFailedException($"{context}: {string.Join(" ", result.Errors)}");

    private sealed record PersonRef(Guid Id, string? FirstName, string LastName, string? Email);

    private sealed record RowPlan(
        ImportLine Line,
        Guid? PropertyId,
        Guid? UnitId,
        Guid? PersonId,
        bool NewProperty,
        bool NewUnit,
        bool SetArea,
        bool NewPerson,
        bool NewLease,
        bool Balance,
        string? Tenant,
        List<string> Actions,
        List<string> Errors);

    private sealed class ImportFailedException(string message) : Exception(message);
}
