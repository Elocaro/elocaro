using Elocaro.Domain.Common;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Infrastructure.Persistence.MigrationHelpers;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Elocaro.Integration.Tests.Persistence;

public class OverlapProtectionTests(PostgresFixture postgres) : PersistenceTestBase(postgres)
{
    private static readonly Guid Apartment1 = Guid.CreateVersion7();
    private static readonly Guid Apartment2 = Guid.CreateVersion7();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var db = NewContext();
        var sql = OverlapProtectionExtensions.CreateSql("ex_test_entries_group_validity", "test_entries", "validity_from", "validity_to", "group_id");
        await db.Database.ExecuteSqlRawAsync(sql, Ct);
    }

    [Fact]
    public void Sql_quotes_identifiers_and_ignores_deleted_rows()
    {
        var sql = OverlapProtectionExtensions.CreateSql("ex", "leases", "term_from", "term_to", "rental_unit_id");

        Assert.Equal(
            "ALTER TABLE \"leases\" ADD CONSTRAINT \"ex\" EXCLUDE USING gist " +
            "(\"rental_unit_id\" WITH =, daterange(\"term_from\", \"term_to\", '[]') WITH &&) WHERE (NOT deleted) DEFERRABLE INITIALLY DEFERRED;",
            sql);
    }

    [Fact]
    public async Task Non_overlapping_periods_are_allowed()
    {
        await StoreAsync(
            Entry(Apartment1, new(2025, 1, 1), new(2025, 12, 31)),
            Entry(Apartment1, new(2026, 1, 1), null));
    }

    [Fact]
    public async Task Overlapping_periods_of_the_same_group_are_rejected()
    {
        await StoreAsync(Entry(Apartment1, new(2025, 1, 1), new(2025, 12, 31)));

        var errors = await Assert.ThrowsAnyAsync<Exception>(
            () => StoreAsync(Entry(Apartment1, new(2025, 12, 31), null)));

        Assert.True(DatabaseErrors.IsOverlap(errors), errors.ToString());
    }

    [Fact]
    public async Task Open_ended_period_blocks_later_entries()
    {
        await StoreAsync(Entry(Apartment1, new(2025, 1, 1), null));

        var errors = await Assert.ThrowsAnyAsync<Exception>(
            () => StoreAsync(Entry(Apartment1, new(2030, 1, 1), new(2030, 6, 30))));

        Assert.True(DatabaseErrors.IsOverlap(errors));
    }

    [Fact]
    public async Task Different_groups_may_overlap()
    {
        await StoreAsync(
            Entry(Apartment1, new(2025, 1, 1), null),
            Entry(Apartment2, new(2025, 1, 1), null));
    }

    [Fact]
    public async Task Soft_deleted_entries_do_not_block()
    {
        var old = Entry(Apartment1, new(2025, 1, 1), null);
        await StoreAsync(old);

        await using (var db = NewContext())
        {
            db.Entries.Remove(await db.Entries.SingleAsync(e => e.Id == old.Id, Ct));
            await db.SaveChangesAsync(Ct);
        }

        await StoreAsync(Entry(Apartment1, new(2025, 6, 1), null));
    }

    [Fact]
    public async Task Continuing_series_in_one_save_is_allowed()
    {
        var old = Entry(Apartment1, new(2025, 1, 1), null);
        await StoreAsync(old);

        // New version and ending of the old one in a single SaveChanges: the check happens only on commit.
        await using var db = NewContext();
        var loaded = await db.Entries.SingleAsync(e => e.Id == old.Id, Ct);
        db.Entries.Add(Entry(Apartment1, new(2026, 1, 1), null));
        loaded.Validity = new Period(new(2025, 1, 1), new(2025, 12, 31));
        await db.SaveChangesAsync(Ct);

        Assert.Equal(2, await db.Entries.CountAsync(Ct));
    }

    [Fact]
    public void Other_database_errors_are_not_overlaps()
    {
        Assert.False(DatabaseErrors.IsOverlap(new InvalidOperationException()));
        Assert.False(DatabaseErrors.IsOverlap(new DbUpdateException("x", new InvalidOperationException())));
    }

    private static TestEntry Entry(Guid group, DateOnly from, DateOnly? to) =>
        new() { Name = "Vertrag", GroupId = group, Validity = new Period(from, to) };

    private async Task StoreAsync(params TestEntry[] entries)
    {
        await using var db = NewContext();
        db.Entries.AddRange(entries);
        await db.SaveChangesAsync(Ct);
    }
}
