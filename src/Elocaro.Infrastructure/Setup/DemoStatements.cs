using Elocaro.Application.Common;
using Elocaro.Application.Documents;
using Elocaro.Application.OperatingCosts;
using Elocaro.Application.Receipts;
using Elocaro.Application.Statements;
using Elocaro.Domain.Common;
using Elocaro.Domain.OperatingCosts;
using Elocaro.Domain.Properties;
using Elocaro.Domain.Statements;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elocaro.Infrastructure.Setup;

/// <summary>
/// Sent statements of the last two calendar years on the demo instance, so that visitors see the derivation, the comparison with
/// the previous year and the released receipts in the tenant portal. A demo without them is still usable: errors are only logged.
/// </summary>
internal static partial class DemoStatements
{
    private const int PdfServiceAttempts = 10;

    private static readonly TimeSpan PdfServiceDelay = TimeSpan.FromSeconds(3);

    private static readonly (string CostType, string Supplier, decimal Amount)[] Costs =
    [
        ("betrkv-01", "Stadt Köln, Steueramt", 1240m),
        ("betrkv-02", "Stadtwerke Köln, Wasser", 1980m),
        ("betrkv-08", "Abfallwirtschaft Köln", 860m),
        ("betrkv-09", "Glanz Gebäudereinigung", 2380m),
        ("betrkv-11", "Stadtwerke Köln, Strom", 310m),
        ("betrkv-13", "Rheinland Versicherung", 1160m),
    ];

    public static async Task CreateAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var pdf = provider.GetRequiredService<IPdfGenerator>();
        if (!await PdfServiceIsReadyAsync(pdf, cancellationToken))
        {
            StatementsSkipped(logger, "Der PDF-Dienst antwortet nicht.");
            return;
        }

        var propertyId = await provider.GetRequiredService<ElocaroDbContext>().Set<Property>().OrderBy(p => p.CreatedAt).Select(p => p.Id).FirstAsync(cancellationToken);
        var allocation = provider.GetRequiredService<ICostAllocation>();
        var year = GermanTime.Today(provider.GetRequiredService<TimeProvider>().GetUtcNow()).Year - 1;
        foreach (var costType in Costs.Select(c => c.CostType))
        {
            var rule = await allocation.SetRuleAsync(propertyId, new CostRuleInput { CostType = costType, From = new DateOnly(year - 1, 1, 1), Key = AllocationKey.LivingArea }, cancellationToken);
            if (!rule.Succeeded)
            {
                StatementsSkipped(logger, string.Join(" ", rule.Errors));
                return;
            }
        }

        // The costs rise by a tenth: the comparison with the previous year has something to show.
        var previous = await CreateYearAsync(provider, pdf, propertyId, year - 1, 0.9m, false, cancellationToken);
        var current = previous.Succeeded ? await CreateYearAsync(provider, pdf, propertyId, year, 1m, true, cancellationToken) : previous;
        if (!current.Succeeded)
        {
            StatementsSkipped(logger, string.Join(" ", current.Errors));
        }
    }

    private static async Task<Result> CreateYearAsync(IServiceProvider provider, IPdfGenerator pdf, Guid propertyId, int year, decimal factor, bool shareReceipts, CancellationToken cancellationToken)
    {
        var receipts = provider.GetRequiredService<IReceiptService>();
        var created = new List<Guid>();
        foreach (var (costType, supplier, amount) in Costs)
        {
            var gross = Math.Round(amount * factor, 2);
            List<Guid> files = [];
            if (shareReceipts)
            {
                var file = await pdf.GenerateAndStoreAsync(
                    new PdfRequest(null, $"Rechnung {supplier}", TemplateText: $"<p>Beispielbeleg der Demo: {supplier}, Jahresrechnung {year}.</p>"),
                    $"Beispielbeleg-{costType}-{year}.pdf",
                    cancellationToken);
                files.Add(file.Id);
            }

            var receipt = await receipts.CreateAsync(
                new ReceiptInput { SupplierName = supplier, InvoiceNumber = $"DEMO-{year}-{costType[^2..]}", InvoiceDate = new DateOnly(year, 12, 15), Gross = gross },
                files,
                new CostItemInput { CostType = costType, PropertyId = propertyId, From = new DateOnly(year, 1, 1), To = new DateOnly(year, 12, 31) },
                cancellationToken);
            if (!receipt.Succeeded)
            {
                return receipt;
            }

            created.Add(receipt.Id!.Value);
        }

        var released = await provider.GetRequiredService<IStatements>().ReleaseAsync(propertyId, Period.CalendarYear(year), true, null, cancellationToken);
        if (!released.Succeeded)
        {
            return released;
        }

        var statementId = released.Id!.Value;
        var dispatch = provider.GetRequiredService<IStatementDispatch>();
        var overview = await dispatch.OverviewAsync(statementId, cancellationToken);
        foreach (var tenancy in overview!.Tenancies)
        {
            var sent = await dispatch.DispatchAsync(statementId, tenancy.LeaseId, new DispatchInput { Channel = DispatchChannel.Post, ProposePrepayments = false }, cancellationToken);
            if (!sent.Succeeded)
            {
                return sent;
            }
        }

        return shareReceipts
            ? await provider.GetRequiredService<IStatementReceiptShares>().SetSharedAsync(statementId, created, cancellationToken)
            : released;
    }

    /// <summary>The PDF service may still be starting together with the application.</summary>
    private static async Task<bool> PdfServiceIsReadyAsync(IPdfGenerator pdf, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await pdf.GenerateAsync(new PdfRequest(null, "Probe", TemplateText: "<p>Probe</p>"), cancellationToken);
                return true;
            }
            catch (HttpRequestException) when (attempt < PdfServiceAttempts)
            {
                await Task.Delay(PdfServiceDelay, cancellationToken);
            }
            catch (HttpRequestException)
            {
                return false;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Demo-Instanz ohne Beispiel-Abrechnungen: {Reason}")]
    private static partial void StatementsSkipped(ILogger logger, string reason);
}
