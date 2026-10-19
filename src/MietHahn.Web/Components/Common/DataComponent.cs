using Microsoft.AspNetCore.Components;

namespace MietHahn.Web.Components.Common;

/// <summary>
/// Basis für Unterkomponenten, die selbst Daten laden (z. B. Änderungshistorie). Beim statischen Rendern starten mehrere
/// Unterkomponenten ihre asynchronen Ladevorgänge gleichzeitig; ein gemeinsamer (scoped) DbContext erlaubt aber nur eine
/// Abfrage zur Zeit. Daher bekommt jeder Ladevorgang einen eigenen DI-Scope und damit einen eigenen DbContext.
/// </summary>
public abstract class DataComponent : ComponentBase
{
    [Inject]
    private IServiceScopeFactory ScopeFactory { get; set; } = default!;

    protected async Task<T> GetAsync<TService, T>(Func<TService, Task<T>> load)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(load);
        await using var scope = ScopeFactory.CreateAsyncScope();
        return await load(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
