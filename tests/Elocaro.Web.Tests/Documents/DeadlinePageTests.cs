using Elocaro.Application.Accounts;
using Elocaro.Application.Deadlines;
using Elocaro.Domain.Deadlines;
using Elocaro.Web.Tests.Account;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Web.Tests.Documents;

public class DeadlinePageTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Calendar_lists_the_maintenance_and_the_task_page_shows_its_history()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var today = Elocaro.Domain.Common.GermanTime.Today(App.Clock.GetUtcNow());
        var id = await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IDeadlines>().CreateTaskAsync(
            new TaskInput { Title = "Schornsteinfeger", Kind = TaskKind.ChimneySweep, DueOn = today.AddDays(5), IntervalMonths = 12 }, Ct)).Id!.Value);

        var calendar = await admin.GetAsync("/verwaltung/fristen");
        var tasks = await admin.GetAsync("/verwaltung/fristen/wartung");
        var details = await admin.GetAsync($"/verwaltung/fristen/wartung/{id}");

        Assert.Contains("Schornsteinfeger", calendar.Text, StringComparison.Ordinal);
        Assert.Contains("Schornsteinfeger", tasks.Text, StringComparison.Ordinal);
        Assert.Contains("Als erledigt eintragen", details.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Calendar_feed_is_public_but_only_with_the_secret_link()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        await App.WithScopeAsync(sp => sp.GetRequiredService<IDeadlines>().RenewCalendarLinkAsync(true, Ct));
        var path = (await App.WithScopeAsync(sp => sp.GetRequiredService<IDeadlines>().CalendarPathAsync(Ct)))!;

        using var anonymous = App.Browser();
        using var feed = await anonymous.Http.GetAsync(new Uri(path, UriKind.Relative), Ct);
        using var wrong = await anonymous.Http.GetAsync(new Uri("/kalender/falsch.ics", UriKind.Relative), Ct);

        feed.EnsureSuccessStatusCode();
        Assert.Equal("text/calendar", feed.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("BEGIN:VCALENDAR", await feed.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, wrong.StatusCode);
    }

    [Fact]
    public async Task Tenants_cannot_open_the_deadline_pages()
    {
        var (tenant, _) = await SignedInAsAsync(Roles.Tenant);
        using var _ = tenant;

        var page = await tenant.GetAsync("/verwaltung/fristen");

        Assert.DoesNotContain("Fristen und Wartung", page.Text, StringComparison.Ordinal);
    }
}
