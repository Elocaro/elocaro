using System.ComponentModel.DataAnnotations;

namespace Elocaro.Application.Common;

/// <summary>
/// Like <see cref="EmailAddressAttribute"/>, but empty is allowed. Forms submit <c>""</c> for empty fields,
/// which <see cref="EmailAddressAttribute"/> rejects as invalid.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class OptionalEmailAttribute : ValidationAttribute
{
    private static readonly EmailAddressAttribute Email = new();

    public OptionalEmailAttribute()
        : base("Bitte eine gültige E-Mail-Adresse angeben.")
    {
    }

    public override bool IsValid(object? value) =>
        value is null || (value is string text && (string.IsNullOrWhiteSpace(text) || Email.IsValid(text.Trim())));
}
