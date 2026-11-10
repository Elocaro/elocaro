using MudBlazor;

namespace Elocaro.Web.Components.Shell;

/// <summary>
/// Short feedback after an action in an interactive page (replacement for the status cookie of redirects). Pages use this
/// service, not the snackbar of the component library.
/// </summary>
public sealed class Notifier(ISnackbar snackbar)
{
    public void Success(string message) => snackbar.Add(message, Severity.Success);

    public void Info(string message) => snackbar.Add(message, Severity.Info);

    public void Warning(string message) => snackbar.Add(message, Severity.Warning);

    /// <summary>Confirmation with a "Rückgängig" action for a change that can be reversed without loss (the snackbar stays a little longer).</summary>
    public void Undoable(string message, Func<Task> undo) => snackbar.Add(message, Severity.Success, options =>
    {
        options.Action = "Rückgängig";
        options.ActionVariant = Variant.Text;
        options.RequireInteraction = false;
        options.VisibleStateDuration = 8000;
        options.OnClick = async _ => await undo();
    });

    public void Error(string message) => snackbar.Add(message, Severity.Error);
}
