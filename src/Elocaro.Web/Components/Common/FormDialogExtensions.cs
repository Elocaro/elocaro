using Elocaro.Application.Common;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Elocaro.Web.Components.Common;

/// <summary>Opens a small form (add or change an entry) as a dialog instead of an unfolding section of the page.</summary>
public static class FormDialogExtensions
{
    /// <summary>
    /// Shows <paramref name="fields"/> bound to <paramref name="model"/>; "save" runs on submit and its errors appear in the dialog.
    /// Returns true when it was saved, so that the page can reload.
    /// </summary>
    public static async Task<bool> ShowFormAsync(
        this IDialogService dialogs, string title, object model, RenderFragment fields, Func<Task<Result>> save,
        string submitText = "Speichern", RenderFragment? note = null)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        var parameters = new DialogParameters
        {
            [nameof(FormDialog.Model)] = model,
            [nameof(FormDialog.Fields)] = fields,
            [nameof(FormDialog.Note)] = note,
            [nameof(FormDialog.Save)] = save,
            [nameof(FormDialog.SubmitText)] = submitText,
        };
        var dialog = await dialogs.ShowAsync<FormDialog>(title, parameters, new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true, CloseOnEscapeKey = true });
        return !(await dialog.Result)!.Canceled;
    }
}
