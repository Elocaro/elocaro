using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Admin;
using MietHahn.Domain.Admin;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Admin;

internal sealed class LandlordProfileService(MietHahnDbContext db) : ILandlordProfileService
{
    public async Task<LandlordProfileInput?> GetAsync(CancellationToken cancellationToken = default)
    {
        var profile = await ProfileAsync(cancellationToken);
        return profile is null ? null : new LandlordProfileInput
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
    }

    public async Task SaveAsync(LandlordProfileInput data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var profile = await ProfileAsync(cancellationToken);
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
    }

    /// <summary>„de021203…“ → „DE02 1203 0000 …“ (groups of four, as is usual on documents).</summary>
    internal static string? FormatIban(string? iban) => TrimToNull(iban) is { } value ? Domain.Common.Iban.Format(value) : null;

    private Task<LandlordProfile?> ProfileAsync(CancellationToken cancellationToken) =>
        db.Set<LandlordProfile>().OrderBy(v => v.CreatedAt).FirstOrDefaultAsync(cancellationToken);

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
