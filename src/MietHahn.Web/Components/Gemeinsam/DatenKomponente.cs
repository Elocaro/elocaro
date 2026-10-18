using Microsoft.AspNetCore.Components;

namespace MietHahn.Web.Components.Gemeinsam;

/// <summary>
/// Basis für Unterkomponenten, die selbst Daten laden (z. B. Änderungshistorie). Beim statischen Rendern starten mehrere
/// Unterkomponenten ihre asynchronen Ladevorgänge gleichzeitig; ein gemeinsamer (scoped) DbContext erlaubt aber nur eine
/// Abfrage zur Zeit. Daher bekommt jeder Ladevorgang einen eigenen DI-Scope und damit einen eigenen DbContext.
/// </summary>
public abstract class DatenKomponente : ComponentBase
{
    [Inject]
    private IServiceScopeFactory ScopeFabrik { get; set; } = default!;

    protected async Task<T> LadenAsync<TDienst, T>(Func<TDienst, Task<T>> laden)
        where TDienst : notnull
    {
        ArgumentNullException.ThrowIfNull(laden);
        await using var scope = ScopeFabrik.CreateAsyncScope();
        return await laden(scope.ServiceProvider.GetRequiredService<TDienst>());
    }
}
