using System.ComponentModel;
using Elocaro.Application.Accounts;
using Elocaro.Application.Bank;
using Elocaro.Application.Finance;
using Elocaro.Application.OperatingCosts;
using Elocaro.Application.Portfolio;
using Elocaro.Application.Receipts;
using Elocaro.Application.Statements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Elocaro.Web.Mcp.Tools;

/// <summary>Finances: accounts, receipts, bank, statements. Requires the permission "Finanzen" (scope read:finance).</summary>
[McpServerToolType]
[Authorize(Policy = Policies.Finance)]
internal sealed class FinanceTools(
    IPortfolioFigures figures,
    ITenantAccounts accounts,
    IDunning dunning,
    IReceiptService receipts,
    IBankService bank,
    IOperatingCosts operatingCosts,
    IStatements statements,
    IHttpContextAccessor http,
    IOptions<McpOptions> options)
{
    [McpServerTool(Name = "get_portfolio_figures", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Kennzahlen des Bestands: Einheiten, Vermietungsstand, Sollmiete, Zahlungseingang des Monats, Rückstände.")]
    public async Task<PortfolioFigures?> GetPortfolioFiguresAsync(CancellationToken cancellationToken) =>
        await figures.FiguresAsync(http.HttpContext!.User, cancellationToken);

    [McpServerTool(Name = "get_tenant_account", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Zeigt das Mieterkonto eines Mietvertrags: Sollstellungen, Zahlungen, Saldo und Fälliges.")]
    public async Task<TenantAccount?> GetTenantAccountAsync([Description("Id des Mietvertrags")] Guid leaseId, CancellationToken cancellationToken) =>
        await accounts.GetAsync(leaseId, cancellationToken);

    [McpServerTool(Name = "list_open_items", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Listet alle offenen Posten (unbezahlte Sollstellungen) mit Mieter, Fälligkeit, offenem Betrag und Tagen Verzug.")]
    public async Task<McpList<OpenItem>> ListOpenItemsAsync(CancellationToken cancellationToken) =>
        (await accounts.OpenItemsAsync(cancellationToken)).OrderByDescending(i => i.DaysOverdue).ToList().Limit(options);

    [McpServerTool(Name = "get_dunning_overview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Übersicht des Mahnwesens: vorgeschlagene und versandte Mahnungen, Mahnsperren und Kündigungshinweise.")]
    public async Task<DunningOverview> GetDunningOverviewAsync(CancellationToken cancellationToken) =>
        await dunning.OverviewAsync(cancellationToken);

    [McpServerTool(Name = "search_receipts", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Sucht Belege (Rechnungen) nach Jahr, Objekt, Kostenart, Text oder Zahlungsstatus.")]
    public async Task<McpList<ReceiptRow>> SearchReceiptsAsync(
        [Description("Rechnungsjahr (optional)")] int? year = null,
        [Description("Id des Objekts (optional)")] Guid? propertyId = null,
        [Description("Suchtext, z. B. Lieferant oder Rechnungsnummer (optional)")] string? text = null,
        [Description("Nur unbezahlte Belege")] bool unpaid = false,
        [Description("Nur Belege, die noch nicht vollständig auf Kosten verteilt sind")] bool unallocated = false,
        CancellationToken cancellationToken = default) =>
        (await receipts.SearchAsync(new ReceiptQuery(year, propertyId, null, unpaid, unallocated, text), cancellationToken)).Limit(options);

    [McpServerTool(Name = "list_bank_transactions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Listet Kontoumsätze (ohne IBAN der Gegenseite und Mandatsdaten). Mit status 'Open' nur noch nicht zugeordnete.")]
    public async Task<McpList<BankTransactionView>> ListBankTransactionsAsync(
        [Description("Suchtext in Verwendungszweck und Name (optional)")] string? text = null,
        [Description("Nur Umsätze ohne Beleg")] bool withoutReceipt = false,
        CancellationToken cancellationToken = default) =>
        (await bank.TransactionsAsync(new TransactionQuery(null, null, text, withoutReceipt), cancellationToken))
            .Select(t => new BankTransactionView(t.Id, t.BookingDate, t.Amount, t.CounterpartyName, t.Purpose, t.Status.ToString()))
            .ToList()
            .Limit(options);

    [McpServerTool(Name = "preview_operating_costs", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Vorschau der Betriebskostenabrechnung eines Objekts für ein Kalenderjahr. Ändert nichts und gibt nichts frei.")]
    public async Task<OperatingCostPreview?> PreviewOperatingCostsAsync(
        [Description("Id des Objekts")] Guid propertyId,
        [Description("Abrechnungsjahr")] int year,
        CancellationToken cancellationToken) =>
        await operatingCosts.PreviewAsync(propertyId, year, cancellationToken);

    [McpServerTool(Name = "list_statements", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Listet die freigegebenen Abrechnungen eines Objekts.")]
    public async Task<McpList<StatementSummary>> ListStatementsAsync([Description("Id des Objekts")] Guid propertyId, CancellationToken cancellationToken) =>
        (await statements.ListAsync(propertyId, cancellationToken)).Limit(options);
}

/// <summary>A bank transaction without bank details of the other party.</summary>
public sealed record BankTransactionView(Guid Id, DateOnly BookingDate, decimal Amount, string? Counterparty, string Purpose, string Status);
