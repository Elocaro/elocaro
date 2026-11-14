using System.Data.Common;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Web.Tests.Account;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Web.Tests.Operations;

/// <summary>
/// The restore page under load: every query takes a while, so the page is rendered while its first query is still running.
/// Its children must not use the same database context at that moment (a second operation on one context fails).
/// </summary>
public class RestoreSetupSlowDatabaseTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    protected override void ConfigureServices(IServiceCollection services) =>
        services.ConfigureDbContext<ElocaroDbContext>(options => options.AddInterceptors(new SlowQueries()));

    [Fact]
    public async Task Restore_page_renders_while_the_database_answers_slowly()
    {
        using var browser = App.Browser();

        var page = await browser.GetAsync("/konto/einrichtung/wiederherstellen");

        Assert.Contains("Einrichtungscode", page.Text, StringComparison.Ordinal);
        Assert.Contains("Sicherungen suchen", page.Text, StringComparison.Ordinal);
    }

    private sealed class SlowQueries : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await Task.Delay(50, cancellationToken);
            return result;
        }
    }
}
