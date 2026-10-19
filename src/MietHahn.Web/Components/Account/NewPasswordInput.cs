using System.ComponentModel.DataAnnotations;

namespace MietHahn.Web.Components.Account;

/// <summary>New password with confirmation (invitation, reset, change).</summary>
public class NewPasswordInput
{
    [Required(ErrorMessage = "Bitte ein Passwort angeben.")]
    [StringLength(200, MinimumLength = 12, ErrorMessage = "Das Passwort muss mindestens 12 Zeichen lang sein.")]
    public string Password { get; set; } = "";

    [Compare(nameof(Password), ErrorMessage = "Die Passwörter stimmen nicht überein.")]
    public string PasswordConfirmation { get; set; } = "";
}
