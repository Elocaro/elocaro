using System.ComponentModel.DataAnnotations;
using Elocaro.Domain.Common;

namespace Elocaro.Application.Common;

/// <summary>Empty or a creditor identifier (Gläubiger-ID) with valid check digits (spaces and lowercase allowed).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class CreditorIdAttribute : ValidationAttribute
{
    public CreditorIdAttribute()
        : base("Bitte eine gültige Gläubiger-Identifikationsnummer angeben (z. B. DE98ZZZ09999999999; Prüfziffer stimmt nicht).")
    {
    }

    public override bool IsValid(object? value) =>
        value is null || (value is string text && (string.IsNullOrWhiteSpace(text) || CreditorId.IsValid(text)));
}
