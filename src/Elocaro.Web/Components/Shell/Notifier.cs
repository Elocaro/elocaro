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

    public void Error(string message) => snackbar.Add(message, Severity.Error);
}
