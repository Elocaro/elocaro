using Elocaro.Application.Common;
using Elocaro.Application.Leases;
using Elocaro.Application.Payments;
using Elocaro.Domain.Common;
using Elocaro.Domain.Leases;
using Elocaro.Domain.Payments;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Payments;

internal sealed class DirectDebitMandates(ElocaroDbContext db, ILeaseService leases, TimeProvider clock) : IDirectDebitMandates
{
    public async Task<IReadOnlyList<MandateRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var today = GermanTime.Today(clock.GetUtcNow());
        var mandates = await db.Set<DirectDebitMandate>().AsNoTracking().ToListAsync(cancellationToken);
        var names = (await leases.ListAsync(false, cancellationToken)).ToDictionary(l => l.Id);
        return [.. mandates
            .Select(m =>
            {
                names.TryGetValue(m.LeaseId, out var lease);
                return new MandateRow(m.Id, m.LeaseId, lease?.RentalUnit ?? "", lease?.Property ?? "", lease?.Tenant ?? "", m.Reference, m.DebtorName, Ending(m.DebtorIban), m.SignedOn, m.Sequence, m.RevokedOn, m.IsActiveOn(today));
            })
            .OrderByDescending(r => r.Active).ThenBy(r => r.Tenant, StringComparer.CurrentCultureIgnoreCase).ThenByDescending(r => r.SignedOn)];
    }

    public async Task<MandateInput?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.Set<DirectDebitMandate>().AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, cancellationToken) is { } m
            ? new MandateInput { Reference = m.Reference, DebtorName = m.DebtorName, DebtorIban = m.DebtorIban, DebtorBic = m.DebtorBic, SignedOn = m.SignedOn, Sequence = m.Sequence, Note = m.Note }
            : null;

    public async Task<IReadOnlyList<MandateLeaseOption>> LeaseOptionsAsync(CancellationToken cancellationToken = default) =>
        [.. (await leases.ListAsync(false, cancellationToken)).Where(l => l.Status != LeaseStatus.Ended).OrderBy(l => l.Property, StringComparer.CurrentCultureIgnoreCase).ThenBy(l => l.RentalUnit, StringComparer.CurrentCultureIgnoreCase)
            .Select(l => new MandateLeaseOption(l.Id, $"{l.RentalUnit}, {l.Property} – {l.Tenant}"))];

    public Task<Result> CreateAsync(Guid leaseId, MandateInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            if (!await db.Set<Lease>().AnyAsync(l => l.Id == leaseId, cancellationToken))
            {
                throw new NotFoundException();
            }

            var signedOn = input.SignedOn ?? throw new RuleViolationException("Bitte das Datum der Unterschrift angeben.");
            var reference = string.IsNullOrWhiteSpace(input.Reference) ? DirectDebitMandate.NewReference(leaseId, signedOn) : input.Reference.Trim();
            if (await db.Set<DirectDebitMandate>().AnyAsync(m => m.Reference == reference, cancellationToken))
            {
                throw new RuleViolationException("Diese Mandatsreferenz gibt es schon. Eine Referenz darf nur einmal vergeben werden.");
            }

            var mandate = new DirectDebitMandate(leaseId, reference, input.DebtorName.Trim(), Iban.Format(input.DebtorIban), Bic(input.DebtorBic), signedOn, input.Sequence) { Note = TrimToNull(input.Note) };
            db.Add(mandate);
            await db.SaveChangesAsync(cancellationToken);
            return mandate.Id;
        });

    public Task<Result> UpdateAsync(Guid id, MandateInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var mandate = await db.Set<DirectDebitMandate>().SingleOrDefaultAsync(m => m.Id == id, cancellationToken) ?? throw new NotFoundException();
            mandate.Change(input.DebtorName.Trim(), Iban.Format(input.DebtorIban), Bic(input.DebtorBic), input.SignedOn ?? throw new RuleViolationException("Bitte das Datum der Unterschrift angeben."), input.Sequence);
            mandate.Note = TrimToNull(input.Note);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public Task<Result> RevokeAsync(Guid id, DateOnly day, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var mandate = await db.Set<DirectDebitMandate>().SingleOrDefaultAsync(m => m.Id == id, cancellationToken) ?? throw new NotFoundException();
            mandate.Revoke(day);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public Task<Result> ReactivateAsync(Guid id, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var mandate = await db.Set<DirectDebitMandate>().SingleOrDefaultAsync(m => m.Id == id, cancellationToken) ?? throw new NotFoundException();
            mandate.Reactivate();
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    private static string Ending(string iban) => "…" + Iban.Compact(iban)[^4..];

    private static string? Bic(string? bic) => TrimToNull(bic)?.ToUpperInvariant();

    private static string? TrimToNull(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
