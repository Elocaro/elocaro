using System.ComponentModel.DataAnnotations;
using MietHahn.Application.Common;
using MietHahn.Domain.People;

namespace MietHahn.Application.People;

public sealed class PersonInput
{
    public Salutation Salutation { get; set; }

    [StringLength(50)]
    public string? Title { get; set; }

    [StringLength(100)]
    public string? FirstName { get; set; }

    [Required(ErrorMessage = "Bitte den Nachnamen bzw. Firmennamen angeben.")]
    [StringLength(200)]
    public string LastName { get; set; } = "";

    [StringLength(200)]
    public string? Company { get; set; }

    public ContactType ContactType { get; set; }

    [StringLength(100)]
    public string? Occupation { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    [OptionalEmail]
    [StringLength(254)]
    public string? Email { get; set; }

    [StringLength(50)]
    public string? Phone { get; set; }

    [StringLength(50)]
    public string? Mobile { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }
}

public sealed class AddressInput
{
    [Required(ErrorMessage = "Bitte angeben, ab wann die Anschrift gilt.")]
    public DateOnly? From { get; set; }

    [StringLength(200)]
    public string? AddressLine2 { get; set; }

    [Required(ErrorMessage = "Bitte Straße und Hausnummer angeben.")]
    [StringLength(200)]
    public string Street { get; set; } = "";

    [Required(ErrorMessage = "Bitte die Postleitzahl angeben.")]
    [StringLength(10)]
    public string PostalCode { get; set; } = "";

    [Required(ErrorMessage = "Bitte den Ort angeben.")]
    [StringLength(100)]
    public string City { get; set; } = "";

    [StringLength(100)]
    public string? Country { get; set; }
}

public sealed class BankDetailsInput
{
    [Required(ErrorMessage = "Bitte die IBAN angeben.")]
    [Iban]
    public string Iban { get; set; } = "";

    [StringLength(200)]
    public string? AccountHolder { get; set; }

    [StringLength(11)]
    public string? Bic { get; set; }
}
