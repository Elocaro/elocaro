using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Audit;
using MietHahn.Web.Components.Common;

namespace MietHahn.Web.Tests.Components;

public class ChangeHistoryTests : BunitContext
{
    private static readonly Guid Id = Guid.CreateVersion7();

    [Fact]
    public void Shows_entries_with_action_user_and_field_changes()
    {
        Services.AddSingleton<IChangeHistory>(new FixedHistory(
            new AuditEntryView(new DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.Zero), "partnerin", AuditAction.Modified,
                [new FieldChange("Kaltmiete", "650", "700")], null),
            new AuditEntryView(new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.Zero), "admin", AuditAction.Read, [], "Mieterakte geöffnet")));

        var component = Render<ChangeHistory>(p => p.Add(x => x.EntityType, "Mietvertrag").Add(x => x.EntityId, Id));

        Assert.Contains("Änderungshistorie (2)", component.Find("summary").TextContent, StringComparison.Ordinal);
        var entries = component.FindAll("ol.historie-liste > li");
        Assert.Equal(2, entries.Count);
        Assert.Contains("01.03.2026 10:30", entries[0].TextContent, StringComparison.Ordinal); // MEZ
        Assert.Contains("geändert", entries[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("partnerin", entries[0].TextContent, StringComparison.Ordinal);
        var cells = entries[0].QuerySelectorAll("tbody td").Select(z => z.TextContent).ToList();
        Assert.Equal(["Kaltmiete", "650", "700"], cells);
        Assert.Contains("eingesehen", entries[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("(Mieterakte geöffnet)", entries[1].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_entries_a_hint_is_shown()
    {
        Services.AddSingleton<IChangeHistory>(new FixedHistory());

        var component = Render<ChangeHistory>(p => p.Add(x => x.EntityType, "Mietvertrag").Add(x => x.EntityId, Id));

        Assert.Contains("Keine Einträge.", component.Markup, StringComparison.Ordinal);
    }

    private sealed class FixedHistory(params AuditEntryView[] entries) : IChangeHistory
    {
        public Task<IReadOnlyList<AuditEntryView>> GetAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuditEntryView>>(entries);
    }
}
