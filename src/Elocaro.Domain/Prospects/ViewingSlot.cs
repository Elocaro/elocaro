using System.Security.Cryptography;
using System.Text;
using Elocaro.Domain.Common;

namespace Elocaro.Domain.Prospects;

/// <summary>
/// A viewing date offered for a rental unit. A prospect books it over the link of the unit; the booking creates the <see cref="Prospect"/>.
/// The booking is released with the prospect: if the data of the prospect is deleted, the date stays but is free of any name.
/// </summary>
public sealed class ViewingSlot : Entity
{
    public const int MinMinutes = 10;
    public const int MaxMinutes = 240;

    private ViewingSlot()
    {
    }

    public ViewingSlot(Guid rentalUnitId, DateTimeOffset startsAt, int minutes)
    {
        if (minutes is < MinMinutes or > MaxMinutes)
        {
            throw new RuleViolationException($"Die Dauer muss zwischen {MinMinutes} und {MaxMinutes} Minuten liegen.");
        }

        RentalUnitId = rentalUnitId;
        StartsAt = startsAt;
        Minutes = minutes;
    }

    public Guid RentalUnitId { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public int Minutes { get; private set; }

    public DateTimeOffset EndsAt => StartsAt.AddMinutes(Minutes);

    /// <summary>The prospect who booked the date; <c>null</c> = free (also after the prospect's data was deleted).</summary>
    public Guid? ProspectId { get; private set; }

    public bool IsFree => ProspectId is null;

    public void Book(Guid prospectId) => ProspectId = IsFree ? prospectId : throw new RuleViolationException("Dieser Termin ist schon vergeben.");

    public void Release() => ProspectId = null;
}

/// <summary>The secret link with which prospects see and book the free dates of a unit (only its hash is stored).</summary>
public sealed class ViewingLink : Entity
{
    public const int Days = 60;

    private ViewingLink()
    {
        TokenHash = "";
    }

    public ViewingLink(Guid rentalUnitId, string tokenHash, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        RentalUnitId = rentalUnitId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
    }

    public Guid RentalUnitId { get; private set; }

    public string TokenHash { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsOpen(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    public void Revoke(DateTimeOffset at) => RevokedAt ??= at;

    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
