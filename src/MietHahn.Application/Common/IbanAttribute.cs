using System.ComponentModel.DataAnnotations;
using MietHahn.Domain.Common;

namespace MietHahn.Application.Common;

/// <summary>Empty or an IBAN with a valid checksum (spaces and lowercase allowed).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class IbanAttribute : ValidationAttribute
{
    public IbanAttribute()
        : base("Bitte eine gültige IBAN angeben (Prüfsumme stimmt nicht).")
    {
    }

    public override bool IsValid(object? value) =>
        value is null || (value is string text && (string.IsNullOrWhiteSpace(text) || Iban.IsValid(text)));
}
