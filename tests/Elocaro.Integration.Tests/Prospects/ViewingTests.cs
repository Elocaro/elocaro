using Elocaro.Application.Common;
using Elocaro.Application.Deadlines;
using Elocaro.Application.Properties;
using Elocaro.Application.Prospects;
using Elocaro.Domain.Prospects;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Prospects;

public class ViewingTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;
    private Guid unit;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero));
        var property = Validate(await With<IPropertyService, Result>(s => s.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)));
        unit = Validate(await With<IPropertyService, Result>(s => s.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1" }, Ct)));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    private Task<Guid> AddSlotAsync(int day, int hour) =>
        With<IViewings, Guid>(async s => Validate(await s.AddAsync(new ViewingInput { RentalUnitId = unit, Day = new DateOnly(2026, 11, day), Time = new TimeOnly(hour, 0), Minutes = 30 }, Ct)));

    private async Task<string> LinkAsync()
    {
        var link = await With<IViewings, ViewingLinkResult>(s => s.CreateLinkAsync(unit, Ct));
        Assert.True(link.Result.Succeeded);
        var url = link.Url!;
        return url[(url.LastIndexOf('/', StringComparison.Ordinal) + 1)..];
    }

    [Fact]
    public async Task A_prospect_books_a_free_date_over_the_link_and_the_date_is_gone_for_others()
    {
        var first = await AddSlotAsync(20, 10);
        await AddSlotAsync(20, 11);
        var token = await LinkAsync();

        var offer = (await With<IViewings, ViewingOffer?>(s => s.OfferAsync(token, Ct)))!;
        Assert.Equal(("Wohnung 1, Lindenweg 3", 2), (offer.Unit, offer.Slots.Count));

        Validate(await With<IViewings, Result>(s => s.BookAsync(token, new BookingInput { SlotId = first, Name = "Ben Weber", Contact = "ben@example.org" }, Ct)));

        Assert.Single((await With<IViewings, ViewingOffer?>(s => s.OfferAsync(token, Ct)))!.Slots);
        Assert.False((await With<IViewings, Result>(s => s.BookAsync(token, new BookingInput { SlotId = first, Name = "Cora Klein", Contact = "0221 1" }, Ct))).Succeeded);
        var prospect = Assert.Single(await With<IProspects, IReadOnlyList<ProspectRow>>(s => s.ListAsync(null, Ct)));
        Assert.Equal(("Ben Weber", "ben@example.org", ProspectStatus.Viewing, new DateOnly(2026, 11, 20)), (prospect.Name, prospect.Email, prospect.Status, prospect.ViewingOn));
        var row = (await With<IViewings, IReadOnlyList<ViewingRow>>(s => s.ListAsync(null, Ct))).Single(r => r.Id == first);
        Assert.Equal(("Ben Weber", prospect.Id), (row.ProspectName, row.ProspectId));
        Assert.False((await With<IViewings, Result>(s => s.DeleteAsync(first, Ct))).Succeeded);
    }

    [Fact]
    public async Task The_calendar_file_has_the_time_in_utc_and_a_new_link_withdraws_the_old_one()
    {
        var slot = await AddSlotAsync(20, 10); // 10:00 in Germany (CET) = 09:00 UTC
        var token = await LinkAsync();

        var ics = await With<IViewings, string?>(s => s.CalendarFileAsync(token, slot, Ct));

        Assert.Contains("DTSTART:20261120T090000Z", ics, StringComparison.Ordinal);
        Assert.Contains("DTEND:20261120T093000Z", ics, StringComparison.Ordinal);
        Assert.Contains("SUMMARY:Besichtigung Wohnung 1", ics, StringComparison.Ordinal);
        await LinkAsync();
        Assert.Null(await With<IViewings, ViewingOffer?>(s => s.OfferAsync(token, Ct)));
        Assert.Null(await With<IViewings, string?>(s => s.CalendarFileAsync("nonsense", slot, Ct)));
    }

    [Fact]
    public async Task Dates_in_the_past_and_a_free_date_that_is_deleted_are_handled()
    {
        var past = await With<IViewings, Result>(s => s.AddAsync(new ViewingInput { RentalUnitId = unit, Day = new DateOnly(2026, 11, 15), Time = new TimeOnly(10, 0) }, Ct));
        var slot = await AddSlotAsync(20, 10);

        Assert.False(past.Succeeded);
        Validate(await With<IViewings, Result>(s => s.DeleteAsync(slot, Ct)));
        Assert.Empty(await With<IViewings, IReadOnlyList<ViewingRow>>(s => s.ListAsync(null, Ct)));
    }

    [Fact]
    public async Task A_booked_viewing_appears_in_the_deadline_calendar()
    {
        var slot = await AddSlotAsync(20, 10);
        var token = await LinkAsync();
        Validate(await With<IViewings, Result>(s => s.BookAsync(token, new BookingInput { SlotId = slot, Name = "Ben Weber", Contact = "0221 1" }, Ct)));

        var entries = await With<IEnumerable<IDeadlineSource>, List<DeadlineEntry>>(async sources =>
        {
            var all = new List<DeadlineEntry>();
            foreach (var source in sources)
            {
                all.AddRange(await source.DeadlinesAsync(new DateOnly(2026, 12, 31), Ct));
            }

            return all;
        });

        var entry = Assert.Single(entries, e => e.Key == $"viewing:{slot}");
        Assert.Equal((new DateOnly(2026, 11, 20), true), (entry.Due, entry.Title.Contains("Ben Weber", StringComparison.Ordinal)));
    }

    private async Task<T> With<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }
}
