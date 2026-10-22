using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Admin;
using MietHahn.Application.Common;
using MietHahn.Domain.Admin;
using MietHahn.Domain.Common;
using MietHahn.Domain.Properties;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Admin;

internal sealed class LandlordProfileService(MietHahnDbContext db) : ILandlordProfileService
{
    public async Task<LandlordProfileInput?> GetAsync(CancellationToken cancellationToken = default) =>
        ToInput(await ProfileAsync(cancellationToken));

    public async Task<LandlordProfileInput?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        ToInput(await db.Set<LandlordProfile>().AsNoTracking().SingleOrDefaultAsync(v => v.Id == id, cancellationToken));

    public async Task SaveAsync(LandlordProfileInput data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var profile = await ProfileAsync(cancellationToken);
        await SaveProfileAsync(profile, data, cancellationToken);
    }

    public async Task<string?> EmergencyInfoAsync(Guid propertyId, CancellationToken cancellationToken = default) =>
        (await LandlordProfiles.ForPropertyAsync(db, propertyId, cancellationToken))?.EmergencyInfo;

    public async Task<IReadOnlyList<LandlordProfileRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var profiles = await db.Set<LandlordProfile>().AsNoTracking().OrderBy(v => v.CreatedAt).ToListAsync(cancellationToken);
        var usage = await db.Set<Property>().AsNoTracking().Where(o => o.LandlordProfileId != null).GroupBy(o => o.LandlordProfileId!.Value)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        return profiles.Select((v, i) => new LandlordProfileRow(v.Id, v.Name, v.City, i == 0, usage.GetValueOrDefault(v.Id))).ToList();
    }

    public Task<Result> SaveAsync(Guid? id, LandlordProfileInput data, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(data);
            var profile = id is { } given
                ? await db.Set<LandlordProfile>().SingleOrDefaultAsync(v => v.Id == given, cancellationToken) ?? throw new NotFoundException()
                : null;
            if (profile is null && !await db.Set<LandlordProfile>().AnyAsync(cancellationToken))
            {
                throw new RuleViolationException("Bitte zuerst das Standardprofil anlegen.");
            }

            return (await SaveProfileAsync(profile, data, cancellationToken)).Id;
        });

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var profile = await db.Set<LandlordProfile>().SingleOrDefaultAsync(v => v.Id == id, cancellationToken) ?? throw new NotFoundException();
            if (await ProfileAsync(cancellationToken) is { } standard && standard.Id == id)
            {
                throw new RuleViolationException("Das Standardprofil lässt sich nicht löschen.");
            }

            if (await db.Set<Property>().AnyAsync(o => o.LandlordProfileId == id, cancellationToken))
            {
                throw new RuleViolationException("Das Profil wird noch von einem Objekt verwendet.");
            }

            db.Remove(profile);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    private static LandlordProfileInput? ToInput(LandlordProfile? profile) => profile is null ? null : new LandlordProfileInput
    {
        Name = profile.Name,
        AddressLine2 = profile.AddressLine2,
        Street = profile.Street,
        PostalCode = profile.PostalCode,
        City = profile.City,
        Phone = profile.Phone,
        Email = profile.Email,
        Iban = profile.Iban,
        Bic = profile.Bic,
        Bank = profile.Bank,
        TaxNumber = profile.TaxNumber,
        EmergencyInfo = profile.EmergencyInfo,
    };

    private async Task<LandlordProfile> SaveProfileAsync(LandlordProfile? profile, LandlordProfileInput data, CancellationToken cancellationToken)
    {
        if (profile is null)
        {
            profile = new LandlordProfile(data.Name.Trim(), data.Street.Trim(), data.PostalCode.Trim(), data.City.Trim());
            db.Add(profile);
        }

        profile.Name = data.Name.Trim();
        profile.AddressLine2 = TrimToNull(data.AddressLine2);
        profile.Street = data.Street.Trim();
        profile.PostalCode = data.PostalCode.Trim();
        profile.City = data.City.Trim();
        profile.Phone = TrimToNull(data.Phone);
        profile.Email = TrimToNull(data.Email);
        profile.Iban = FormatIban(data.Iban);
        profile.Bic = TrimToNull(data.Bic)?.ToUpperInvariant();
        profile.Bank = TrimToNull(data.Bank);
        profile.TaxNumber = TrimToNull(data.TaxNumber);
        profile.EmergencyInfo = TrimToNull(data.EmergencyInfo);
        profile.Validate();
        await db.SaveChangesAsync(cancellationToken);
        return profile;
    }

    /// <summary>„de021203…“ → „DE02 1203 0000 …“ (groups of four, as is usual on documents).</summary>
    internal static string? FormatIban(string? iban) => TrimToNull(iban) is { } value ? Domain.Common.Iban.Format(value) : null;

    private Task<LandlordProfile?> ProfileAsync(CancellationToken cancellationToken) =>
        db.Set<LandlordProfile>().OrderBy(v => v.CreatedAt).FirstOrDefaultAsync(cancellationToken);

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
