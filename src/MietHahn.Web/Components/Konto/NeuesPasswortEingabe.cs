using System.ComponentModel.DataAnnotations;

namespace MietHahn.Web.Components.Konto;

/// <summary>Neues Passwort mit Wiederholung (Einladung, Zurücksetzen, Ändern).</summary>
public class NeuesPasswortEingabe
{
    [Required(ErrorMessage = "Bitte ein Passwort angeben.")]
    [StringLength(200, MinimumLength = 12, ErrorMessage = "Das Passwort muss mindestens 12 Zeichen lang sein.")]
    public string Passwort { get; set; } = "";

    [Compare(nameof(Passwort), ErrorMessage = "Die Passwörter stimmen nicht überein.")]
    public string PasswortWiederholung { get; set; } = "";
}
