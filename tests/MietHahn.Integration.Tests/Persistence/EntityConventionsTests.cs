using Microsoft.EntityFrameworkCore;
using MietHahn.Domain.Common;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Persistence;

public class EntityConventionsTests(PostgresFixture postgres) : PersistenceTestBase(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Entity_is_saved_and_read_with_audit_fields()
    {
        var entry = new TestEntry { Name = "Erdgeschoss links", Amount = 812.50m, Validity = new(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)) };
        await using (var db = NewContext())
        {
            db.Entries.Add(entry);
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = NewContext())
        {
            var read = await db.Entries.SingleAsync(e => e.Id == entry.Id, Ct);

            Assert.Equal("Erdgeschoss links", read.Name);
            Assert.Equal(812.50m, read.Amount);
            Assert.Equal(new Period(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), read.Validity);
            Assert.Equal(StartTime, read.CreatedAt);
            Assert.Equal("test", read.CreatedBy);
            Assert.Null(read.ModifiedAt);
            Assert.NotEqual(0u, read.Version);
        }
    }

    [Fact]
    public void Id_is_time_sortable_Guid_v7()
    {
        var entry = new TestEntry { Name = "x" };

        Assert.Equal(7, entry.Id.Version);
    }

    [Fact]
    public async Task Change_sets_ModifiedAt_and_ModifiedBy()
    {
        var entry = new TestEntry { Name = "alt" };
        await using (var db = NewContext())
        {
            db.Entries.Add(entry);
            await db.SaveChangesAsync(Ct);
        }

        Clock.Advance(TimeSpan.FromHours(2));
        User.Identifier = "partnerin";
        await using (var db = NewContext())
        {
            var loaded = await db.Entries.SingleAsync(e => e.Id == entry.Id, Ct);
            loaded.Name = "neu";
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = NewContext())
        {
            var read = await db.Entries.SingleAsync(e => e.Id == entry.Id, Ct);
            Assert.Equal(StartTime.AddHours(2), read.ModifiedAt);
            Assert.Equal("partnerin", read.ModifiedBy);
            Assert.Equal(StartTime, read.CreatedAt);
            Assert.Equal("test", read.CreatedBy);
        }
    }

    [Fact]
    public async Task Delete_is_soft_and_hides_the_record()
    {
        var entry = new TestEntry { Name = "weg" };
        await using (var db = NewContext())
        {
            db.Entries.Add(entry);
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = NewContext())
        {
            var loaded = await db.Entries.SingleAsync(e => e.Id == entry.Id, Ct);
            db.Entries.Remove(loaded);
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = NewContext())
        {
            Assert.False(await db.Entries.AnyAsync(e => e.Id == entry.Id, Ct));

            var stillPresent = await db.Entries.IgnoreQueryFilters().SingleAsync(e => e.Id == entry.Id, Ct);
            Assert.True(stillPresent.Deleted);
            Assert.Equal(StartTime, stillPresent.ModifiedAt);
        }
    }

    [Fact]
    public async Task Concurrent_change_is_detected_as_conflict()
    {
        var entry = new TestEntry { Name = "start" };
        await using (var db = NewContext())
        {
            db.Entries.Add(entry);
            await db.SaveChangesAsync(Ct);
        }

        await using var first = NewContext();
        await using var second = NewContext();
        var fromFirst = await first.Entries.SingleAsync(e => e.Id == entry.Id, Ct);
        var fromSecond = await second.Entries.SingleAsync(e => e.Id == entry.Id, Ct);

        fromFirst.Name = "erster";
        await first.SaveChangesAsync(Ct);

        fromSecond.Name = "zweiter";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Money_amounts_are_stored_as_numeric_18_2()
    {
        await using var db = NewContext();

        var type = await db.Database
            .SqlQuery<string>($"SELECT format_type(atttypid, atttypmod) AS \"Value\" FROM pg_attribute WHERE attrelid = 'test_entries'::regclass AND attname = 'amount'")
            .SingleAsync(Ct);

        Assert.Equal("numeric(18,2)", type);
    }

    [Fact]
    public async Task Names_are_sorted_in_German_order()
    {
        await using (var db = NewContext())
        {
            db.Entries.AddRange(
                new TestEntry { Name = "Zander" },
                new TestEntry { Name = "Öztürk" },
                new TestEntry { Name = "Ahrens" },
                new TestEntry { Name = "Ohm" },
                new TestEntry { Name = "Ärmel" });
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = NewContext())
        {
            var names = await db.Entries.OrderBy(e => e.Name).Select(e => e.Name).ToListAsync(Ct);

            Assert.Equal(["Ahrens", "Ärmel", "Ohm", "Öztürk", "Zander"], names);
        }
    }
}
