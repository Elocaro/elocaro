using Microsoft.JSInterop;

namespace Elocaro.Web.Components.Shell;

/// <summary>
/// JS interop calls of interactive components. When the browser has closed the tab or the connection is lost, the circuit is
/// gone and every call throws <see cref="JSDisconnectedException"/>; there is nobody left to show anything to, so these methods
/// swallow exactly that exception and return <c>default</c>. Errors of the script itself (<see cref="JSException"/>) are not
/// caught: they are bugs and must reach the log.
/// </summary>
public static class JsInterop
{
    /// <summary>Loads an ES module; <c>null</c> when the circuit is gone.</summary>
    public static async ValueTask<IJSObjectReference?> TryImportAsync(this IJSRuntime js, string path)
    {
        try
        {
            return await js.InvokeAsync<IJSObjectReference>("import", path);
        }
        catch (JSDisconnectedException)
        {
            return null;
        }
    }

    /// <summary>Calls a function without result; <c>false</c> when the circuit is gone.</summary>
    public static async ValueTask<bool> TryInvokeVoidAsync(this IJSObjectReference js, string identifier, params object?[] args)
    {
        try
        {
            await js.InvokeVoidAsync(identifier, args);
            return true;
        }
        catch (JSDisconnectedException)
        {
            return false;
        }
    }

    /// <summary>Calls a function with a result; <c>default</c> when the circuit is gone.</summary>
    public static async ValueTask<T?> TryInvokeAsync<T>(this IJSObjectReference js, string identifier, params object?[] args)
    {
        try
        {
            return await js.InvokeAsync<T>(identifier, args);
        }
        catch (JSDisconnectedException)
        {
            return default;
        }
    }
}
