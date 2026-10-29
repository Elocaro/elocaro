using Elocaro.Application.Bank;
using Elocaro.Application.Common;
using Elocaro.Domain.Bank;
using Elocaro.Domain.Common;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Persistence;
using libfintx.FinTS;
using libfintx.FinTS.Camt;
using libfintx.FinTS.Data;
using libfintx.Globals;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elocaro.Infrastructure.Bank.FinTs;

internal sealed partial class FinTsService(
    ElocaroDbContext db,
    IDataProtectionProvider protection,
    IOptions<FinTsOptions> options,
    FinTsSessions sessions,
    TimeProvider clock,
    ILogger<FinTsService> logger) : IFinTsService
{
    internal const string ProtectionPurpose = "Elocaro.FinTs.UserId";

    public Task<Result> StartAsync(Guid bankAccountId, string pin, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            if (string.IsNullOrEmpty(pin))
            {
                throw new RuleViolationException("Bitte die PIN eingeben.");
            }

            if (to < from)
            {
                throw new RuleViolationException("Das Ende des Zeitraums liegt vor dem Anfang.");
            }

            var account = await db.Set<BankAccount>().AsNoTracking().SingleOrDefaultAsync(k => k.Id == bankAccountId, cancellationToken) ?? throw new NotFoundException();
            if (!account.HasFinTs)
            {
                throw new RuleViolationException("Für dieses Konto ist FinTS nicht eingerichtet (Konto bearbeiten).");
            }

            if (sessions.IsRunning(bankAccountId))
            {
                throw new RuleViolationException("Für dieses Konto läuft bereits ein Abruf.");
            }

            // Process-wide in libfintx; sent with every dialog initialization (HKVVB). The version field holds at most 5 characters.
            FinTsGlobals.ProductId = options.Value.EffectiveProductId;
            FinTsGlobals.Version = string.IsNullOrWhiteSpace(options.Value.ProductVersion) ? "1.0" : options.Value.ProductVersion.Trim()[..Math.Min(5, options.Value.ProductVersion.Trim().Length)];

            var details = new ConnectionDetails
            {
                Url = account.FinTsUrl,
                FinTSVersion = FinTsVersion.v300,
                UserId = Unprotect(account.FinTsUserId!),
                Pin = pin,
                Blz = int.Parse(account.FinTsBankCode!, System.Globalization.CultureInfo.InvariantCulture),
                Iban = account.Iban,
                Bic = account.Bic,
                Account = GermanAccountNumber(account.Iban),
            };

            var session = new FinTsSession(account.Id, account.Iban, clock.GetUtcNow());
            var id = sessions.Add(session, clock.GetUtcNow());
            session.Cancellation.CancelAfter(TimeSpan.FromSeconds(Math.Max(60, options.Value.TanTimeoutSeconds)));

            // From here on only the bank is contacted; the database is not touched.
            _ = Task.Run(() => RunAsync(session, details, from, to), CancellationToken.None);
            return id;
        });

    public async Task<FinTsFetchState?> StateAsync(Guid fetchId, CancellationToken cancellationToken = default)
    {
        var session = sessions.Find(fetchId);
        if (session is null)
        {
            return null;
        }

        if (session.Result is { } transactions && session.State.Phase != FinTsPhase.Completed)
        {
            var import = new BankImport(session.BankAccountId, $"FinTS-Abruf {clock.GetLocalNow():yyyy-MM-dd HH-mm}", FinTsPayload.Write(session.Iban, transactions), BankFormat.FinTs, null);
            db.Add(import);
            await db.SaveChangesAsync(cancellationToken);
            session.Set(new FinTsFetchState(FinTsPhase.Completed, null, import.Id));
        }

        return session.State;
    }

    public void ProvideTan(Guid fetchId, string tan)
    {
        if (!string.IsNullOrWhiteSpace(tan))
        {
            sessions.Find(fetchId)?.Answer(tan.Trim());
        }
    }

    public void ConfirmApproval(Guid fetchId) => sessions.Find(fetchId)?.Answer("");

    public void Cancel(Guid fetchId) => sessions.Find(fetchId)?.Cancellation.Cancel();

    internal static string GermanAccountNumber(string iban) => iban.StartsWith("DE", StringComparison.Ordinal) && iban.Length == 22 ? iban[12..].TrimStart('0') : "";

    private string Unprotect(string value) => protection.CreateProtector(ProtectionPurpose).Unprotect(value);

    private async Task RunAsync(FinTsSession session, ConnectionDetails details, DateOnly from, DateOnly to)
    {
        var token = session.Cancellation.Token;
        try
        {
            // No logger factory of the application: libfintx must never log the dialog (PIN, TAN) anywhere.
            var client = new FinTsClient(details, false, new MemoryBpdStore(), NullLoggerFactory.Instance);
            var dialog = new TANDialog(async d =>
            {
                var message = Describe(d.DialogResult?.Messages ?? []);
                return d.IsDecoupled
                    ? await session.AskAsync(FinTsPhase.ApprovalRequired, message.Length > 0 ? message : "Bitte den Abruf in der Banking-App freigeben.", token)
                    : await session.AskAsync(FinTsPhase.TanRequired, message, token);
            });

            var sync = await client.Synchronization();
            Check(sync);
            var accounts = await client.Accounts(dialog);
            Check(accounts);
            client.activeAccount = accounts.Data.FirstOrDefault(a => a.AccountIban is { } i && Iban.Compact(i) == session.Iban)
                ?? throw new FinTsFailure("Die Bank kennt dieses Konto nicht unter der angegebenen Benutzerkennung (IBAN prüfen).");

            var statements = await client.Transactions_camt(dialog, CamtVersion.Camt053, from.ToDateTime(TimeOnly.MinValue), to.ToDateTime(TimeOnly.MinValue), false);
            Check(statements);
            session.Succeed(FinTsMapping.FromCamt(statements.Data ?? []));
        }
        catch (FinTsFailure ex)
        {
            session.Set(new FinTsFetchState(FinTsPhase.Failed, ex.Message));
        }
        catch (OperationCanceledException)
        {
            session.Set(new FinTsFetchState(FinTsPhase.Failed, "Der Abruf wurde abgebrochen oder hat zu lange auf die TAN gewartet."));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Only the type: messages of the library can contain parts of the dialog.
            LogFailed(logger, ex.GetType().Name);
            session.Set(new FinTsFetchState(FinTsPhase.Failed, "Die Verbindung zur Bank ist fehlgeschlagen. Bitte URL, Bankleitzahl und Benutzerkennung prüfen oder später noch einmal versuchen."));
        }
        finally
        {
            details.Pin = "";
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "FinTS-Abruf fehlgeschlagen ({ExceptionType})")]
    private static partial void LogFailed(ILogger logger, string exceptionType);

    private static void Check(HBCIDialogResult result)
    {
        if (result.IsSuccess)
        {
            return;
        }

        var errors = result.Messages.Where(m => m.IsError).ToList();
        var text = Describe(errors.Count > 0 ? errors : result.Messages);
        // 9931/9942/9930: login or PIN rejected. Exactly one attempt is made, so the account is not blocked.
        var credentials = errors.Any(m => m.Code is "9930" or "9931" or "9942");
        throw new FinTsFailure((text.Length > 0 ? text : "Die Bank hat den Abruf abgelehnt.")
            + (credentials ? " Es wurde nur ein Versuch unternommen; bitte Benutzerkennung und PIN im Onlinebanking prüfen, bevor es erneut versucht wird." : ""));
    }

    private static string Describe(IEnumerable<HBCIBankMessage> messages) =>
        string.Join(' ', messages.Select(m => m.Message?.Trim()).Where(m => !string.IsNullOrEmpty(m)));

    private sealed class FinTsFailure(string message) : Exception(message);
}

/// <summary>Bank parameter data are not kept between retrievals.</summary>
internal sealed class MemoryBpdStore : libfintx.FinTS.BankParameterData.IBpdStore
{
    public Task<int?> GetBPDVersion(int bankCountry, int bankCode) => Task.FromResult<int?>(null);

    public Task<string?> GetBPD(int bankCountry, int bankCode) => Task.FromResult<string?>(null);

    public Task SaveBPD(int bankCountry, int bankCode, string bpd) => Task.CompletedTask;

    public Task DeleteBPD(int bankCountry, int bankCode) => Task.CompletedTask;
}
