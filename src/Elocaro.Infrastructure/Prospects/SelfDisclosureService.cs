using Elocaro.Application.Admin;
using Elocaro.Application.Audit;
using Elocaro.Application.Common;
using Elocaro.Application.Leases;
using Elocaro.Application.Prospects;
using Elocaro.Domain.Common;
using Elocaro.Domain.Prospects;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Elocaro.Infrastructure.Prospects;

internal sealed class SelfDisclosureService(ElocaroDbContext db, ILeaseService leases, ILandlordProfileService landlord, IAccessLog accessLog, IOptions<ApplicationOptions> app, TimeProvider clock) : ISelfDisclosures
{
    public async Task<DisclosureLink> CreateLinkAsync(Guid prospectId, int days, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        if (!await db.Set<Prospect>().AnyAsync(p => p.Id == prospectId, cancellationToken))
        {
            return new DisclosureLink(Command.NotFound);
        }

        foreach (var open in await db.Set<SelfDisclosure>().Where(s => s.ProspectId == prospectId && s.SubmittedAt == null && s.RevokedAt == null).ToListAsync(cancellationToken))
        {
            open.Revoke(now);
        }

        var token = SelfDisclosure.NewToken();
        var expires = now.AddDays(Math.Clamp(days, 1, SelfDisclosure.MaxDays));
        var disclosure = new SelfDisclosure(prospectId, SelfDisclosure.Hash(token), expires);
        db.Add(disclosure);
        await db.SaveChangesAsync(cancellationToken);
        return new DisclosureLink(Result.Ok(disclosure.Id), app.Value.Link($"/selbstauskunft/{token}").ToString(), expires);
    }

    public async Task<DisclosureView?> GetAsync(Guid prospectId, CancellationToken cancellationToken = default)
    {
        var s = await db.Set<SelfDisclosure>().AsNoTracking().Where(x => x.ProspectId == prospectId).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (s is null)
        {
            return null;
        }

        if (s.SubmittedAt is not null)
        {
            await accessLog.LogAsync(nameof(Prospect), prospectId, "Selbstauskunft eines Interessenten angesehen", cancellationToken);
        }

        return new DisclosureView(s.Id, s.IsOpen(clock.GetUtcNow()), s.ExpiresAt, s.SubmittedAt, s.Persons, s.Occupation, s.NetIncome, s.Pets, s.MoveInOn, s.Message);
    }

    public async Task<DisclosureForm?> ForFormAsync(string token, CancellationToken cancellationToken = default)
    {
        if (await FindOpenAsync(token, false, cancellationToken) is not { } found)
        {
            return null;
        }

        var unit = (await leases.RentalUnitsAsync(cancellationToken)).FirstOrDefault(u => u.Id == found.Prospect.RentalUnitId);
        var profile = await landlord.GetAsync(cancellationToken);
        var controller = profile is null
            ? ""
            : string.Join(", ", new[] { profile.Name, profile.Street, $"{profile.PostalCode} {profile.City}", profile.Email, profile.Phone }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return new DisclosureForm(unit is null ? "" : $"{unit.Name}, {unit.Property}", found.Disclosure.ExpiresAt, controller, Prospect.RetentionMonthsAfterRejection);
    }

    public Task<Result> SubmitAsync(string token, DisclosureInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var (disclosure, prospect) = await FindOpenAsync(token, true, cancellationToken) ?? throw new RuleViolationException("Dieser Link ist abgelaufen oder wurde schon benutzt.");
            var now = clock.GetUtcNow();
            disclosure.Submit(now, input.Persons ?? 0, TrimToNull(input.Occupation), input.NetIncome, TrimToNull(input.Pets), input.MoveInOn, TrimToNull(input.Message));
            if (prospect.Status is ProspectStatus.Inquiry or ProspectStatus.Viewing)
            {
                prospect.SetStatus(ProspectStatus.SelfDisclosure, GermanTime.Today(now), Prospect.RetentionMonthsAfterRejection);
            }

            await db.SaveChangesAsync(cancellationToken);
            return disclosure.Id;
        });

    /// <summary>The self-disclosure and prospect behind a token: unknown, expired, withdrawn and used links all look the same.</summary>
    private async Task<(SelfDisclosure Disclosure, Prospect Prospect)?> FindOpenAsync(string token, bool tracked, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100)
        {
            return null;
        }

        var hash = SelfDisclosure.Hash(token);
        var disclosures = tracked ? db.Set<SelfDisclosure>() : db.Set<SelfDisclosure>().AsNoTracking();
        var disclosure = await disclosures.SingleOrDefaultAsync(s => s.TokenHash == hash, cancellationToken);
        if (disclosure is null || !disclosure.IsOpen(clock.GetUtcNow()))
        {
            return null;
        }

        var prospects = tracked ? db.Set<Prospect>() : db.Set<Prospect>().AsNoTracking();
        var prospect = await prospects.SingleOrDefaultAsync(p => p.Id == disclosure.ProspectId, cancellationToken);
        return prospect is null || prospect.Status is ProspectStatus.Accepted or ProspectStatus.Declined ? null : (disclosure, prospect);
    }

    private static string? TrimToNull(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
