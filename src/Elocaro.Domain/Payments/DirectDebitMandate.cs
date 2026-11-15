using Elocaro.Domain.Common;
using Elocaro.Domain.Leases;

namespace Elocaro.Domain.Payments;

public enum MandateSequence
{
    /// <summary>For the rent: the mandate is used again and again (sequence type RCUR; since 2016 it is also allowed for the first collection).</summary>
    Recurring = 0,

    /// <summary>One collection only (sequence type OOFF).</summary>
    OneOff = 1,
}

/// <summary>
/// SEPA core direct debit mandate of a tenant for a lease: who allowed the collection from which account, and when. The mandate reference is unique
/// and goes into every collection. The landlord keeps the signed mandate (as a document) for the legal period; Elocaro keeps the data needed for the file.
/// </summary>
public sealed class DirectDebitMandate : Entity
{
    public const int MaxReference = 35;
    public const int MaxName = 70;

    private const string ReferenceCharacters = "+?/-:().,'";

    private DirectDebitMandate()
    {
        Reference = "";
        DebtorName = "";
        DebtorIban = "";
    }

    public DirectDebitMandate(Guid leaseId, string reference, string debtorName, string debtorIban, string? debtorBic, DateOnly signedOn, MandateSequence sequence)
    {
        LeaseId = leaseId;
        Reference = reference;
        DebtorName = debtorName;
        DebtorIban = debtorIban;
        DebtorBic = debtorBic;
        SignedOn = signedOn;
        Sequence = sequence;
        Validate();
    }

    public Guid LeaseId { get; private set; }

    /// <summary>Unique reference of the mandate (letters, digits and <c>+ ? / - : ( ) . , '</c>), up to 35 characters.</summary>
    public string Reference { get; private set; }

    /// <summary>Name of the account holder as in the mandate (often the tenant, not always).</summary>
    public string DebtorName { get; private set; }

    [Confidential]
    public string DebtorIban { get; private set; }

    public string? DebtorBic { get; private set; }

    /// <summary>Day the mandate was signed.</summary>
    public DateOnly SignedOn { get; private set; }

    public MandateSequence Sequence { get; private set; }

    /// <summary>From this day no more collections (revoked by the tenant or the lease ended); empty = valid.</summary>
    public DateOnly? RevokedOn { get; private set; }

    public string? Note { get; set; }

    public bool IsActiveOn(DateOnly day) => SignedOn <= day && (RevokedOn is null || RevokedOn > day);

    /// <summary>Changes the data of the mandate (e.g. a new account). The reference stays.</summary>
    public void Change(string debtorName, string debtorIban, string? debtorBic, DateOnly signedOn, MandateSequence sequence)
    {
        DebtorName = debtorName;
        DebtorIban = debtorIban;
        DebtorBic = debtorBic;
        SignedOn = signedOn;
        Sequence = sequence;
        Validate();
    }

    public void Revoke(DateOnly day)
    {
        if (day < SignedOn)
        {
            throw new RuleViolationException("Der Widerruf kann nicht vor der Unterschrift liegen.");
        }

        RevokedOn = day;
    }

    public void Reactivate() => RevokedOn = null;

    /// <summary>A reference nobody else has: day of signature and the start of the lease id, e.g. <c>MND-20260105-1F3A9C2B</c>.</summary>
    public static string NewReference(Guid leaseId, DateOnly signedOn) => $"MND-{signedOn:yyyyMMdd}-{leaseId.ToString("N")[..8].ToUpperInvariant()}";

    public static bool IsValidReference(string? reference) =>
        !string.IsNullOrEmpty(reference) && reference.Length <= MaxReference && reference.All(c => char.IsAsciiLetterOrDigit(c) || ReferenceCharacters.Contains(c, StringComparison.Ordinal))
        && !reference.StartsWith('/') && !reference.EndsWith('/') && !reference.Contains("//", StringComparison.Ordinal);

    private void Validate()
    {
        if (!IsValidReference(Reference))
        {
            throw new RuleViolationException("Die Mandatsreferenz darf höchstens 35 Zeichen lang sein und nur Buchstaben, Ziffern und + ? / - : ( ) . , ' enthalten (keine Leerzeichen, kein Umlaut).");
        }

        if (string.IsNullOrWhiteSpace(DebtorName) || DebtorName.Length > 200)
        {
            throw new RuleViolationException("Bitte den Namen des Kontoinhabers angeben.");
        }

        if (!Iban.IsValid(DebtorIban))
        {
            throw new RuleViolationException("Bitte eine gültige IBAN des Kontoinhabers angeben.");
        }
    }
}
