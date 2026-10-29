using Elocaro.Application.Audit;
using Elocaro.Domain.Common;
using Elocaro.Domain.Files;
using Elocaro.Infrastructure.Audit;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Elocaro.Integration.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Integration.Tests.Audit;

public class AuditLogTests(PostgresFixture postgres) : PersistenceTestBase(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Creation_logs_all_set_fields()
    {
        var entry = await CreateAsync(new TestEntry
        {
            Name = "EG links",
            Amount = 650m,
            Validity = new Period(new DateOnly(2026, 1, 1)),
            Tags = ["WG", "Balkon"],
        });

        var log = Assert.Single(await HistoryAsync(entry.Id));
        Assert.Equal(AuditAction.Created, log.Action);
        Assert.Equal("test", log.User);
        Assert.Equal(StartTime, log.Timestamp);
        Assert.Contains(new FieldChange("Name", null, "EG links"), log.Changes);
        Assert.Contains(new FieldChange("Amount", null, "650"), log.Changes);
        Assert.Contains(new FieldChange("Validity.From", null, "2026-01-01"), log.Changes);
        Assert.Contains(new FieldChange("Tags", null, "WG, Balkon"), log.Changes);
        Assert.DoesNotContain(log.Changes, a => a.Field is "Iban" or "Validity.To" or "CreatedAt" or "Version");
    }

    [Fact]
    public async Task Change_logs_old_and_new_value_with_user()
    {
        var entry = await CreateAsync(new TestEntry { Name = "Kaltmiete", Amount = 650m });

        Clock.Advance(TimeSpan.FromDays(1));
        User.Identifier = "partnerin";
        await using (var db = NewContext())
        {
            var loaded = await db.Entries.SingleAsync(e => e.Id == entry.Id, Ct);
            loaded.Amount = 700m;
            loaded.Validity = loaded.Validity.EndOn(new DateOnly(2026, 12, 31));
            await db.SaveChangesAsync(Ct);
        }

        var change = (await HistoryAsync(entry.Id))[0];
        Assert.Equal(AuditAction.Modified, change.Action);
        Assert.Equal("partnerin", change.User);
        Assert.Equal(StartTime.AddDays(1), change.Timestamp);
        Assert.Equal(
            [new FieldChange("Amount", "650", "700"), new FieldChange("Validity.To", null, "2026-12-31")],
            change.Changes.OrderBy(a => a.Field, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Saving_without_real_change_creates_no_entry()
    {
        var entry = await CreateAsync(new TestEntry { Name = "unverändert", Amount = 1m });

        await using (var db = NewContext())
        {
            var loaded = await db.Entries.SingleAsync(e => e.Id == entry.Id, Ct);
            loaded.Amount = 1m;
            loaded.Tags = [];
            await db.SaveChangesAsync(Ct);
        }

        Assert.Single(await HistoryAsync(entry.Id));
    }

    [Fact]
    public async Task Confidential_fields_are_masked()
    {
        var entry = await CreateAsync(new TestEntry { Name = "Konto", Iban = "DE02120300000000202051" });

        await using (var db = NewContext())
        {
            (await db.Entries.SingleAsync(e => e.Id == entry.Id, Ct)).Iban = "DE89370400440532013000";
            await db.SaveChangesAsync(Ct);
        }

        var history = await HistoryAsync(entry.Id);
        Assert.Contains(new FieldChange("Iban", AuditCapture.Masked, AuditCapture.Masked), history[0].Changes);
        Assert.Contains(new FieldChange("Iban", null, AuditCapture.Masked), history[1].Changes);
        await using var check = NewContext();
        var raw = await check.Set<AuditEntry>().Select(a => a.ChangesJson).ToListAsync(Ct);
        Assert.DoesNotContain(raw, j => j.Contains("DE02", StringComparison.Ordinal) || j.Contains("DE89", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Delete_is_logged_as_deletion()
    {
        var entry = await CreateAsync(new TestEntry { Name = "weg" });

        await using (var db = NewContext())
        {
            db.Entries.Remove(await db.Entries.SingleAsync(e => e.Id == entry.Id, Ct));
            await db.SaveChangesAsync(Ct);
        }

        var deletion = (await HistoryAsync(entry.Id))[0];
        Assert.Equal(AuditAction.Deleted, deletion.Action);
        Assert.Empty(deletion.Changes);
    }

    [Fact]
    public async Task Technical_entities_are_not_logged()
    {
        await using (var db = NewContext())
        {
            db.Add(new Elocaro.Domain.Communication.OutgoingMail(["a@example.de"], "Betreff", "<p/>", "", StartTime));
            db.Add(new StoredFile("receipt.pdf", "application/pdf", 10, new string('a', 64)));
            await db.SaveChangesAsync(Ct);
        }

        await using var check = NewContext();
        var types = await check.Set<AuditEntry>().Select(a => a.EntityType).ToListAsync(Ct);
        Assert.Equal(["StoredFile"], types);
    }

    [Fact]
    public async Task Audit_entry_is_rolled_back_with_the_change()
    {
        await using var db = NewContext();
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        db.Entries.Add(new TestEntry { Name = "abgebrochen" });
        await db.SaveChangesAsync(Ct);
        await transaction.RollbackAsync(Ct);

        await using var check = NewContext();
        Assert.False(await check.Set<AuditEntry>().AnyAsync(Ct));
    }

    private async Task<TestEntry> CreateAsync(TestEntry entry)
    {
        await using var db = NewContext();
        db.Entries.Add(entry);
        await db.SaveChangesAsync(Ct);
        return entry;
    }

    private async Task<IReadOnlyList<AuditEntryView>> HistoryAsync(Guid id)
    {
        await using var db = NewContext();
        return await new ChangeHistory(db).GetAsync(nameof(TestEntry), id, Ct);
    }
}
