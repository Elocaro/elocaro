using MudBlazor;

namespace Elocaro.Web.Components.Shell;

/// <summary>
/// Confirmation before a risky action in an interactive page (the counterpart of <c>DangerAction</c> on static pages).
/// Pages use this service, not the dialog service of the component library.
/// </summary>
public sealed class Confirmations(IDialogService dialogs)
{
    /// <returns><c>true</c> if the user confirmed.</returns>
    public async Task<bool> AskAsync(string title, string message, string confirmText = "Ja, ausführen") =>
        await dialogs.ShowMessageBoxAsync(title, message, yesText: confirmText, cancelText: "Abbrechen") == true;
}
