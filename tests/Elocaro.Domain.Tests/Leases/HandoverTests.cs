using Elocaro.Domain.Common;
using Elocaro.Domain.Leases;

namespace Elocaro.Domain.Tests.Leases;

public class HandoverTests
{
    private static HandoverContent MoveIn() => new()
    {
        Rooms = [new() { Name = "Küche", Items = [new() { Component = "Boden", Condition = ItemCondition.Wear }, new() { Component = "Wände", Condition = ItemCondition.Good }] }],
        Defects = [new() { Room = "Bad", Description = "Fliese gesprungen" }],
        Keys = [new() { Kind = "Haustür", Count = 2 }, new() { Kind = "Wohnung", Count = 2 }],
    };

    [Fact]
    public void Only_worse_conditions_and_defects_not_noted_at_move_in_are_new()
    {
        var moveIn = MoveIn();
        var moveOut = new HandoverContent
        {
            Rooms = [new() { Name = "küche", Items = [new() { Component = "Boden", Condition = ItemCondition.Wear }, new() { Component = "Wände", Condition = ItemCondition.Defect, Note = "Löcher" }] }],
            Defects =
            [
                new() { Room = "Bad", Description = "Fliese gesprungen", SourceId = moveIn.Defects[0].Id },
                new() { Room = "Flur", Description = "Kratzer an der Tür" },
            ],
            Keys = [new() { Kind = "Haustür", Count = 2 }, new() { Kind = "Wohnung", Count = 1 }],
        };

        var differences = HandoverComparison.Compare(moveIn, moveOut);

        Assert.Equal(
            [(DifferenceKind.Worse, "küche"), (DifferenceKind.KnownDefect, "Bad"), (DifferenceKind.NewDefect, "Flur"), (DifferenceKind.MissingKeys, "Schlüssel")],
            differences.Select(d => (d.Kind, d.Room)));
        Assert.Contains("Wände: in Ordnung → Mangel (Löcher)", differences[0].Text, StringComparison.Ordinal);
        Assert.Equal("Wohnung: 1 von 2 fehlen", differences[3].Text);
    }

    [Fact]
    public void Unchanged_condition_leaves_only_the_known_defect()
    {
        var moveIn = MoveIn();

        var moveOut = moveIn.Copy();
        foreach (var defect in moveOut.Defects)
        {
            defect.SourceId = defect.Id;
        }

        var differences = HandoverComparison.Compare(moveIn, moveOut);

        Assert.Equal([DifferenceKind.KnownDefect], differences.Select(d => d.Kind));
    }

    [Fact]
    public void A_signed_protocol_cannot_be_changed_or_signed_again()
    {
        var protocol = new HandoverProtocol(Guid.NewGuid(), HandoverType.MoveIn, new DateOnly(2026, 11, 1), MoveIn());

        protocol.Sign("Anna Schmidt", DateTimeOffset.UnixEpoch, null, null, Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(HandoverStatus.Completed, protocol.Status);
        Assert.Throws<RuleViolationException>(() => protocol.Edit(new DateOnly(2026, 11, 2), MoveIn()));
        Assert.Throws<RuleViolationException>(() => protocol.Sign("Anna Schmidt", DateTimeOffset.UnixEpoch, null, null, Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public void Keys_need_a_name_and_meter_readings_must_not_be_negative()
    {
        var protocol = new HandoverProtocol(Guid.NewGuid(), HandoverType.MoveIn, new DateOnly(2026, 11, 1), new HandoverContent());

        Assert.Throws<RuleViolationException>(() => protocol.Edit(new DateOnly(2026, 11, 1), new HandoverContent { Keys = [new() { Kind = "", Count = 1 }] }));
        Assert.Throws<RuleViolationException>(() => protocol.Edit(new DateOnly(2026, 11, 1), new HandoverContent { Meters = [new() { Value = -1 }] }));
        Assert.Throws<RuleViolationException>(() => protocol.Sign(" ", DateTimeOffset.UnixEpoch, null, null, Guid.NewGuid(), Guid.NewGuid()));
    }
}
