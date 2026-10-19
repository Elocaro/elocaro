using Microsoft.AspNetCore.Components;

namespace MietHahn.Web.Components.Common;

/// <summary>
/// Base for child components that load their own data (e.g. change history). During static rendering several
/// child components start their asynchronous loads at the same time; a shared (scoped) DbContext only allows one
/// query at a time. Therefore each load gets its own DI scope and thus its own DbContext.
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
