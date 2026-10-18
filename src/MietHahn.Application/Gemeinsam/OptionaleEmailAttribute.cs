using System.ComponentModel.DataAnnotations;

namespace MietHahn.Application.Gemeinsam;

/// <summary>
/// Wie <see cref="EmailAddressAttribute"/>, aber leer ist erlaubt. Formulare liefern für leere Felder <c>""</c>,
/// das <see cref="EmailAddressAttribute"/> als ungültig ablehnt.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class OptionaleEmailAttribute : ValidationAttribute
{
    private static readonly EmailAddressAttribute Email = new();

    public OptionaleEmailAttribute()
        : base("Bitte eine gültige E-Mail-Adresse angeben.")
    {
    }

    public override bool IsValid(object? value) =>
        value is null || (value is string text && (string.IsNullOrWhiteSpace(text) || Email.IsValid(text.Trim())));
}
