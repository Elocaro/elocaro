using System.Security.Cryptography;
using System.Text;
using Elocaro.Domain.Common;

namespace Elocaro.Domain.Prospects;

/// <summary>
/// The self-disclosure of a prospect: a link without account (only its hash is stored) and, once the prospect has filled it in, the answers.
/// Only what a landlord needs to choose between applicants is asked; the consent is part of the answers. It lives and dies with the prospect.
/// </summary>
public sealed class SelfDisclosure : Entity
{
    public const int DefaultDays = 14;
    public const int MaxDays = 60;

    private SelfDisclosure()
    {
        TokenHash = "";
    }

    public SelfDisclosure(Guid prospectId, string tokenHash, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ProspectId = prospectId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
    }

    public Guid ProspectId { get; private set; }

    public string TokenHash { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    /// <summary>When the prospect agreed to the processing (the text they saw is the one of the form at that time).</summary>
    public DateTimeOffset? ConsentAt { get; private set; }

    public int? Persons { get; private set; }

    public string? Occupation { get; private set; }

    /// <summary>Monthly net household income in euros; optional.</summary>
    public decimal? NetIncome { get; private set; }

    public string? Pets { get; private set; }

    public DateOnly? MoveInOn { get; private set; }

    public string? Message { get; private set; }

    public bool IsOpen(DateTimeOffset now) => RevokedAt is null && SubmittedAt is null && now < ExpiresAt;

    public void Revoke(DateTimeOffset at) => RevokedAt ??= at;

    public void Submit(DateTimeOffset now, bool consent, int persons, string? occupation, decimal? netIncome, string? pets, DateOnly? moveInOn, string? message)
    {
        if (!IsOpen(now))
        {
            throw new RuleViolationException("Dieser Link ist abgelaufen oder wurde schon benutzt.");
        }

        if (!consent)
        {
            throw new RuleViolationException("Ohne Ihre Einwilligung können wir die Angaben nicht speichern.");
        }

        if (persons is < 1 or > 20)
        {
            throw new RuleViolationException("Bitte die Zahl der einziehenden Personen angeben (1 bis 20).");
        }

        if (netIncome is < 0)
        {
            throw new RuleViolationException("Das Einkommen darf nicht negativ sein.");
        }

        ConsentAt = now;
        SubmittedAt = now;
        Persons = persons;
        Occupation = occupation;
        NetIncome = netIncome;
        Pets = pets;
        MoveInOn = moveInOn;
        Message = message;
    }

    /// <summary>A fresh unguessable secret (256 bits, URL-safe).</summary>
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
